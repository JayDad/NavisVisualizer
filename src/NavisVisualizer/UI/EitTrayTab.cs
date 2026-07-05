using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class EitTrayTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<EitTrayData> _trays = new List<EitTrayData>();
        private Dictionary<EitStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedTrayNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedTrayNos = new List<string>();

        private Button _btnLoad;
        private Label _lblFile;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<EitStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<EitStage, (Panel, Button, ComboBox, CheckBox)>();

        public EitTrayTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.EitDefaults);
            InitializeComponent();
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

            _btnLoad = new Button { Text = "EIT Tray Excel", Dock = DockStyle.Fill, Height = 30 };
            _btnLoad.Click += BtnLoad_Click;
            _lblFile = new Label { Text = "(파일 없음)", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Height = 18 };

            var datePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            var dateLabel = new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 110,
                Enabled = false, // 향후 Install date 컬럼 추가에 대비해 UI만 유지
            };
            datePanel.Controls.Add(dateLabel);
            datePanel.Controls.Add(_dtpReference);
            datePanel.Controls.Add(new Label { Text = "(향후 사용)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(4, 4, 0, 0) });

            var colorPanel = BuildColorPanel();

            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 160, Text = "" };
            _txtSearch.TextChanged += (s, e) => FilterList();
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 출력", Width = 110, Height = 23 };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 36 };

            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 230 };
            var tabAll = new TabPage("전체");
            var tabMatched = new TabPage("매칭");
            var tabUnmatched = new TabPage("미매칭");
            _tabFilter.TabPages.Add(tabAll);
            _tabFilter.TabPages.Add(tabMatched);
            _tabFilter.TabPages.Add(tabUnmatched);
            _tabFilter.SelectedIndexChanged += (s, e) =>
            {
                var selectedTab = _tabFilter.SelectedTab;
                if (!selectedTab.Controls.Contains(_listView))
                    selectedTab.Controls.Add(_listView);
                FilterList();
            };

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
            };
            _listView.Columns.Add("Tray No.", 200);
            _listView.Columns.Add("Lth", 55);
            _listView.Columns.Add("Installed", 65);
            _listView.Columns.Add("Install %", 65);
            _listView.Columns.Add("단계", 75);
            _listView.Columns.Add("매칭", 40);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            tabAll.Controls.Add(_listView);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply     = new Button { Text = "적용",           Width = 80  };
            _btnReset     = new Button { Text = "전체 초기화",    Width = 90  };
            _btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd       = new Button { Text = "NWD Export",     Width = 110 };
            _btnApply.Click     += BtnApply_Click;
            _btnReset.Click     += BtnReset_Click;
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            layout.Controls.Add(_btnLoad);
            layout.Controls.Add(_lblFile);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);
            layout.Controls.Add(searchPanel);
            layout.Controls.Add(_tabFilter);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var allStages = new[] { EitStage.NotStarted }.Concat(EitStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            // Checkbox column widened to fit "Cable 포설중" (6 chars + checkbox square)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < allStages.Length; i++)
            {
                var stage = allStages[i];
                var setting = _colorSettings[stage];
                string label = EitStageInfo.Labels[stage];

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
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.EitTray)) return;
            if (Enum.TryParse<EitStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.EitTray, stageKey, setting);
        }

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "EIT Tray Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _trays = ExcelLoader.LoadEitTray(dlg.FileName);
                    _lblFile.Text = Path.GetFileName(dlg.FileName);
                    _matchedTrayNos.Clear();
                    _unmatchedTrayNos.Clear();
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
            if (_trays.Count == 0) { MessageBox.Show("Excel을 먼저 로드하세요."); return; }
            var lines = new List<string>();
            lines.Add("Tray No.,Tray Lth,Tray Installed,Install %,Stage,Matched");
            foreach (var t in _trays)
            {
                var stage = t.GetStage();
                string stageLabel = EitStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedTrayNos.Count == 0 || _matchedTrayNos.Contains(t.TrayNumber);
                string lth = t.TrayLth.HasValue ? t.TrayLth.Value.ToString("0.##") : "";
                string installed = t.TrayInstalled.HasValue ? t.TrayInstalled.Value.ToString("0.##") : "";
                string pct = t.InstallProgress.HasValue ? $"{t.InstallProgress.Value * 100:0}%" : "";
                lines.Add($"\"{t.TrayNumber}\",\"{lth}\",\"{installed}\",\"{pct}\",\"{stageLabel}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"EitTray_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
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
            _main.TagSearcher.BuildIndex(doc);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _trays.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.TagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<EitStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var result = _main.OverrideEngine.ApplyEit(doc, _trays, activeSettings);

            _unmatchedTrayNos = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedTrayNos = new HashSet<string>(
                _trays.Select(t => t.TrayNumber).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            _tabFilter.TabPages[0].Text = $"전체 ({_trays.Count})";
            _tabFilter.TabPages[1].Text = $"매칭 ({_matchedTrayNos.Count})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedTrayNos.Count})";

            UpdateStats(result);
            FilterList();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 초기화 완료";
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"EitTray_{DateTime.Now:yyyyMMdd_HHmm}";
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

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;

            var doc = _main.GetDocument();
            if (doc == null || !_main.TagSearcher.IsIndexBuilt || _main.TagSearcher.NeedsRebuild(doc)) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (ListViewItem selected in _listView.SelectedItems)
            {
                var tray = selected.Tag as EitTrayData;
                if (tray == null) continue;
                var found = _main.TagSearcher.FindBySpoolIds(new[] { EitTrayData.NormalizeId(tray.TrayNumber) });
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }

            if (collection.Count == 0) return;
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            _listView.ListViewItemSorter = new ListViewItemComparer(_sortColumn, _sortAscending);
            _listView.Sort();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _col; private readonly int _dir;
            public ListViewItemComparer(int c, bool asc) { _col = c; _dir = asc ? 1 : -1; }
            public int Compare(object x, object y) =>
                string.Compare(((ListViewItem)x).SubItems[_col].Text, ((ListViewItem)y).SubItems[_col].Text, StringComparison.OrdinalIgnoreCase) * _dir;
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _trays.AsEnumerable();

            if (tabIndex == 1 && _matchedTrayNos.Count > 0)
                filtered = filtered.Where(t => _matchedTrayNos.Contains(t.TrayNumber));
            else if (tabIndex == 2 && _unmatchedTrayNos.Count > 0)
                filtered = filtered.Where(t => _unmatchedTrayNos.Contains(t.TrayNumber));

            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(t => (t.TrayNumber ?? "").ToUpperInvariant().Contains(keyword));

            _listView.Items.Clear();

            foreach (var tray in filtered)
            {
                var stage = tray.GetStage();
                string stageLabel = EitStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedTrayNos.Count > 0 || _unmatchedTrayNos.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedTrayNos.Contains(tray.TrayNumber) ? "O" : "X");
                string lth = tray.TrayLth.HasValue ? tray.TrayLth.Value.ToString("0.##") : "-";
                string installed = tray.TrayInstalled.HasValue ? tray.TrayInstalled.Value.ToString("0.##") : "-";
                string pct = tray.InstallProgress.HasValue ? $"{tray.InstallProgress.Value * 100:0}%" : "-";

                var item = new ListViewItem(tray.TrayNumber);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(lth);
                item.SubItems.Add(installed);
                item.SubItems.Add(pct);
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(matchLabel);
                if (matchLabel == "X")
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = tray;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            var counts = _trays
                .GroupBy(t => t.GetStage())
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { EitStage.NotStarted }.Concat(EitStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{EitStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (result != null && result.MatchedCount > 0)
                line2 = $"매칭 {result.MatchedCount} / 미매칭 {result.UnmatchedCount}";
            _lblStats.Text = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
        }

        private Dictionary<EitStage, ColorSetting> CloneDefaults(Dictionary<EitStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<EitStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
