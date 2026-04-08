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
    public class EquipmentTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<EquipmentData> _equipments = new List<EquipmentData>();
        private Dictionary<EquipmentStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedTagNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedTagNos = new List<string>();

        private Button _btnLoad;
        private Label _lblFile;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnReset;
        private Button _btnWriteProps;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<EquipmentStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<EquipmentStage, (Panel, Button, ComboBox, CheckBox)>();

        public EquipmentTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.EquipmentDefaults);
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

            _btnLoad = new Button { Text = "Equipment Excel", Dock = DockStyle.Fill, Height = 30 };
            _btnLoad.Click += BtnLoad_Click;
            _lblFile = new Label { Text = "(파일 없음)", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Height = 18 };

            var datePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            var dateLabel = new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 110,
            };
            _dtpReference.ValueChanged += (s, e) =>
            {
                if (_equipments.Count > 0) { FilterList(); UpdateStats(); }
            };
            datePanel.Controls.Add(dateLabel);
            datePanel.Controls.Add(_dtpReference);

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
            _listView.Columns.Add("Tag No.", 160);
            _listView.Columns.Add("Description", 140);
            _listView.Columns.Add("단계", 75);
            _listView.Columns.Add("매칭", 45);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            tabAll.Controls.Add(_listView);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply      = new Button { Text = "적용",           Width = 80  };
            _btnReset      = new Button { Text = "전체 초기화",    Width = 90  };
            _btnWriteProps = new Button { Text = "속성 쓰기",      Width = 80  };
            _btnViewpoint  = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd        = new Button { Text = "NWD Export",     Width = 110 };
            _btnApply.Click      += BtnApply_Click;
            _btnReset.Click      += BtnReset_Click;
            _btnWriteProps.Click += BtnWriteProps_Click;
            _btnViewpoint.Click  += BtnViewpoint_Click;
            _btnNwd.Click        += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnWriteProps, _btnViewpoint, _btnNwd });

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
            var allStages = new[] { EquipmentStage.NotStarted }.Concat(EquipmentStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < allStages.Length; i++)
            {
                var stage = allStages[i];
                var setting = _colorSettings[stage];
                string label = EquipmentStageInfo.Labels[stage];

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
            if (Enum.TryParse<EquipmentStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, stageKey, setting);
        }

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Equipment Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _equipments = ExcelLoader.LoadEquipment(dlg.FileName);
                    _lblFile.Text = Path.GetFileName(dlg.FileName);
                    _matchedTagNos.Clear();
                    _unmatchedTagNos.Clear();
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
            if (_equipments.Count == 0) { MessageBox.Show("Excel을 먼저 로드하세요."); return; }
            var doc = _main.GetDocument();
            var referenceDate = _dtpReference.Value;

            // Get fresh search results for diagnostics
            Dictionary<string, List<Autodesk.Navisworks.Api.ModelItem>> searchResult = null;
            if (doc != null && _main.Searcher.IsIndexBuilt)
            {
                var allTags = _equipments.Select(eq => eq.TagNo).Distinct();
                searchResult = _main.Searcher.FindByTagPrefix(allTags);
            }

            var lines = new List<string>();
            lines.Add("Tag No.,Description,Sub System,RFQ No.,Delivery,ETA,Stage,Matched,ModelItems,MatchedDisplayName");
            foreach (var eq in _equipments)
            {
                var stage = eq.GetStageAtDate(referenceDate);
                string stageLabel = EquipmentStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedTagNos.Count == 0 || _matchedTagNos.Contains(eq.TagNo);

                int itemCount = 0;
                string matchedName = "";
                if (searchResult != null && searchResult.TryGetValue(eq.TagNo, out var items))
                {
                    itemCount = items.Count;
                    if (items.Count > 0)
                        matchedName = items[0].DisplayName ?? "";
                }

                lines.Add($"\"{eq.TagNo}\",\"{eq.Description}\",\"{eq.SubSystem}\",\"{eq.RfqNo}\"," +
                    $"\"{eq.DeliveryStatus}\",\"{eq.ConfirmedEta?.ToString("yyyy-MM-dd") ?? ""}\"," +
                    $"\"{stageLabel}\",\"{(matched ? "O" : "X")}\",{itemCount},\"{matchedName}\"");
            }

            // Add index stats at bottom
            lines.Add("");
            lines.Add($"\"인덱스 항목 수: {_main.Searcher.IndexedCount}\"");

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Equipment_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
            MessageBox.Show($"저장 완료: {path}\n인덱스: {_main.Searcher.IndexedCount}건");
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            _main.Searcher.BuildIndex(doc);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _equipments.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.Searcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<EquipmentStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            var result = _main.OverrideEngine.ApplyEquipment(doc, _equipments, activeSettings, referenceDate);

            _unmatchedTagNos = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedTagNos = new HashSet<string>(
                _equipments.Select(eq => eq.TagNo).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            _tabFilter.TabPages[0].Text = $"전체 ({_equipments.Count})";
            _tabFilter.TabPages[1].Text = $"매칭 ({_matchedTagNos.Count})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedTagNos.Count})";

            UpdateStats(result);
            FilterList();
        }

        private void BtnWriteProps_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _equipments.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.Searcher.NeedsRebuild(doc))
                BuildIndex();

            try
            {
                doc.CurrentSelection.Clear();
                _btnWriteProps.Enabled = false;
                _btnWriteProps.Text = "쓰는 중...";
                Application.DoEvents();

                var referenceDate = _dtpReference.Value;
                var allTagNos = _equipments.Select(eq => eq.TagNo).Distinct();
                var searchResult = _main.Searcher.FindByTagPrefix(allTagNos);
                int written = _main.UserDataSvc.WriteEquipmentProperties(_equipments, searchResult, referenceDate);

                _lblStats.Text += $"\n속성 {written}건 삽입 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"속성 삽입 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _btnWriteProps.Enabled = true;
                _btnWriteProps.Text = "속성 쓰기";
            }
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
            string name = $"Equipment_{DateTime.Now:yyyyMMdd_HHmm}";
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
            var tag = _listView.SelectedItems[0].Tag as EquipmentData;
            if (tag == null) return;

            var doc = _main.GetDocument();
            if (doc == null || !_main.Searcher.IsIndexBuilt || _main.Searcher.NeedsRebuild(doc)) return;

            var found = _main.Searcher.FindByTagPrefix(new[] { tag.TagNo });
            var items = found.Values.SelectMany(v => v).ToList();
            if (items.Count == 0) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            collection.AddRange(items);
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
            var referenceDate = _dtpReference.Value;
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _equipments.AsEnumerable();

            if (tabIndex == 1 && _matchedTagNos.Count > 0)
                filtered = filtered.Where(eq => _matchedTagNos.Contains(eq.TagNo));
            else if (tabIndex == 2 && _unmatchedTagNos.Count > 0)
                filtered = filtered.Where(eq => _unmatchedTagNos.Contains(eq.TagNo));

            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(eq => eq.TagNo.ToUpperInvariant().Contains(keyword)
                    || (eq.Description ?? "").ToUpperInvariant().Contains(keyword));

            _listView.Items.Clear();

            foreach (var equip in filtered)
            {
                var stage = equip.GetStageAtDate(referenceDate);
                string stageLabel = EquipmentStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool isMatched = _matchedTagNos.Count == 0 || _matchedTagNos.Contains(equip.TagNo);

                var item = new ListViewItem(equip.TagNo);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(equip.Description ?? "");
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(isMatched ? "O" : "X");
                if (!isMatched && _matchedTagNos.Count > 0)
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = equip;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            var counts = _equipments
                .GroupBy(eq => eq.GetStageAtDate(referenceDate))
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { EquipmentStage.NotStarted }.Concat(EquipmentStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{EquipmentStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (result != null && result.MatchedCount > 0)
                line2 = $"매칭 {result.MatchedCount} / 미매칭 {result.UnmatchedCount}";
            _lblStats.Text = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
        }

        private Dictionary<EquipmentStage, ColorSetting> CloneDefaults(Dictionary<EquipmentStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<EquipmentStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
