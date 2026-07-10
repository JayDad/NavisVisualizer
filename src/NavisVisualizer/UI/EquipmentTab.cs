using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class EquipmentTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<EquipmentData> _equipments = new List<EquipmentData>();
        private readonly Dictionary<TabDataSource, List<EquipmentData>> _equipmentsBySource
            = new Dictionary<TabDataSource, List<EquipmentData>>();
        private bool _appliedOnce;
        // 레벨 타겟 인덱스는 태그 셋 기반이라 소스 전환 시 재빌드 필요 (NeedsRebuild는 모델 변경만 감지)
        private bool _needsIndexRebuild;
        private Dictionary<EquipmentStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedTagNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedTagNos = new List<string>();

        // Aggregation scope (현황 집계 범위) — null = 전체 모델 (no filtering)
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;

        private DataSourcePanel _srcPanel;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnResetModule;   // 이 공종(Equipment) 색만 제거
        private Button _btnReset;
        private Button _btnHideOthers;    // 체크된 단계 장비만 남기고 나머지 3D 숨김 (토글)
        private Button _btnWriteProps;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Autodesk.Navisworks.Api.ModelItemCollection _tagHiddenByStage; // 숨긴 것 복원용
        private Label _lblStats;
        private Label _lblUnmatched;   // fixed 미매칭(모델 없음) count, pinned to the corner
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<EquipmentStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<EquipmentStage, (Panel, Button, ComboBox, CheckBox)>();

        public EquipmentTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.EquipmentDefaults);
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
            // 라디오 전환은 자동 재적용 안 함(§6) — 대형 모델 수 초 블로킹 방지. 색칠은 [적용] 시에만.
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData(reapply: false);
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();

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
            _txtSearch = new TextBox { Width = 210, Text = "" };
            _txtSearch.TextChanged += (s, e) => FilterList();
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            // 선택 행(없으면 표시 중인 전체 행)을 클립보드로 복사 — Ctrl+C 대체 버튼.
            var btnCopy = new Button { Text = "클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnCopy.Click += (s, e) => CopyListToClipboard();
            searchPanel.Controls.Add(btnCopy);

            // Stats row: scoped stats left, fixed 미매칭(모델 없음) count pinned right.
            var statsRow = new Panel { Dock = DockStyle.Fill, Height = 36 };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false };
            _lblUnmatched = new Label
            {
                Dock = DockStyle.Right,
                Width = 150,
                AutoSize = false,
                TextAlign = ContentAlignment.TopRight,
                ForeColor = Color.Gray,
                Text = "",
            };
            statsRow.Controls.Add(_lblStats);      // Fill added first (lowest z-order)
            statsRow.Controls.Add(_lblUnmatched);  // Right pinned after

            // Aggregation scope group (radios select only; [적용] runs the judgement)
            _scopePanel = new ScopePanel { Dock = DockStyle.Fill };
            _scopePanel.ApplyRequested += (s, e) => ApplyScope();

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
            // ListView는 기본적으로 Ctrl+C를 지원하지 않으므로 공용 헬퍼로 배선.
            ListViewClipboard.EnableCtrlC(_listView, ShowCopied);
            tabAll.Controls.Add(_listView);

            // 1행(가시화): 적용 · 체크 단계 외 숨김
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply      = new Button { Text = "적용",           Width = 80  };
            _btnHideOthers = new Button { Text = "체크 단계 외 숨김", Width = 140 };
            _btnApply.Click      += BtnApply_Click;
            _btnHideOthers.Click += BtnHideOthers_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnHideOthers });

            // 2행(초기화): 공종 초기화(이 공종 색만) · 전체 초기화(모든 공종)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "공종 초기화", Width = 100 };
            _btnReset       = new Button { Text = "전체 초기화", Width = 100 };
            _btnResetModule.Click += BtnResetModule_Click;
            _btnReset.Click       += BtnReset_Click;
            btnPanelReset.Controls.AddRange(new Control[] { _btnResetModule, _btnReset });

            // 3행(덜 쓰임): 속성 쓰기 · Viewpoint 저장 · NWD Export
            var btnPanel2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnWriteProps = new Button { Text = "속성 쓰기",      Width = 80  };
            _btnViewpoint  = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd        = new Button { Text = "NWD Export",     Width = 110 };
            _btnWriteProps.Click += BtnWriteProps_Click;
            _btnViewpoint.Click  += BtnViewpoint_Click;
            _btnNwd.Click        += BtnNwd_Click;
            btnPanel2.Controls.AddRange(new Control[] { _btnWriteProps, _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(btnPanelReset);
            layout.Controls.Add(btnPanel2);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(statsRow);
            layout.Controls.Add(searchPanel);
            layout.Controls.Add(_scopePanel);
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
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.Equipment)) return;
            if (Enum.TryParse<EquipmentStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.Equipment, stageKey, setting);
        }

        private void LoadExcel()
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
                    var list = ExcelLoader.LoadEquipment(dlg.FileName);
                    _equipmentsBySource[TabDataSource.Excel] = list;
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
                string path = InputTemplate.ExportEquipment();
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
                var list = SqlLoader.LoadEquipment(settings);
                _equipmentsBySource[TabDataSource.Oasis] = list;
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
        /// 초기화하고, 적용 이력이 있으면 새 소스 기준으로 색상을 재적용한다.
        /// 소스가 바뀌면 태그 셋도 달라지므로 레벨 타겟 인덱스는 다음 적용 때
        /// NeedsRebuild와 무관하게 새 태그로 재빌드한다.
        /// </summary>
        private void ApplyActiveSourceData(bool reapply)
        {
            _equipments = _equipmentsBySource.TryGetValue(_srcPanel.ActiveSource, out var list)
                ? list : new List<EquipmentData>();
            _matchedTagNos.Clear();
            _unmatchedTagNos.Clear();

            // 소스 전환 → 매칭 집합이 바뀌므로 범위 판정도 무효화. 재적용 경로가
            // 현재 범위를 새 매칭 기준으로 다시 계산하고, 재적용이 없으면(매칭 없음)
            // 범위 표시가 거짓말하지 않도록 전체 모델로 복귀시킨다.
            _scopeFilter.Invalidate();
            _scopeKeys = null;
            bool willReapply = reapply && _equipments.Count > 0 && _main.GetDocument() != null;
            if (!willReapply)
                _scopePanel.ResetToFullModel();

            _tabFilter.TabPages[0].Text = $"전체 ({_equipments.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            _needsIndexRebuild = true;
            FilterList();
            UpdateStats();

            // 색상이 이전 소스 기준으로 화면에 남아 있으면 경고 — 자동 재색칠은 안 함(§6).
            if (!willReapply && _appliedOnce && _equipments.Count > 0)
                _lblStats.Text = "⚠ 화면 색상은 이전 소스 기준 — [적용]을 눌러 새 소스로 갱신하세요";

            if (willReapply)
                BtnApply_Click(null, EventArgs.Empty);
        }

        private void ExportComparison()
        {
            if (!_equipmentsBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_equipmentsBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            var fields = new List<SourceComparer.Field<EquipmentData>>
            {
                new SourceComparer.Field<EquipmentData>("Description", eq => eq.Description ?? ""),
                new SourceComparer.Field<EquipmentData>("Sub System", eq => eq.SubSystem ?? ""),
                new SourceComparer.Field<EquipmentData>("RFQ No", eq => eq.RfqNo ?? ""),
                new SourceComparer.Field<EquipmentData>("Confirmed ETA",
                    eq => SourceComparer.FormatDate(eq.ConfirmedEta)),
            };
            foreach (var stage in EquipmentStageInfo.OrderedStages)
            {
                var captured = stage;
                fields.Add(new SourceComparer.Field<EquipmentData>(EquipmentStageInfo.Labels[captured], eq =>
                {
                    eq.StageDates.TryGetValue(captured, out var d);
                    return SourceComparer.FormatDate(d);
                }));
            }
            fields.Add(new SourceComparer.Field<EquipmentData>($"현재 단계({referenceDate:yyyy-MM-dd})",
                eq => EquipmentStageInfo.Labels[eq.GetStageAtDate(referenceDate)]));

            var lines = SourceComparer.BuildCsv("Tag No", excelList, oasisList, eq => eq.TagNo, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Equipment_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            MessageBox.Show($"비교 결과 저장 완료: {path}");
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_equipments.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            var doc = _main.GetDocument();
            var referenceDate = _dtpReference.Value;

            // Get fresh search results for diagnostics
            Dictionary<string, List<Autodesk.Navisworks.Api.ModelItem>> searchResult = null;
            if (doc != null && _main.EquipmentSearcher.IsIndexBuilt)
            {
                var allTags = _equipments.Select(eq => eq.TagNo).Distinct();
                searchResult = _main.EquipmentSearcher.FindByTagPrefix(allTags);
            }

            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add($"인덱스 스코프,\"{_main.EquipmentSearcher.LastScopeNote ?? "-"}\"");
            lines.Add("Tag No.,Description,Sub System,RFQ No.,Delivery,ETA,Stage,Matched,ModelItems,MatchedDisplayName");
            foreach (var eq in _equipments)
            {
                if (!InScope(eq.TagNo)) continue;
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
            lines.Add($"\"인덱스 항목 수: {_main.EquipmentSearcher.IndexedCount}\"");

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Equipment_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
            MessageBox.Show($"저장 완료: {path}\n인덱스: {_main.EquipmentSearcher.IndexedCount}건");
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();

            // Use level-targeted indexing with known tags from the active source
            var tagSet = new HashSet<string>(_equipments.Select(eq => eq.TagNo));
            _main.EquipmentSearcher.BuildIndexForTags(doc, tagSet, NwdScope.Equipment);
            _needsIndexRebuild = false;

            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _equipments.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_needsIndexRebuild || _main.EquipmentSearcher.NeedsRebuild(doc))
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

            // Matched set changed → scope verdicts are stale
            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            _appliedOnce = true;
            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
        }

        // ----- 현황 집계 범위 (aggregation scope) -----

        /// <summary>
        /// A row passes the active scope. No scope → all pass; a matched row passes when
        /// its node was judged inside the scope. An unmatched row (present in the data but
        /// with no model node — hence no position) is spatially unjudgeable, so it always
        /// passes: its count is a fixed, scope-independent figure shown in _lblUnmatched,
        /// not folded into the scoped 매칭 stats.
        /// </summary>
        private bool InScope(string id) =>
            _scopeKeys == null || !_matchedTagNos.Contains(id) || _scopeKeys.Contains(id);

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
                if (doc == null || _matchedTagNos.Count == 0)
                {
                    MessageBox.Show("먼저 적용(가시화)을 실행하세요. 집계 범위는 매칭된 항목에 적용됩니다.");
                    return;
                }
                if (_main.EquipmentSearcher.NeedsRebuild(doc) || _needsIndexRebuild)
                {
                    MessageBox.Show("모델 또는 데이터 소스가 변경되었습니다. 적용(가시화)을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    var itemsByKey = _main.EquipmentSearcher.FindByTagPrefix(_matchedTagNos);
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
            var itemsByKey = _main.EquipmentSearcher.FindByTagPrefix(_matchedTagNos);
            _scopeKeys = _scopeFilter.Apply(doc, scope, itemsByKey);
        }

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedTagNos.Count > 0 || _unmatchedTagNos.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null
                ? _matchedTagNos.Count
                : _matchedTagNos.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _equipments.Count : _equipments.Count(eq => InScope(eq.TagNo));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedTagNos.Count})";
        }

        private void BtnWriteProps_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _equipments.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_needsIndexRebuild || _main.EquipmentSearcher.NeedsRebuild(doc))
                BuildIndex();

            try
            {
                doc.CurrentSelection.Clear();
                _btnWriteProps.Enabled = false;
                _btnWriteProps.Text = "쓰는 중...";
                Application.DoEvents();

                var referenceDate = _dtpReference.Value;
                var allTagNos = _equipments.Select(eq => eq.TagNo).Distinct();
                var searchResult = _main.EquipmentSearcher.FindByTagPrefix(allTagNos);
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

        /// <summary>공종 초기화: 이 탭(Equipment) 색만 제거 — 다른 공종 색은 유지. 숨김도 복원.</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_tagHiddenByStage != null)
            {
                doc.Models.SetHidden(_tagHiddenByStage, false);
                _tagHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.ResetModule(doc, VisualModule.Equipment);
            _lblStats.Text = "공종 초기화 완료 (Equipment 색만 제거)";
        }

        private void BtnHideOthers_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;

            if (_tagHiddenByStage != null)
            {
                doc.Models.SetHidden(_tagHiddenByStage, false);
                _tagHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
                return;
            }

            if (_matchedTagNos.Count == 0)
            {
                MessageBox.Show("먼저 적용(가시화)을 실행하세요. 숨김은 매칭된 항목에 적용됩니다.");
                return;
            }
            if (_needsIndexRebuild || _main.EquipmentSearcher.NeedsRebuild(doc))
            {
                MessageBox.Show("모델 또는 데이터 소스가 변경되었습니다. 적용(가시화)을 다시 실행한 뒤 사용하세요.");
                return;
            }

            var checkedStages = new HashSet<EquipmentStage>(
                _colorRows.Where(kv => kv.Value.check.Checked).Select(kv => kv.Key));

            var referenceDate = _dtpReference.Value;
            var itemsByKey = _main.EquipmentSearcher.FindByTagPrefix(_matchedTagNos);
            var toHide = new Autodesk.Navisworks.Api.ModelItemCollection();

            foreach (var eq in _equipments)
            {
                if (!_matchedTagNos.Contains(eq.TagNo)) continue;
                var stage = eq.GetStageAtDate(referenceDate);
                if (checkedStages.Contains(stage)) continue;
                if (itemsByKey.TryGetValue(eq.TagNo, out var items))
                    toHide.AddRange(items);
            }

            if (toHide.Count == 0)
            {
                MessageBox.Show("숨길 대상이 없습니다 (모든 매칭 항목이 체크된 단계입니다).");
                return;
            }

            doc.Models.SetHidden(toHide, true);
            _tagHiddenByStage = toHide;
            _btnHideOthers.Text = "전체 보기";
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            // 숨김(체크 단계 외 숨김)도 함께 복원 — 초기화는 완전 원상복구여야 함
            if (_tagHiddenByStage != null)
            {
                doc.Models.SetHidden(_tagHiddenByStage, false);
                _tagHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 초기화 완료";
            _lblUnmatched.Text = "";
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

            var doc = _main.GetDocument();
            if (doc == null || !_main.EquipmentSearcher.IsIndexBuilt || _main.EquipmentSearcher.NeedsRebuild(doc)) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (ListViewItem selected in _listView.SelectedItems)
            {
                var equip = selected.Tag as EquipmentData;
                if (equip == null) continue;
                var found = _main.EquipmentSearcher.FindByTagPrefix(new[] { equip.TagNo });
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }

            if (collection.Count == 0) return;
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        /// <summary>[클립보드 복사] 버튼 → 공용 헬퍼 호출 후 결과 표시.</summary>
        private void CopyListToClipboard() => ShowCopied(ListViewClipboard.CopySelectedOrAll(_listView));

        private void ShowCopied(int n)
        {
            if (n > 0) _lblStats.Text = $"클립보드에 {n}행 복사됨";
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

            // Aggregation scope (matched rows only; unmatched rows are unjudgeable)
            if (_scopeKeys != null)
                filtered = filtered.Where(eq => InScope(eq.TagNo));

            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(eq => eq.TagNo.ToUpperInvariant().Contains(keyword)
                    || (eq.Description ?? "").ToUpperInvariant().Contains(keyword));

            _listView.Items.Clear();

            foreach (var equip in filtered)
            {
                var stage = equip.GetStageAtDate(referenceDate);
                string stageLabel = EquipmentStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedTagNos.Count > 0 || _unmatchedTagNos.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedTagNos.Contains(equip.TagNo) ? "O" : "X");

                var item = new ListViewItem(equip.TagNo);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(equip.Description ?? "");
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(matchLabel);
                if (matchLabel == "X")
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = equip;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedTagNos.Count > 0 || _unmatchedTagNos.Count > 0;

            // Stage 현황 counts MATCHED (in-scope) equipment only once matching is applied.
            // Unmatched rows have no model node, so folding their stages in is misleading —
            // the breakdown would show numbers even with 0 matches. Before applying, show all.
            var statBasis = _equipments.Where(eq => InScope(eq.TagNo));
            if (hasApplied)
                statBasis = statBasis.Where(eq => _matchedTagNos.Contains(eq.TagNo));
            var counts = statBasis
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
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null
                    ? _matchedTagNos.Count
                    : _matchedTagNos.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope}";
                if (_scopeKeys != null)
                    line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준)";
            }
            _lblStats.Text = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            _lblUnmatched.Text = hasApplied ? $"미매칭 {_unmatchedTagNos.Count}건" : "";
        }

        private Dictionary<EquipmentStage, ColorSetting> CloneDefaults(Dictionary<EquipmentStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<EquipmentStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
