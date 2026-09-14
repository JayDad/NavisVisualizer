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
    public class HydrotestTab : UserControl, IOverviewSource
    {
        private readonly MainDockablePanel _main;

        private List<TestPackageData> _packages = new List<TestPackageData>();
        private readonly Dictionary<TabDataSource, List<TestPackageData>> _packagesBySource
            = new Dictionary<TabDataSource, List<TestPackageData>>();
        private bool _appliedOnce;
        private Dictionary<HydrotestStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedPkgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedPkgIds = new List<string>();

        // Aggregation scope (현황 집계 범위) — null = 전체 모델 (no filtering)
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;

        private DataSourcePanel _srcPanel;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private SearchScopeCombo _searchScope;   // 검색 범위(전체/열) 드롭다운
        private Debouncer _searchDebounce;   // 키 입력마다 리스트 재계산 방지 (성능 audit P0-1)
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnResetModule;   // 이 공종(Hydrotest) 색만 제거
        private Button _btnReset;
        private Button _btnHideOthers;    // 체크된 단계 PKG만 남기고 나머지 3D 숨김 (토글)
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Autodesk.Navisworks.Api.ModelItemCollection _pkgHiddenByStage; // 숨긴 것 복원용
        private Label _lblStats;
        private Label _lblUnmatched;   // fixed 미매칭(모델 없음) count, pinned to the corner
        private Label _lblCopied;            // 복사 피드백 (우측 코너, 4초 후 소거)
        private System.Windows.Forms.Timer _copiedClear;
        private ApplyStatePanel _applyState;   // 3D 적용 상태 표시 (데이터↔3D 어긋남 경고 전담)
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<HydrotestStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<HydrotestStage, (Panel, Button, ComboBox, CheckBox)>();

        public HydrotestTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.HydrotestDefaults);
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

            // 색상 패널·기준일 핸들러가 참조하므로 먼저 생성 (버튼 연결은 버튼 행에서).
            _applyState = new ApplyStatePanel();

            _srcPanel = new DataSourcePanel();
            _srcPanel.ExcelLoadClicked    += (s, e) => LoadExcel();
            _srcPanel.TemplateClicked     += (s, e) => ExportInputTemplate();
            _srcPanel.OasisLoadClicked    += (s, e) => LoadOasis();
            // 라디오 전환은 자동 재적용 안 함(§6) — 대형 모델 수 초 블로킹 방지. 색칠은 [적용] 시에만.
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData(reapply: false);
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();
            // 공사(전역) 변경 → 로드된 OASIS 데이터는 이전 공사 기준. 자동 재로드는 하지 않고
            // (네트워크 대기로 UI가 멈추므로) 3D 상태만 stale로 표시한다. 활성 소스가
            // Excel이면 공사와 무관하므로 아무것도 하지 않는다(거짓 경고 방지).
            _srcPanel.ProjectChanged      += (s, e) =>
            {
                if (_srcPanel.ActiveSource == TabDataSource.Oasis && _srcPanel.IsOasisProjectStale)
                    _applyState.MarkStale("공사 변경 — OASIS 재로드 필요");
            };

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
                if (_packages.Count > 0) { FilterList(); UpdateStats(); _applyState.MarkStale("기준일 변경"); }
            };
            datePanel.Controls.Add(dateLabel);
            datePanel.Controls.Add(_dtpReference);

            var colorPanel = BuildColorPanel();

            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 210, Text = "" };
            // 입력 즉시가 아니라 입력이 멈춘 뒤 1회만 필터 실행 (성능 audit P0-1)
            _searchDebounce = new Debouncer(FilterList);
            _txtSearch.TextChanged += (s, e) => _searchDebounce.Trigger();
            searchPanel.Controls.Add(_txtSearch);
            // 검색 범위 드롭다운 — 전체(전 필드) 또는 특정 열만. 열 채우기·이벤트 연결은 Columns.Add 뒤
            // (초기 SelectedIndex=0이 구성 중 FilterList를 호출하지 않도록 이벤트는 Populate 후 연결).
            _searchScope = new SearchScopeCombo();
            searchPanel.Controls.Add(_searchScope);
            var btnExport = new Button { Text = "매칭 Status 엑셀 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            // 선택 행(없으면 표시 중인 전체 행)을 클립보드로 복사 — Ctrl+C 대체 버튼.
            var btnCopy = new Button { Text = "선택항목 클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            new System.Windows.Forms.ToolTip().SetToolTip(btnCopy, "선택한 행을 복사합니다. 선택이 없으면 표시 중인 전체 행을 복사합니다.");
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
            // 복사 피드백 — 통계 라벨을 덮지 않도록 우측 코너(미매칭 오른쪽)에 표시하고
            // 4초 후 자동 소거 (종전엔 _lblStats를 덮어써 단계 현황이 사라졌다 — 2026-07 사용자 요청).
            _lblCopied = new Label
            {
                Dock = DockStyle.Right, Width = 0, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, ForeColor = Color.Gray, Text = "",
            };
            statsRow.Controls.Add(_lblCopied);   // 마지막 추가 = 가장 오른쪽에 도킹
            _copiedClear = new System.Windows.Forms.Timer { Interval = 4000 };
            _copiedClear.Tick += (s, e) => { _copiedClear.Stop(); _lblCopied.Text = ""; _lblCopied.Width = 0; };

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
            _listView.Columns.Add("Test Pkg No.", 170);
            _listView.Columns.Add("System", 80);
            _listView.Columns.Add("Service", 50);
            _listView.Columns.Add("단계", 75);
            _listView.Columns.Add("매칭", 45);
            _searchScope.Populate(_listView);   // "전체" + 각 열 ("#" 컬럼 없음)
            _searchScope.SelectedIndexChanged += (s, e) => FilterList();
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            // ListView는 기본적으로 Ctrl+C를 지원하지 않으므로 공용 헬퍼로 배선.
            ListViewClipboard.EnableCtrlC(_listView, ShowCopied);
            tabAll.Controls.Add(_listView);

            // 1행(가시화): 가시화 적용 · 체크 단계 외 숨김 · 3D 적용 상태
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply      = new Button { Text = "가시화 적용",       Width = 90  };
            _btnHideOthers = new Button { Text = "체크 단계 외 숨김", Width = 140 };
            _btnApply.Click      += BtnApply_Click;
            _btnHideOthers.Click += BtnHideOthers_Click;
            _applyState.AttachApplyButton(_btnApply);
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnHideOthers, _applyState });

            // 2행(해제): 이 탭 가시화 해제(이 공종 색만) · 전체 가시화 해제(모든 공종)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            _btnReset       = new Button { Text = "전체 가시화 해제", Width = 130 };
            _btnResetModule.Click += BtnResetModule_Click;
            _btnReset.Click       += BtnReset_Click;
            btnPanelReset.Controls.AddRange(new Control[] { _btnResetModule, _btnReset });

            // 3행(덜 쓰임): Viewpoint 저장 · NWD Export
            var btnPanel2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd       = new Button { Text = "NWD Export",     Width = 110 };
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel2.Controls.AddRange(new Control[] { _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            // 색상 편집(▼·투명도)은 기본 접힘 — 체크박스·스와치만 상시 노출 (UX audit P1)
            layout.Controls.Add(ColorEditCollapse.BuildToggleRow(colorPanel));
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
            var allStages = new[] { HydrotestStage.NotStarted }.Concat(HydrotestStageInfo.OrderedStages).ToArray();
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
                string label = HydrotestStageInfo.Labels[stage];

                var chk = new CheckBox { Text = label, Checked = true, AutoSize = true };
                chk.CheckedChanged += (s, e) => _applyState.MarkStale("단계 선택 변경");
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

        private void LoadExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Hydrotest Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var list = ExcelLoader.LoadHydrotest(dlg.FileName, out int dup);
                    _packagesBySource[TabDataSource.Excel] = list;
                    _srcPanel.SetLoaded(TabDataSource.Excel, list.Count,
                        Path.GetFileName(dlg.FileName) + (dup > 0 ? $" · 중복 {dup}건 제외" : ""));
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
                string path = InputTemplate.ExportHydrotest();
                SaveNotifier.ShowSaved(this, "Template 출력", path,
                    "작성 후 Excel 형식(.xlsx)으로 저장해 Import 하세요.");
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
                var list = SqlLoader.LoadHydrotest(settings);
                _packagesBySource[TabDataSource.Oasis] = list;
                // 코드만이 아니라 공사명까지 표기 ("Trion (Q557)") — 어느 공사 데이터를
                // 보고 있는지가 라벨 한 줄로 확정되도록.
                string prj = ProjectContext.CurrentDisplay;
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
        /// </summary>
        private void ApplyActiveSourceData(bool reapply)
        {
            _packages = _packagesBySource.TryGetValue(_srcPanel.ActiveSource, out var list)
                ? list : new List<TestPackageData>();
            _matchedPkgIds.Clear();
            _unmatchedPkgIds.Clear();

            // 소스 전환 → 매칭 집합이 바뀌므로 범위 판정도 무효화. 재적용 경로가
            // 현재 범위를 새 매칭 기준으로 다시 계산하고, 재적용이 없으면(매칭 없음)
            // 범위 표시가 거짓말하지 않도록 전체 모델로 복귀시킨다.
            _scopeFilter.Invalidate();
            _scopeKeys = null;
            bool willReapply = reapply && _packages.Count > 0 && _main.GetDocument() != null;
            if (!willReapply)
                _scopePanel.ResetToFullModel();

            _tabFilter.TabPages[0].Text = $"전체 ({_packages.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            FilterList();
            UpdateStats();

            // 색상이 이전 소스 기준으로 화면에 남아 있으면 상태 표시기로 경고 —
            // 자동 재색칠은 안 함(§6). 통계 라벨은 통계만 표시한다 (UX audit P0-1).
            if (!willReapply)
                _applyState.MarkStale("데이터 변경");

            if (willReapply)
                BtnApply_Click(null, EventArgs.Empty);
        }

        private void ExportComparison()
        {
            if (!_packagesBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_packagesBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            var fields = new List<SourceComparer.Field<TestPackageData>>
            {
                new SourceComparer.Field<TestPackageData>("System No", p => p.SystemNo ?? ""),
                new SourceComparer.Field<TestPackageData>("Line Service", p => p.LineService ?? ""),
            };
            foreach (var stage in HydrotestStageInfo.OrderedStages)
            {
                var captured = stage;
                fields.Add(new SourceComparer.Field<TestPackageData>(HydrotestStageInfo.Labels[captured], p =>
                {
                    p.StageDates.TryGetValue(captured, out var d);
                    return SourceComparer.FormatDate(d);
                }));
            }
            fields.Add(new SourceComparer.Field<TestPackageData>($"현재 단계({referenceDate:yyyy-MM-dd})",
                p => HydrotestStageInfo.Labels[p.GetStageAtDate(referenceDate)]));

            var lines = SourceComparer.BuildCsv("PKG No", excelList, oasisList, p => p.TestPkgId, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Hydrotest_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "Excel↔OASIS 비교 출력", path);
        }

        private void IncrementalUpdate(string stageKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.Hydrotest)) return;

            if (Enum.TryParse<HydrotestStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.Hydrotest, stageKey, setting);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_packages.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            var referenceDate = _dtpReference.Value;
            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add($"인덱스 스코프,\"{_main.HydroTagSearcher.LastScopeNote ?? "-"}\"");
            lines.Add("Test Pkg No.,System No.,Line Service,Stage,Matched");
            foreach (var pkg in _packages)
            {
                if (!InScope(pkg.TestPkgId)) continue;
                var stage = pkg.GetStageAtDate(referenceDate);
                string stageLabel = HydrotestStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedPkgIds.Count == 0 || _matchedPkgIds.Contains(pkg.TestPkgId);
                lines.Add($"\"{pkg.TestPkgId}\",\"{pkg.SystemNo}\",\"{pkg.LineService}\",\"{stageLabel}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Hydrotest_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
            SaveNotifier.ShowSaved(this, "매칭 Status 엑셀 출력", path);
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            // 단순 marquee만으로는 무엇을 하는지 알 수 없어 단계 문구 병기 (UX audit P0-3)
            _lblStats.Text = "모델 태그 인덱스 생성 중…";
            Application.DoEvents();
            _main.HydroTagSearcher.BuildIndex(doc, NwdScope.Hydrotest);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _packages.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_main.HydroTagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<HydrotestStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            // 색칠은 수천 PKG permanent override — 진행바+단계 문구로 UI 프리즈 인상 제거.
            OverrideResult result;
            _btnApply.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "색상 적용 중…";
            Application.DoEvents();
            try
            {
                result = _main.OverrideEngine.ApplyHydrotest(doc, _packages, activeSettings, referenceDate);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
                _btnApply.Enabled = true;
            }

            _unmatchedPkgIds = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedPkgIds = new HashSet<string>(
                _packages.Select(p => p.TestPkgId).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            // Matched set changed → scope verdicts are stale
            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            _appliedOnce = true;
            _applyState.SetApplied($"{SourceLabel()} · 기준일 {referenceDate:MM-dd}");
            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
        }

        private string SourceLabel() =>
            _srcPanel.ActiveSource == TabDataSource.Oasis ? "OASIS" : "Excel";

        /// <summary>Overview 탭 상태 노출 — 인메모리 조회만 (IOverviewSource).</summary>
        public OverviewStatus GetOverviewStatus()
        {
            bool hasApplied = _matchedPkgIds.Count > 0 || _unmatchedPkgIds.Count > 0;
            return new OverviewStatus
            {
                DataLoaded = _packages.Count > 0,
                DataText = _packages.Count > 0 ? $"{SourceLabel()} {_packages.Count:N0}건" : "미로드",
                IndexText = _main.HydroTagSearcher.IsIndexBuilt
                    ? _main.HydroTagSearcher.IndexedCount.ToString("N0") : "-",
                ApplyStateText = _applyState.Text,
                ApplyStale = _applyState.IsStale,
                MatchedText = hasApplied ? _matchedPkgIds.Count.ToString("N0") : "-",
                UnmatchedText = hasApplied ? _unmatchedPkgIds.Count.ToString("N0") : "-",
                UnmatchedCount = hasApplied ? _unmatchedPkgIds.Count : 0,
                ScopeNote = _main.HydroTagSearcher.LastScopeNote ?? "-",
                ScopeFellBack = _main.HydroTagSearcher.LastScopeFellBack,
            };
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
            _scopeKeys == null || !_matchedPkgIds.Contains(id) || _scopeKeys.Contains(id);

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
                if (doc == null || _matchedPkgIds.Count == 0)
                {
                    MessageBox.Show("먼저 [가시화 적용]을 실행하세요. 집계 범위는 매칭된 항목에 적용됩니다.");
                    return;
                }
                if (_main.HydroTagSearcher.NeedsRebuild(doc))
                {
                    MessageBox.Show("모델이 변경되었습니다. [가시화 적용]을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    var itemsByKey = _main.HydroTagSearcher.FindBySpoolIds(_matchedPkgIds);
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
            var itemsByKey = _main.HydroTagSearcher.FindBySpoolIds(_matchedPkgIds);
            _scopeKeys = _scopeFilter.Apply(doc, scope, itemsByKey);
        }

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedPkgIds.Count > 0 || _unmatchedPkgIds.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null
                ? _matchedPkgIds.Count
                : _matchedPkgIds.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _packages.Count : _packages.Count(p => InScope(p.TestPkgId));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedPkgIds.Count})";
        }

        /// <summary>이 탭 가시화 해제: 이 탭(Hydrotest) 색만 제거 — 다른 공종 색은 유지. 숨김도 복원.</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_pkgHiddenByStage != null)
            {
                doc.Models.SetHidden(_pkgHiddenByStage, false);
                _pkgHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.ResetModule(doc, VisualModule.Hydrotest);
            _lblStats.Text = "이 탭 가시화 해제 완료 (Hydrotest 색만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            // 숨김(체크 단계 외 숨김)도 함께 복원 — 초기화는 완전 원상복구여야 함
            if (_pkgHiddenByStage != null)
            {
                doc.Models.SetHidden(_pkgHiddenByStage, false);
                _pkgHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 가시화 해제 완료";
            _lblUnmatched.Text = "";
            _applyState.SetCleared();
        }

        /// <summary>
        /// 체크된 단계(기준일)에 해당하는 매칭 PKG만 남기고 나머지 매칭 PKG를 3D에서 숨긴다.
        /// 토글: 이미 숨긴 상태면 복원. 색상과 독립(숨김은 렌더링 제외라 투명보다 가벼움).
        /// </summary>
        private void BtnHideOthers_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;

            if (_pkgHiddenByStage != null)
            {
                doc.Models.SetHidden(_pkgHiddenByStage, false);
                _pkgHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
                return;
            }

            if (_matchedPkgIds.Count == 0)
            {
                MessageBox.Show("먼저 [가시화 적용]을 실행하세요. 숨김은 매칭된 항목에 적용됩니다.");
                return;
            }
            if (_main.HydroTagSearcher.NeedsRebuild(doc))
            {
                MessageBox.Show("모델이 변경되었습니다. [가시화 적용]을 다시 실행한 뒤 사용하세요.");
                return;
            }

            var checkedStages = new HashSet<HydrotestStage>(
                _colorRows.Where(kv => kv.Value.check.Checked).Select(kv => kv.Key));

            var referenceDate = _dtpReference.Value;
            var itemsByKey = _main.HydroTagSearcher.FindBySpoolIds(_matchedPkgIds);
            var toHide = new Autodesk.Navisworks.Api.ModelItemCollection();

            foreach (var pkg in _packages)
            {
                if (!_matchedPkgIds.Contains(pkg.TestPkgId)) continue;
                var stage = pkg.GetStageAtDate(referenceDate);
                if (checkedStages.Contains(stage)) continue;
                if (itemsByKey.TryGetValue(pkg.TestPkgId, out var items))
                    toHide.AddRange(items);
            }

            if (toHide.Count == 0)
            {
                MessageBox.Show("숨길 대상이 없습니다 (모든 매칭 항목이 체크된 단계입니다).");
                return;
            }

            doc.Models.SetHidden(toHide, true);
            _pkgHiddenByStage = toHide;
            _btnHideOthers.Text = "전체 보기";
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"Hydrotest_{DateTime.Now:yyyyMMdd_HHmm}";
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
            if (doc == null || !_main.HydroTagSearcher.IsIndexBuilt || _main.HydroTagSearcher.NeedsRebuild(doc)) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (ListViewItem selected in _listView.SelectedItems)
            {
                var pkg = selected.Tag as TestPackageData;
                if (pkg == null) continue;
                var found = _main.HydroTagSearcher.FindBySpoolIds(new[] { pkg.TestPkgId });
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }

            if (collection.Count == 0) return;
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        /// <summary>[선택항목 클립보드 복사] 버튼 → 공용 헬퍼 호출 후 우측 코너에 결과 표시.</summary>
        private void CopyListToClipboard() => ShowCopied(ListViewClipboard.CopySelectedOrAll(_listView));

        private void ShowCopied(int n)
        {
            if (n <= 0) return;
            _lblCopied.Text = $"클립보드에 {n}행 복사됨";
            _lblCopied.Width = 150;
            _copiedClear.Stop();
            _copiedClear.Start();
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
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _packages.AsEnumerable();

            if (tabIndex == 1 && _matchedPkgIds.Count > 0)
                filtered = filtered.Where(p => _matchedPkgIds.Contains(p.TestPkgId));
            else if (tabIndex == 2 && _unmatchedPkgIds.Count > 0)
                filtered = filtered.Where(p => _unmatchedPkgIds.Contains(p.TestPkgId));

            // Aggregation scope (matched rows only; unmatched rows are unjudgeable)
            if (_scopeKeys != null)
                filtered = filtered.Where(p => InScope(p.TestPkgId));

            if (!string.IsNullOrEmpty(keyword))
            {
                int scopeCol = _searchScope?.SelectedColumn ?? -1;
                if (scopeCol < 0)
                    filtered = filtered.Where(p => p.TestPkgId.ToUpperInvariant().Contains(keyword)
                        || (p.SystemNo ?? "").ToUpperInvariant().Contains(keyword));
                else
                    filtered = filtered.Where(p => CellText(p, scopeCol).ToUpperInvariant().Contains(keyword));
            }

            _listView.Items.Clear();

            foreach (var pkg in filtered)
            {
                var stage = pkg.GetStageAtDate(referenceDate);
                string stageLabel = HydrotestStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedPkgIds.Count > 0 || _unmatchedPkgIds.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedPkgIds.Contains(pkg.TestPkgId) ? "O" : "X");

                var item = new ListViewItem(pkg.TestPkgId);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(pkg.SystemNo ?? "");
                item.SubItems.Add(pkg.LineService ?? "");
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(matchLabel);
                if (matchLabel == "X")
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = pkg;
                _listView.Items.Add(item);
            }
        }

        /// <summary>검색 범위(특정 열)용 — 열 인덱스의 표시 텍스트 (행 빌드 sub-item과 동일 값).</summary>
        private string CellText(TestPackageData pkg, int col)
        {
            var referenceDate = _dtpReference.Value;
            switch (col)
            {
                case 0: return pkg.TestPkgId ?? "";
                case 1: return pkg.SystemNo ?? "";
                case 2: return pkg.LineService ?? "";
                case 3:
                    var stage = pkg.GetStageAtDate(referenceDate);
                    return HydrotestStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                case 4:
                    bool hasApplied = _matchedPkgIds.Count > 0 || _unmatchedPkgIds.Count > 0;
                    return !hasApplied ? "-" : (_matchedPkgIds.Contains(pkg.TestPkgId) ? "O" : "X");
                default: return "";
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedPkgIds.Count > 0 || _unmatchedPkgIds.Count > 0;

            // Stage 현황 counts MATCHED (in-scope) packages only once matching is applied.
            // Unmatched rows have no model node, so folding their stages in is misleading —
            // the breakdown would show numbers even with 0 matches. Before applying, show all.
            var statBasis = _packages.Where(p => InScope(p.TestPkgId));
            if (hasApplied)
                statBasis = statBasis.Where(p => _matchedPkgIds.Contains(p.TestPkgId));
            var counts = statBasis
                .GroupBy(p => p.GetStageAtDate(referenceDate))
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { HydrotestStage.NotStarted }.Concat(HydrotestStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{HydrotestStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null
                    ? _matchedPkgIds.Count
                    : _matchedPkgIds.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope}";
                if (_scopeKeys != null)
                    line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준)";
            }
            _lblStats.Text = string.Join("  ", parts)
                           + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            _lblUnmatched.Text = hasApplied ? $"미매칭 {_unmatchedPkgIds.Count}건" : "";
        }

        private Dictionary<HydrotestStage, ColorSetting> CloneDefaults(Dictionary<HydrotestStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<HydrotestStage, ColorSetting>();
            foreach (var kv in defaults)
                clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
