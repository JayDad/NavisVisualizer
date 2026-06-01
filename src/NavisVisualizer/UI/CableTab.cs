using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;
using Color = System.Drawing.Color;
using View = System.Windows.Forms.View;
using Application = System.Windows.Forms.Application;

namespace NavisVisualizer.UI
{
    public class CableTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<CableNodeData> _nodes = new List<CableNodeData>();
        private Dictionary<CableStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedNodeIds = new List<string>();

        /// <summary>Cable No → ordered list of Node IDs that the cable passes through.</summary>
        private Dictionary<string, List<string>> _cableRoutes
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cable No → representative metadata (first encountered row).</summary>
        private Dictionary<string, CableRecord> _cableMeta
            = new Dictionary<string, CableRecord>(StringComparer.OrdinalIgnoreCase);

        private Document _subscribedDoc;
        private bool _suppressSelectionSync;

        private Button _btnLoad;
        private Label _lblFile;
        private TextBox _txtSearch;
        private CheckBox _chkFocus;
        private bool _focusOn;
        private bool _suppressFocusCheck;
        private CheckBox _chkVisibleOnly;
        private bool _visibleOnly;
        private bool _suppressVisibleCheck;
        private Button _btnRefreshVisible;

        private string _statsBase = "로드된 데이터 없음";
        private string _visDiag = "";
        private TabControl _tabFilter;
        private ListView _nodeList;
        private ListView _cableList;
        private ListView _routeList;   // Cable No 단위 + Route(노드 배열)
        private Button _btnApply;
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<CableStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<CableStage, (Panel, Button, ComboBox, CheckBox)>();

        public CableTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.CableDefaults);
            InitializeComponent();
            this.HandleDestroyed += (s, e) => UnsubscribeSelection();
        }

        private void InitializeComponent()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                Padding = new Padding(4)
            };

            _btnLoad = new Button { Text = "Cable Pull Excel", Dock = DockStyle.Fill, Height = 30 };
            _btnLoad.Click += BtnLoad_Click;
            _lblFile = new Label { Text = "(파일 없음)", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Height = 18 };

            var colorPanel = BuildColorPanel();

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply        = new Button { Text = "적용",           Width = 80  };
            _btnReset        = new Button { Text = "전체 초기화",    Width = 90  };
            _btnViewpoint    = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd          = new Button { Text = "NWD Export",     Width = 110 };
            _chkFocus          = new CheckBox { Text = "필터 포커스", AutoSize = true, Padding = new Padding(6, 5, 6, 0) };
            _chkVisibleOnly    = new CheckBox { Text = "보이는 것만", AutoSize = true, Padding = new Padding(6, 5, 6, 0) };
            _btnRefreshVisible = new Button { Text = "새로고침", Width = 70 };
            _btnApply.Click                += BtnApply_Click;
            _btnReset.Click                += BtnReset_Click;
            _btnViewpoint.Click            += BtnViewpoint_Click;
            _btnNwd.Click                  += BtnNwd_Click;
            _chkFocus.CheckedChanged       += ChkFocus_CheckedChanged;
            _chkVisibleOnly.CheckedChanged += ChkVisibleOnly_CheckedChanged;
            // 새로고침: re-evaluate 보이는 것만 against the *current* section/hide state
            // (there is no auto event for section/visibility changes).
            _btnRefreshVisible.Click       += (s, e) => { if (_visibleOnly) FilterList(); };
            // Top row: action buttons. Bottom row: filter checkboxes + refresh (forced via flow break).
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnViewpoint, _btnNwd, _chkFocus, _chkVisibleOnly, _btnRefreshVisible });
            btnPanel.SetFlowBreak(_btnNwd, true);

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 54 };

            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색(Node/Cable No/Equip No):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 180, Text = "" };
            _txtSearch.TextChanged += (s, e) => { FilterList(); RefreshFocusIfActive(); };
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 출력", Width = 120, Height = 23 };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 22 };
            _tabFilter.TabPages.Add(new TabPage("전체"));
            _tabFilter.TabPages.Add(new TabPage("매칭"));
            _tabFilter.TabPages.Add(new TabPage("미매칭"));
            _tabFilter.SelectedIndexChanged += (s, e) => FilterList();

            _nodeList = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                HideSelection = false,
                Height = 220,
            };
            _nodeList.Columns.Add("Node ID", 220);
            _nodeList.Columns.Add("케이블", 50);
            _nodeList.Columns.Add("Design", 65);
            _nodeList.Columns.Add("Pulled", 65);
            _nodeList.Columns.Add("%", 50);
            _nodeList.Columns.Add("단계", 65);
            _nodeList.Columns.Add("매칭", 40);
            _nodeList.SelectedIndexChanged += NodeList_SelectedIndexChanged;
            _nodeList.ColumnClick += NodeList_ColumnClick;

            _cableList = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                Height = 180,
            };
            _cableList.Columns.Add("Cable No",   170);
            _cableList.Columns.Add("Equip No",   140);
            _cableList.Columns.Add("Route",      40);
            _cableList.Columns.Add("Design",     55);
            _cableList.Columns.Add("Pulled",     55);
            _cableList.Columns.Add("%",          50);
            _cableList.Columns.Add("From",       110);
            _cableList.Columns.Add("To",         110);

            _routeList = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                HideSelection = false,
                Height = 180,
            };
            _routeList.Columns.Add("Cable No",  170);
            _routeList.Columns.Add("Equip No",  140);
            _routeList.Columns.Add("노드수",    50);
            _routeList.Columns.Add("Design",    65);
            _routeList.Columns.Add("Pulled",    65);
            _routeList.Columns.Add("%",         50);
            _routeList.Columns.Add("Route (Node 배열)", 360);
            _routeList.SelectedIndexChanged += RouteList_SelectedIndexChanged;

            layout.Controls.Add(_btnLoad);
            layout.Controls.Add(_lblFile);
            layout.Controls.Add(new Label { Text = "단계 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);
            layout.Controls.Add(searchPanel);
            layout.Controls.Add(_tabFilter);
            layout.Controls.Add(new Label { Text = "Cable 목록 (행 클릭 → 해당 Cable 전체 Route 하이라이트)", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 16 });
            layout.Controls.Add(_routeList);
            layout.Controls.Add(new Label { Text = "Node 목록", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 16 });
            layout.Controls.Add(_nodeList);
            layout.Controls.Add(new Label { Text = "선택된 Node의 Cable 상세", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 16 });
            layout.Controls.Add(_cableList);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var allStages = new[] { CableStage.NotStarted }.Concat(CableStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < allStages.Length; i++)
            {
                var stage = allStages[i];
                var setting = _colorSettings[stage];
                string label = CableStageInfo.Labels[stage];

                var chk = new CheckBox { Text = label, Checked = true, AutoSize = true };
                var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var cs = stage;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[cs].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[cs].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(cs.ToString());
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        _colorSettings[cs].Transparency = pct / 100.0;
                        IncrementalUpdate(cs.ToString());
                    }
                };

                _colorRows[stage] = (colorBox, colorBtn, transparencyBox, chk);

                int row = i / 2;
                int colOffset = (i % 2) * 4;
                layout.Controls.Add(chk, colOffset + 0, row);
                layout.Controls.Add(colorBox, colOffset + 1, row);
                layout.Controls.Add(colorBtn, colOffset + 2, row);
                layout.Controls.Add(transparencyBox, colOffset + 3, row);
            }

            panel.Controls.Add(layout);
            return panel;
        }

        private void IncrementalUpdate(string stageKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData) return;
            if (Enum.TryParse<CableStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, stageKey, setting);
        }

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Cable Pull Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _nodes = ExcelLoader.LoadCablePull(dlg.FileName);
                    _lblFile.Text = Path.GetFileName(dlg.FileName);
                    _matchedNodeIds.Clear();
                    _unmatchedNodeIds.Clear();
                    BuildCableRoutes();
                    FilterList();
                    UpdateStats();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Excel 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_nodes.Count == 0) { MessageBox.Show("Excel을 먼저 로드하세요."); return; }
            var lines = new List<string>();
            lines.Add("Node ID,Cable Count,Total Design,Total Pulled,Overall %,Stage,Matched");
            foreach (var n in _nodes)
            {
                var stage = n.GetStage();
                string stageLabel = CableStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedNodeIds.Count == 0 || _matchedNodeIds.Contains(n.NodeId);
                string pct = n.OverallProgress.HasValue ? $"{n.OverallProgress.Value * 100:0}%" : "";
                lines.Add($"\"{n.NodeId}\",{n.Cables.Count},{n.TotalDesignLth:0.##},{n.TotalPulledLth:0.##},\"{pct}\",\"{stageLabel}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"CablePull_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
            MessageBox.Show($"저장 완료: {path}");
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            _main.CableBoxSearcher.BuildIndexForBoxes(doc);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _nodes.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.CableBoxSearcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<CableStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var result = _main.OverrideEngine.ApplyCable(doc, _nodes, activeSettings);

            _unmatchedNodeIds = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedNodeIds = new HashSet<string>(
                _nodes.Select(n => n.NodeId).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            int hiddenCount = _main.OverrideEngine.HideUnmatchedCableBoxes(doc, _matchedNodeIds);

            _tabFilter.TabPages[0].Text = $"전체 ({_nodes.Count})";
            _tabFilter.TabPages[1].Text = $"매칭 ({_matchedNodeIds.Count})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedNodeIds.Count})";

            SetFocusChecked(false);

            UpdateStats(result, hiddenCount);
            FilterList();
            SubscribeSelection(doc);
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.Reset(doc);
            SetFocusChecked(false);
            SetVisibleOnlyChecked(false);
            _statsBase = "전체 초기화 완료";
            _visDiag = "";
            RefreshStatsLabel();
        }

        private void ChkFocus_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressFocusCheck) return;

            var doc = _main.GetDocument();
            if (_chkFocus.Checked)
            {
                if (doc == null || _matchedNodeIds.Count == 0)
                {
                    MessageBox.Show("먼저 적용을 실행하세요.");
                    SetFocusChecked(false);
                    return;
                }
                var hits = GetCurrentFilterHitNodeIds();
                if (hits.Count == 0)
                {
                    MessageBox.Show("현재 필터에 일치하는 Node가 없습니다.");
                    SetFocusChecked(false);
                    return;
                }
                _main.OverrideEngine.SetCableFilterFocus(doc, hits);
                _focusOn = true;
            }
            else
            {
                if (doc != null) _main.OverrideEngine.ClearCableFilterFocus(doc);
                _focusOn = false;
            }
        }

        /// <summary>Set the focus checkbox state without firing the CheckedChanged side effects.</summary>
        private void SetFocusChecked(bool value)
        {
            _suppressFocusCheck = true;
            _chkFocus.Checked = value;
            _suppressFocusCheck = false;
            _focusOn = value;
        }

        private void ChkVisibleOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressVisibleCheck) return;

            if (_chkVisibleOnly.Checked)
            {
                var doc = _main.GetDocument();
                if (doc == null || !_main.CableBoxSearcher.IsIndexBuilt)
                {
                    MessageBox.Show("먼저 적용을 실행해 박스 인덱스를 빌드하세요.");
                    SetVisibleOnlyChecked(false); // revert without re-entering
                    return;
                }
            }
            _visibleOnly = _chkVisibleOnly.Checked;
            FilterList();
        }

        /// <summary>Set the checkbox state without firing the CheckedChanged side effects.</summary>
        private void SetVisibleOnlyChecked(bool value)
        {
            _suppressVisibleCheck = true;
            _chkVisibleOnly.Checked = value;
            _suppressVisibleCheck = false;
            _visibleOnly = value;
        }

        /// <summary>
        /// Node IDs whose box (a point marker; we test its bounding-box center) is
        /// currently on screen: not hidden AND inside every active section plane.
        /// A node with multiple boxes counts as visible if ANY box center is visible.
        /// Returns null when the filter is off or cannot be evaluated (→ no filtering).
        /// </summary>
        private HashSet<string> ComputeVisibleNodeIds()
        {
            _visDiag = "";
            if (!_visibleOnly) return null;

            var doc = _main.GetDocument();
            if (doc == null || !_main.CableBoxSearcher.IsIndexBuilt)
            {
                _visDiag = "보이는것만: 모델/인덱스 없음 → 필터 미적용";
                return null;
            }

            var planes = _main.SectionSvc.GetActiveClipPlanes(doc);

            var search = _main.CableBoxSearcher.FindBySpoolIds(
                _nodes.Select(n => CableNodeData.NormalizeId(n.NodeId)).Distinct());

            int nodesWithBox = 0, hiddenAll = 0, clippedAll = 0;
            var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in _nodes)
            {
                string key = CableNodeData.NormalizeId(node.NodeId);
                if (!search.TryGetValue(key, out var items) || items.Count == 0)
                    continue; // no box in model → not on screen

                nodesWithBox++;
                bool anyVisible = false, anyNotHidden = false;
                foreach (var item in items)
                {
                    if (SectionService.IsEffectivelyHidden(item)) continue;
                    anyNotHidden = true;
                    BoundingBox3D bbox;
                    try { bbox = item.BoundingBox(); }
                    catch { continue; }
                    if (bbox == null) continue;
                    if (_main.SectionSvc.IsPointVisible(bbox.Center, planes))
                    {
                        anyVisible = true;
                        break;
                    }
                }

                if (anyVisible) visible.Add(node.NodeId);
                else if (!anyNotHidden) hiddenAll++;   // every box hidden
                else clippedAll++;                      // not hidden but outside section
            }

            _visDiag = $"보이는것만 진단: 박스노드 {nodesWithBox}, 활성평면 {planes.Count}, "
                     + $"숨김제외 {hiddenAll}, 단면제외 {clippedAll}, 표시 {visible.Count}";
            return visible;
        }

        private void RefreshFocusIfActive()
        {
            if (!_focusOn) return;
            var doc = _main.GetDocument();
            if (doc == null) return;
            var hits = GetCurrentFilterHitNodeIds();
            _main.OverrideEngine.SetCableFilterFocus(doc, hits);
        }

        private List<string> GetCurrentFilterHitNodeIds()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrEmpty(keyword))
                return _matchedNodeIds.ToList();
            return _nodes
                .Where(n => _matchedNodeIds.Contains(n.NodeId) && NodeMatchesKeyword(n, keyword))
                .Select(n => n.NodeId)
                .ToList();
        }

        private static bool NodeMatchesKeyword(CableNodeData n, string keywordUpper)
        {
            if ((n.NodeId ?? "").ToUpperInvariant().Contains(keywordUpper)) return true;
            foreach (var c in n.Cables)
            {
                if ((c.CableNo ?? "").ToUpperInvariant().Contains(keywordUpper)) return true;
                if ((c.EquipNo ?? "").ToUpperInvariant().Contains(keywordUpper)) return true;
            }
            return false;
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"CablePull_{DateTime.Now:yyyyMMdd_HHmm}";
            try
            {
                _main.ExportSvc.SaveViewpoint(doc, name);
                MessageBox.Show($"Viewpoint '{name}' 저장 완료");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Viewpoint 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNwd_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.ExportSvc.ExportNwdWithDialog(doc);
        }

        private void NodeList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_nodeList.SelectedItems.Count == 0) { _cableList.Items.Clear(); return; }

            var node = _nodeList.SelectedItems[0].Tag as CableNodeData;
            PopulateCableList(node);

            if (_suppressSelectionSync) return;

            var doc = _main.GetDocument();
            if (doc == null) return;
            var col = _main.OverrideEngine.GetCableNodeItems(node?.NodeId);
            if (col == null || col.Count == 0) return;
            _suppressSelectionSync = true;
            try
            {
                doc.CurrentSelection.CopyFrom(col);
                doc.ActiveView.FocusOnCurrentSelection();
            }
            finally { _suppressSelectionSync = false; }
        }

        private void NodeList_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            _nodeList.ListViewItemSorter = new ListViewItemComparer(_sortColumn, _sortAscending);
            _nodeList.Sort();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _col; private readonly int _dir;
            public ListViewItemComparer(int c, bool asc) { _col = c; _dir = asc ? 1 : -1; }
            public int Compare(object x, object y) =>
                string.Compare(((ListViewItem)x).SubItems[_col].Text, ((ListViewItem)y).SubItems[_col].Text, StringComparison.OrdinalIgnoreCase) * _dir;
        }

        private void PopulateCableList(CableNodeData node)
        {
            _cableList.Items.Clear();
            if (node == null) return;
            foreach (var c in node.Cables)
            {
                string design = c.DesignLth.HasValue ? c.DesignLth.Value.ToString("0.##") : "-";
                string pulled = c.PulledLth.HasValue ? c.PulledLth.Value.ToString("0.##") : "-";
                string pct = c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "-";
                string from = string.IsNullOrEmpty(c.FromModule) ? c.FromEquip : $"{c.FromModule}/{c.FromEquip}";
                string to = string.IsNullOrEmpty(c.ToModule) ? c.ToEquip : $"{c.ToModule}/{c.ToEquip}";
                var item = new ListViewItem(c.CableNo ?? "");
                item.SubItems.Add(c.EquipNo ?? "");
                item.SubItems.Add(c.RouteSys ?? "");
                item.SubItems.Add(design);
                item.SubItems.Add(pulled);
                item.SubItems.Add(pct);
                item.SubItems.Add(from);
                item.SubItems.Add(to);
                _cableList.Items.Add(item);
            }
        }

        /// <summary>
        /// Refresh the 전체/매칭/미매칭 tab labels. With "보이는 것만" on, counts become
        /// "화면표시/전체" so the user sees the numbers shrink as the section clips nodes.
        /// </summary>
        private void UpdateTabCounts(HashSet<string> visibleNodeIds)
        {
            bool applied = _matchedNodeIds.Count > 0 || _unmatchedNodeIds.Count > 0;
            if (!applied)
            {
                _tabFilter.TabPages[0].Text = $"전체 ({_nodes.Count})";
                _tabFilter.TabPages[1].Text = "매칭";
                _tabFilter.TabPages[2].Text = "미매칭";
                return;
            }

            if (visibleNodeIds == null)
            {
                _tabFilter.TabPages[0].Text = $"전체 ({_nodes.Count})";
                _tabFilter.TabPages[1].Text = $"매칭 ({_matchedNodeIds.Count})";
                _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedNodeIds.Count})";
                return;
            }

            int vTotal = _nodes.Count(n => visibleNodeIds.Contains(n.NodeId));
            int vMatched = _nodes.Count(n => visibleNodeIds.Contains(n.NodeId) && _matchedNodeIds.Contains(n.NodeId));
            int vUnmatched = _nodes.Count(n => visibleNodeIds.Contains(n.NodeId) && _unmatchedNodeIds.Contains(n.NodeId));
            _tabFilter.TabPages[0].Text = $"전체 ({vTotal}/{_nodes.Count})";
            _tabFilter.TabPages[1].Text = $"매칭 ({vMatched}/{_matchedNodeIds.Count})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({vUnmatched}/{_unmatchedNodeIds.Count})";
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            int tabIndex = _tabFilter.SelectedIndex;

            var visibleNodeIds = ComputeVisibleNodeIds();
            UpdateTabCounts(visibleNodeIds);

            var filtered = _nodes.AsEnumerable();

            if (tabIndex == 1 && _matchedNodeIds.Count > 0)
                filtered = filtered.Where(n => _matchedNodeIds.Contains(n.NodeId));
            else if (tabIndex == 2 && _unmatchedNodeIds.Count > 0)
                filtered = filtered.Where(n => _unmatchedNodeIds.Contains(n.NodeId));

            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(n => NodeMatchesKeyword(n, keyword));

            if (visibleNodeIds != null)
                filtered = filtered.Where(n => visibleNodeIds.Contains(n.NodeId));

            _nodeList.BeginUpdate();
            _nodeList.Items.Clear();

            foreach (var node in filtered)
            {
                var stage = node.GetStage();
                string stageLabel = CableStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedNodeIds.Count > 0 || _unmatchedNodeIds.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedNodeIds.Contains(node.NodeId) ? "O" : "X");
                string pct = node.OverallProgress.HasValue ? $"{node.OverallProgress.Value * 100:0}%" : "-";

                var item = new ListViewItem(node.NodeId);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(node.Cables.Count.ToString());
                item.SubItems.Add(node.TotalDesignLth.ToString("0.##"));
                item.SubItems.Add(node.TotalPulledLth.ToString("0.##"));
                item.SubItems.Add(pct);
                var stageSub = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSub.ForeColor = setting.DisplayColor;
                var matchSub = item.SubItems.Add(matchLabel);
                if (matchLabel == "X") matchSub.ForeColor = Color.Red;
                item.Tag = node;
                _nodeList.Items.Add(item);
            }
            _nodeList.EndUpdate();
            _cableList.Items.Clear();

            PopulateRouteList(keyword, tabIndex, visibleNodeIds);
            RefreshStatsLabel();
        }

        /// <summary>
        /// Build Cable No → ordered Node list from raw rows. Each Excel row is one
        /// cable instance at one node, so a cable that passes through 3 nodes
        /// appears 3 times — we collapse it into one route here.
        /// </summary>
        private void BuildCableRoutes()
        {
            _cableRoutes.Clear();
            _cableMeta.Clear();
            foreach (var node in _nodes)
            {
                foreach (var c in node.Cables)
                {
                    string cableNo = (c.CableNo ?? "").Trim();
                    if (string.IsNullOrEmpty(cableNo)) continue;

                    if (!_cableRoutes.TryGetValue(cableNo, out var list))
                    {
                        list = new List<string>();
                        _cableRoutes[cableNo] = list;
                        _cableMeta[cableNo] = c; // first encounter = representative metadata
                    }
                    if (!list.Contains(node.NodeId, StringComparer.OrdinalIgnoreCase))
                        list.Add(node.NodeId);
                }
            }
        }

        private void PopulateRouteList(string keywordUpper, int tabIndex, HashSet<string> visibleNodeIds)
        {
            _routeList.BeginUpdate();
            _routeList.Items.Clear();

            foreach (var kv in _cableRoutes)
            {
                string cableNo = kv.Key;
                var nodes = kv.Value;

                // Filter tab: 매칭 = at least one node matched; 미매칭 = none matched.
                if (tabIndex == 1 && _matchedNodeIds.Count > 0 &&
                    !nodes.Any(n => _matchedNodeIds.Contains(n))) continue;
                if (tabIndex == 2 && _unmatchedNodeIds.Count > 0 &&
                    nodes.Any(n => _matchedNodeIds.Contains(n))) continue;

                // 보이는 것만: keep a cable if any node on its route is currently visible.
                if (visibleNodeIds != null && !nodes.Any(n => visibleNodeIds.Contains(n)))
                    continue;

                // Keyword filter: Cable No / Equip No / any Node ID
                if (!string.IsNullOrEmpty(keywordUpper))
                {
                    var meta = _cableMeta.TryGetValue(cableNo, out var m) ? m : null;
                    bool hit =
                        cableNo.ToUpperInvariant().Contains(keywordUpper)
                        || (meta != null && (meta.EquipNo ?? "").ToUpperInvariant().Contains(keywordUpper))
                        || nodes.Any(n => n.ToUpperInvariant().Contains(keywordUpper));
                    if (!hit) continue;
                }

                var meta2 = _cableMeta.TryGetValue(cableNo, out var mm) ? mm : null;
                string equip = meta2?.EquipNo ?? "";
                string design = meta2?.DesignLth.HasValue == true ? meta2.DesignLth.Value.ToString("0.##") : "-";
                string pulled = meta2?.PulledLth.HasValue == true ? meta2.PulledLth.Value.ToString("0.##") : "-";
                string pct    = meta2?.PullingProgress.HasValue == true ? $"{meta2.PullingProgress.Value * 100:0}%" : "-";
                string route  = string.Join(", ", nodes);

                var item = new ListViewItem(cableNo);
                item.SubItems.Add(equip);
                item.SubItems.Add(nodes.Count.ToString());
                item.SubItems.Add(design);
                item.SubItems.Add(pulled);
                item.SubItems.Add(pct);
                item.SubItems.Add(route);
                item.Tag = nodes;
                _routeList.Items.Add(item);
            }
            _routeList.EndUpdate();
        }

        private void RouteList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_routeList.SelectedItems.Count == 0) return;
            if (_suppressSelectionSync) return;

            var nodes = _routeList.SelectedItems[0].Tag as List<string>;
            if (nodes == null) return;

            var doc = _main.GetDocument();
            if (doc == null) return;

            // Aggregate every box on the cable's route. Boxes for unmatched nodes are
            // skipped silently — they simply don't contribute to the selection.
            var combined = new ModelItemCollection();
            foreach (var nodeId in nodes)
            {
                var col = _main.OverrideEngine.GetCableNodeItems(nodeId);
                if (col == null) continue;
                foreach (ModelItem mi in col) combined.Add(mi);
            }
            if (combined.Count == 0) return;

            _suppressSelectionSync = true;
            try
            {
                doc.CurrentSelection.CopyFrom(combined);
                doc.ActiveView.FocusOnCurrentSelection();
            }
            finally { _suppressSelectionSync = false; }
        }

        private void UpdateStats(OverrideResult result = null, int hiddenCount = 0)
        {
            var counts = _nodes
                .GroupBy(n => n.GetStage())
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { CableStage.NotStarted }.Concat(CableStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{CableStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (result != null)
                line2 = $"매칭 {result.MatchedCount} / 미매칭 {result.UnmatchedCount} / 숨김 박스 {hiddenCount}";
            _statsBase = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            RefreshStatsLabel();
        }

        /// <summary>Compose the stats label = base apply stats + (보이는것만 진단 when active).</summary>
        private void RefreshStatsLabel()
        {
            _lblStats.Text = _statsBase
                + (_visibleOnly && !string.IsNullOrEmpty(_visDiag) ? $"\n{_visDiag}" : "");
        }

        // ----- Bidirectional selection sync -----

        private void SubscribeSelection(Document doc)
        {
            if (_subscribedDoc == doc) return;
            UnsubscribeSelection();
            _subscribedDoc = doc;
            doc.CurrentSelection.Changed += CurrentSelection_Changed;
        }

        private void UnsubscribeSelection()
        {
            if (_subscribedDoc != null)
            {
                try { _subscribedDoc.CurrentSelection.Changed -= CurrentSelection_Changed; } catch { }
                _subscribedDoc = null;
            }
        }

        private void CurrentSelection_Changed(object sender, EventArgs e)
        {
            if (_suppressSelectionSync) return;
            var doc = _main.GetDocument();
            if (doc == null) return;

            string nodeId = ResolveSelectedCableNodeId(doc);
            if (nodeId == null) return;

            // Find the row in _nodeList; select it.
            _suppressSelectionSync = true;
            try
            {
                foreach (ListViewItem item in _nodeList.Items)
                {
                    var n = item.Tag as CableNodeData;
                    if (n != null && string.Equals(n.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        _nodeList.SelectedItems.Clear();
                        item.Selected = true;
                        item.EnsureVisible();
                        PopulateCableList(n);
                        break;
                    }
                }
            }
            finally { _suppressSelectionSync = false; }
        }

        /// <summary>Walk the current selection (and its ancestors) for the first DisplayName containing "-BOX",
        /// then return the prefix before "-BOX" if it matches a known matched Node.</summary>
        private string ResolveSelectedCableNodeId(Document doc)
        {
            foreach (ModelItem selected in doc.CurrentSelection.SelectedItems)
            {
                var item = selected;
                while (item != null)
                {
                    string name = item.DisplayName?.Trim() ?? "";
                    int idx = name.IndexOf("-BOX", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        string key = name.Substring(0, idx).TrimStart('/').Trim();
                        // Match against the canonical NodeId stored in _matchedNodeIds (case-insensitive)
                        foreach (var id in _matchedNodeIds)
                        {
                            if (string.Equals(CableNodeData.NormalizeId(id), key, StringComparison.OrdinalIgnoreCase))
                                return id;
                        }
                        return null;
                    }
                    item = item.Parent;
                }
            }
            return null;
        }

        private Dictionary<CableStage, ColorSetting> CloneDefaults(Dictionary<CableStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<CableStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
