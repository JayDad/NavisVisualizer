using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class SpoolTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<SpoolData> _spools = new List<SpoolData>();
        private readonly Dictionary<TabDataSource, List<SpoolData>> _spoolsBySource
            = new Dictionary<TabDataSource, List<SpoolData>>();
        private bool _appliedOnce;
        private Dictionary<SpoolStage, ColorSetting> _colorSettings;

        // Match tracking
        private HashSet<string> _matchedSpoolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedSpoolIds = new List<string>();

        // Aggregation scope (매칭 집계 범위) — null = 전체 모델 (no filtering)
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;

        private DataSourcePanel _srcPanel;
        private DateTimePicker _dtpReference;
        private TextBox  _txtSearch;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button   _btnApply;
        private Button   _btnReset;
        private Button   _btnWriteProps;
        private Button   _btnViewpoint;
        private Button   _btnNwd;
        private Label    _lblStats;
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<SpoolStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<SpoolStage, (Panel, Button, ComboBox, CheckBox)>();

        public SpoolTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.SpoolDefaults);
            _scopeFilter = new ScopeFilter(main.SectionSvc);
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

            _srcPanel = new DataSourcePanel();
            _srcPanel.ExcelLoadClicked    += (s, e) => LoadExcel();
            _srcPanel.TemplateClicked     += (s, e) => ExportInputTemplate();
            _srcPanel.OasisLoadClicked    += (s, e) => LoadOasis();
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData(reapply: _appliedOnce);
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();

            // Reference date picker
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
                if (_spools.Count > 0)
                {
                    FilterList();
                    UpdateStats();
                }
            };
            datePanel.Controls.Add(dateLabel);
            datePanel.Controls.Add(_dtpReference);

            var colorPanels = BuildColorPanel();

            // Search box
            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 210, Text = "" };
            _txtSearch.TextChanged += (s, e) => FilterList();
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            // Stats label
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 36 };

            // Aggregation scope group (radios select only; [적용] runs the judgement)
            _scopePanel = new ScopePanel { Dock = DockStyle.Fill };
            _scopePanel.ApplyRequested += (s, e) => ApplyScope();

            // Filter tabs: 전체 / 매칭 / 미매칭
            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 230 };
            var tabAll       = new TabPage("전체");
            var tabMatched   = new TabPage("매칭");
            var tabUnmatched = new TabPage("미매칭");
            _tabFilter.TabPages.Add(tabAll);
            _tabFilter.TabPages.Add(tabMatched);
            _tabFilter.TabPages.Add(tabUnmatched);
            _tabFilter.SelectedIndexChanged += (s, e) => FilterList();

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
            };
            _listView.Columns.Add("Spool ID", 140);
            _listView.Columns.Add("ISO No", 120);
            _listView.Columns.Add("단계", 80);
            _listView.Columns.Add("매칭", 45);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;

            // ListView goes into the first tab, but we'll manage it by moving it
            tabAll.Controls.Add(_listView);
            _tabFilter.SelectedIndexChanged += (s, e) =>
            {
                // Move ListView to the selected tab
                var selectedTab = _tabFilter.SelectedTab;
                if (!selectedTab.Controls.Contains(_listView))
                {
                    selectedTab.Controls.Add(_listView);
                }
            };

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply      = new Button { Text = "적용",              Width = 80  };
            _btnReset      = new Button { Text = "전체 초기화",       Width = 90  };
            _btnWriteProps = new Button { Text = "속성 쓰기",         Width = 80  };
            _btnViewpoint  = new Button { Text = "Viewpoint 저장",    Width = 120 };
            _btnNwd        = new Button { Text = "NWD Export",        Width = 110 };
            _btnApply.Click      += BtnApply_Click;
            _btnReset.Click      += BtnReset_Click;
            _btnWriteProps.Click += BtnWriteProps_Click;
            _btnViewpoint.Click  += BtnViewpoint_Click;
            _btnNwd.Click        += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnWriteProps, _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "Fabrication", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanels.fabPanel);
            layout.Controls.Add(new Label { Text = "Install", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanels.instPanel);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);
            layout.Controls.Add(searchPanel);
            layout.Controls.Add(_scopePanel);
            layout.Controls.Add(_tabFilter);

            Controls.Add(layout);
        }

        private (Panel fabPanel, Panel instPanel) BuildColorPanel()
        {
            var fabStages = new[] { SpoolStage.NotStarted }.Concat(SpoolStageInfo.FabricationStages).ToArray();
            var fabPanel = BuildStageColorPanel(fabStages);
            var instPanel = BuildStageColorPanel(SpoolStageInfo.InstallStages);
            return (fabPanel, instPanel);
        }

        private Panel BuildStageColorPanel(SpoolStage[] stages)
        {
            var panel  = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            // 2 columns: [check][color][btn][trans] | [check][color][btn][trans]
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < stages.Length; i++)
            {
                var stage   = stages[i];
                var setting = _colorSettings[stage];
                string label = SpoolStageInfo.Labels[stage];

                var chk             = new CheckBox { Text = label, Checked = true, AutoSize = true };
                var colorBox        = new Panel    { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn        = new Button   { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var capturedStage = stage;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[capturedStage].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[capturedStage].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(capturedStage.ToString());
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        _colorSettings[capturedStage].Transparency = pct / 100.0;
                        IncrementalUpdate(capturedStage.ToString());
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

        private void LoadExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Spool Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var list = ExcelLoader.LoadSpool(dlg.FileName);
                    _spoolsBySource[TabDataSource.Excel] = list;
                    _srcPanel.SetLoaded(TabDataSource.Excel, list.Count, Path.GetFileName(dlg.FileName));
                    if (_srcPanel.ActiveSource == TabDataSource.Excel)
                        ApplyActiveSourceData(reapply: false);
                }
                catch (Exception ex)
                {
                    _srcPanel.SetFailed(TabDataSource.Excel, "로드 실패");
                    MessageBox.Show($"Excel 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportInputTemplate()
        {
            try
            {
                string path = InputTemplate.ExportSpool();
                MessageBox.Show($"입력 양식 저장 완료: {path}\n작성 후 Excel 형식(.xlsx)으로 저장해 Import 하세요.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"입력 양식 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadSpool(settings);
                _spoolsBySource[TabDataSource.Oasis] = list;
                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _srcPanel.SetLoaded(TabDataSource.Oasis, list.Count,
                    $"{settings.Database}/{prj} · {DateTime.Now:HH:mm}");
                if (_srcPanel.ActiveSource == TabDataSource.Oasis)
                    ApplyActiveSourceData(reapply: false);
            }
            catch (Exception ex)
            {
                _srcPanel.SetFailed(TabDataSource.Oasis, "로드 실패");
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 적용 기준 소스의 리스트로 화면을 전환한다. 매칭 결과는 소스별로 다르므로
        /// 초기화하고, 이미 적용 이력이 있으면(reapply) 새 소스 기준으로 색상을 재적용해
        /// 리스트와 3D 뷰가 어긋나지 않게 한다.
        /// </summary>
        private void ApplyActiveSourceData(bool reapply)
        {
            _spools = _spoolsBySource.TryGetValue(_srcPanel.ActiveSource, out var list)
                ? list : new List<SpoolData>();
            _matchedSpoolIds.Clear();
            _unmatchedSpoolIds.Clear();

            // 소스 전환 → 매칭 집합이 바뀌므로 범위 판정도 무효화. 재적용 경로가
            // 현재 범위를 새 매칭 기준으로 다시 계산하고, 재적용이 없으면(매칭 없음)
            // 범위 표시가 거짓말하지 않도록 전체 모델로 복귀시킨다.
            _scopeFilter.Invalidate();
            _scopeKeys = null;
            bool willReapply = reapply && _spools.Count > 0 && _main.GetDocument() != null;
            if (!willReapply)
                _scopePanel.ResetToFullModel();

            _tabFilter.TabPages[0].Text = $"전체 ({_spools.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            FilterList();
            UpdateStats();

            if (willReapply)
                BtnApply_Click(null, EventArgs.Empty);
        }

        private void ExportComparison()
        {
            if (!_spoolsBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_spoolsBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            var fields = new List<SourceComparer.Field<SpoolData>>
            {
                new SourceComparer.Field<SpoolData>("ISO No", s => s.IsoNo ?? ""),
            };
            foreach (var stage in SpoolStageInfo.OrderedStages)
            {
                var captured = stage;
                fields.Add(new SourceComparer.Field<SpoolData>(SpoolStageInfo.Labels[captured], s =>
                {
                    s.StageDates.TryGetValue(captured, out var d);
                    return SourceComparer.FormatDate(d);
                }));
            }
            fields.Add(new SourceComparer.Field<SpoolData>($"현재 단계({referenceDate:yyyy-MM-dd})",
                s => SpoolStageInfo.Labels[s.GetStageAtDate(referenceDate)]));

            var lines = SourceComparer.BuildCsv("Spool No", excelList, oasisList, s => s.SpoolId, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Spool_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            MessageBox.Show($"비교 결과 저장 완료: {path}");
        }

        private void IncrementalUpdate(string stageKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.Spool)) return;

            // Parse the stage key back to find the setting
            if (Enum.TryParse<SpoolStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.Spool, stageKey, setting);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_spools.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            var referenceDate = _dtpReference.Value;
            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add("Spool ID,ISO No,Stage,Matched");
            foreach (var sp in _spools)
            {
                if (!InScope(sp.SpoolId)) continue;
                var stage = sp.GetStageAtDate(referenceDate);
                string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedSpoolIds.Count == 0 || _matchedSpoolIds.Contains(sp.SpoolId);
                lines.Add($"\"{sp.SpoolId}\",\"{sp.IsoNo}\",\"{stageLabel}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Spool_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
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
            if (doc == null || _spools.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.TagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<SpoolStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            var result = _main.OverrideEngine.ApplySpool(doc, _spools, activeSettings, referenceDate);
            // Update match tracking
            _unmatchedSpoolIds = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedSpoolIds = new HashSet<string>(
                _spools.Select(s => s.SpoolId).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            // Matched set changed → scope verdicts are stale
            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            _appliedOnce = true;
            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
        }

        // ----- 매칭 집계 범위 (aggregation scope) -----

        /// <summary>
        /// A row passes the scope when no scope is active, when it is unmatched
        /// (no model position — spatially unjudgeable, always shown), or when its
        /// matched node was judged inside the scope.
        /// </summary>
        private bool InScope(string id) =>
            _scopeKeys == null || !_matchedSpoolIds.Contains(id) || _scopeKeys.Contains(id);

        /// <summary>Scope [적용]: run the judgement for the selected radio, then re-aggregate.</summary>
        private void ApplyScope()
        {
            var scope = _scopePanel.SelectedScope;
            if (scope == MatchScope.FullModel)
            {
                _scopeKeys = null;
                _scopeFilter.SetFullModel();
            }
            else
            {
                var doc = _main.GetDocument();
                if (doc == null || _matchedSpoolIds.Count == 0)
                {
                    MessageBox.Show("먼저 적용(가시화)을 실행하세요. 집계 범위는 매칭된 항목에 적용됩니다.");
                    return;
                }
                if (_main.TagSearcher.NeedsRebuild(doc))
                {
                    MessageBox.Show("모델이 변경되었습니다. 적용(가시화)을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    var itemsByKey = _main.TagSearcher.FindBySpoolIds(_matchedSpoolIds);
                    _scopeKeys = _scopeFilter.Apply(doc, scope, itemsByKey);
                }
                finally
                {
                    _progressBar.Visible = false;
                    _progressBar.Style = ProgressBarStyle.Blocks;
                }
            }

            _scopePanel.SetCurrentScope(scope);
            UpdateTabCounts();
            FilterList();
            UpdateStats();
        }

        /// <summary>After 가시화 적용 the matched set is fresh — silently recompute the applied scope.</summary>
        private void ReapplyCurrentScope(Autodesk.Navisworks.Api.Document doc)
        {
            var scope = _scopePanel.CurrentScope;
            if (scope == MatchScope.FullModel) { _scopeKeys = null; return; }
            var itemsByKey = _main.TagSearcher.FindBySpoolIds(_matchedSpoolIds);
            _scopeKeys = _scopeFilter.Apply(doc, scope, itemsByKey);
        }

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedSpoolIds.Count > 0 || _unmatchedSpoolIds.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null
                ? _matchedSpoolIds.Count
                : _matchedSpoolIds.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _spools.Count : _spools.Count(s => InScope(s.SpoolId));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedSpoolIds.Count})";
        }

        private void BtnWriteProps_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _spools.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.TagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var referenceDate = _dtpReference.Value;

            try
            {
                // Clear selection to reduce properties panel flashing
                doc.CurrentSelection.Clear();

                _btnWriteProps.Enabled = false;
                _btnWriteProps.Text = "쓰는 중...";
                Application.DoEvents();

                var allSpoolIds = _spools.Select(s => s.SpoolId).Distinct();
                var searchResult = _main.TagSearcher.FindBySpoolIds(allSpoolIds);
                int written = _main.UserDataSvc.WriteSpoolProperties(_spools, searchResult, referenceDate);

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
            string name = $"Spool_{DateTime.Now:yyyyMMdd_HHmm}";
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
                var spool = selected.Tag as SpoolData;
                if (spool == null) continue;
                var found = _main.TagSearcher.FindBySpoolIds(new[] { spool.SpoolId });
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }

            if (collection.Count == 0) return;
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }
            _listView.ListViewItemSorter = new ListViewItemComparer(_sortColumn, _sortAscending);
            _listView.Sort();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _col;
            private readonly int _dir;
            public ListViewItemComparer(int column, bool ascending) { _col = column; _dir = ascending ? 1 : -1; }
            public int Compare(object x, object y)
            {
                string a = ((ListViewItem)x).SubItems[_col].Text;
                string b = ((ListViewItem)y).SubItems[_col].Text;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) * _dir;
            }
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            var referenceDate = _dtpReference.Value;
            int tabIndex = _tabFilter.SelectedIndex; // 0=전체, 1=매칭, 2=미매칭

            var filtered = _spools.AsEnumerable();

            // Tab filter
            if (tabIndex == 1 && _matchedSpoolIds.Count > 0)
                filtered = filtered.Where(s => _matchedSpoolIds.Contains(s.SpoolId));
            else if (tabIndex == 2 && _unmatchedSpoolIds.Count > 0)
                filtered = filtered.Where(s => _unmatchedSpoolIds.Contains(s.SpoolId));

            // Aggregation scope (matched rows only; unmatched rows are unjudgeable)
            if (_scopeKeys != null)
                filtered = filtered.Where(s => InScope(s.SpoolId));

            // Text search filter
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(s => s.SpoolId.ToUpperInvariant().Contains(keyword)
                    || (s.IsoNo ?? "").ToUpperInvariant().Contains(keyword));

            _listView.Items.Clear();

            foreach (var spool in filtered)
            {
                var stage = spool.GetStageAtDate(referenceDate);
                string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedSpoolIds.Count > 0 || _unmatchedSpoolIds.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedSpoolIds.Contains(spool.SpoolId) ? "O" : "X");

                var item = new ListViewItem(spool.SpoolId);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(spool.IsoNo ?? "");
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(matchLabel);
                if (matchLabel == "X")
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = spool;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            var counts = _spools
                .Where(s => InScope(s.SpoolId))
                .GroupBy(s => s.GetStageAtDate(referenceDate))
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { SpoolStage.NotStarted }.Concat(SpoolStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{SpoolStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            bool hasApplied = _matchedSpoolIds.Count > 0 || _unmatchedSpoolIds.Count > 0;
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null
                    ? _matchedSpoolIds.Count
                    : _matchedSpoolIds.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope} / 미매칭 {_unmatchedSpoolIds.Count}";
                if (_scopeKeys != null)
                    line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준, 미매칭은 전체)";
            }
            _lblStats.Text = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
        }

        private Dictionary<SpoolStage, ColorSetting> CloneDefaults(Dictionary<SpoolStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<SpoolStage, ColorSetting>();
            foreach (var kv in defaults)
                clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
