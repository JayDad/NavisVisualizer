using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;
using Color = System.Drawing.Color;
using View = System.Windows.Forms.View;
using Application = System.Windows.Forms.Application;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// Cable(형상) 탭 — 07_Trion_All_Cable.nwd의 cable-no 컴포넌트를 직접 매칭·하이라이트한다
    /// (기존 Cable Pull은 트레이 노드 박스 집계 — 별개). Excel↔OASIS 듀얼소스
    /// (EIT_Cable 철자 실측 확정 2026-07 — SqlLoader.LoadCable). 주요 기능: ① 케이블 목록
    /// 하이라이트(stage 날짜 없으면 단색), ② 날짜 기반 4단계 공정 시각화, ③ 집계 범위
    /// Clipping 영역 = 단면 통과 clash 판정(선분-vs-볼륨), ④ 겹침 완화(숨김 isolate).
    /// (구 전용 clash 추출 버튼·필터 포커스는 2026-07 사용자 결정으로 삭제 — 추출은
    /// Clipping 영역 + 매칭 Status 엑셀 출력이 대체, 부분집합 보기는 'Cable 찾기'(텍스트 입력)가
    /// 원래 의도.) 미매칭은 스코프와
    /// 직교(전역 고정, 코너 라벨 — §7/L3).
    /// </summary>
    public class CableLineTab : UserControl, IOverviewSource
    {
        private readonly MainDockablePanel _main;

        private readonly Dictionary<TabDataSource, List<CableLineData>> _cablesBySource
            = new Dictionary<TabDataSource, List<CableLineData>>();
        private List<CableLineData> _cables = new List<CableLineData>();
        private Dictionary<CableLineStage, ColorSetting> _colorSettings;
        private ColorSetting _highlightSetting;
        private bool _lastHighlightMode;

        private HashSet<string> _matchedCableNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedCableNos = new List<string>();

        // Aggregation scope (현황 집계 범위) — Clipping 영역은 clash(선분-vs-볼륨)로 판정.
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;
        private readonly CableClashService _clash = new CableClashService();
        private readonly Func<string, List<ModelItem>, IList<ClipPlane>, bool> _volumeJudge;

        // 레벨 타겟 인덱스는 활성 소스 태그 셋 기반 → 로드/소스 전환 시 재빌드 플래그(Spool 패턴).
        private bool _needsIndexRebuild;

        // 겹침 완화용 숨김(isolate 버튼들 공유 — 상호배타). null = 숨김 없음.
        private ModelItemCollection _cableHidden;

        // 텍스트로 받은 케이블 부분집합 필터 (정규화 키). null = 필터 없음.
        // 리스트를 이 집합으로 좁히고, 매칭 케이블이 있으면 3D도 그것만 남기고 숨긴다.
        private HashSet<string> _listFilter;
        private string _cableFilterText;   // 재입력 시 prefill 원문
        private Button _btnListFilter;

        private Document _subscribedDoc;
        private bool _suppressSelectionSync;

        private DataSourcePanel _srcPanel;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private Debouncer _searchDebounce;   // 키 입력마다 리스트 재계산 방지 (성능 audit P0-1)
        private TabControl _tabFilter;
        private ListView _listView;
        // VirtualMode ListView의 백킹 행 — 보이는 행만 ListViewItem으로 생성 (성능 audit P0-2).
        private List<CableLineData> _viewRows = new List<CableLineData>();
        private Button _btnApply;
        private Button _btnHideOthers;   // 체크 단계 외 숨김
        private Button _btnIsolateSel;   // 선택 케이블만 보기
        private Button _btnResetModule;
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private Label _lblUnmatched;
        private Label _lblCopied;            // 복사 피드백 (우측 코너, 4초 후 소거)
        private System.Windows.Forms.Timer _copiedClear;
        private ApplyStatePanel _applyState;   // 3D 적용 상태 표시 (데이터↔3D 어긋남 경고 전담)
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<CableLineStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<CableLineStage, (Panel, Button, ComboBox, CheckBox)>();

        public CableLineTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.CableLineDefaults);
            _highlightSetting = ColorSetting.CableLineHighlight;
            _scopeFilter = new ScopeFilter(main.SectionSvc);
            _volumeJudge = (cableNo, items, planes) =>
                _clash.PassesVolume(cableNo, items, planes, _main.SectionSvc.KeepPositiveSide);
            InitializeComponent();
            // 문서 전환/같은 파일 재로드 → clash 형상 캐시(세그먼트·bbox)도 무효화 (2차 audit P1 —
            // 같은 파일 재로드는 지문이 안 바뀌어 EnsureFresh로는 못 잡는다).
            _main.IndexesInvalidated += OnIndexesInvalidated;
            this.HandleDestroyed += (s, e) =>
            {
                UnsubscribeSelection();
                _main.IndexesInvalidated -= OnIndexesInvalidated;
            };
        }

        private void OnIndexesInvalidated() => _clash.Invalidate();

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
            // 라디오 전환은 절대 자동 재적용하지 않는다(§6) — 리스트/통계만 새 소스로 갱신.
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData();
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();

            var datePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            datePanel.Controls.Add(new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _dtpReference = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 110 };
            _dtpReference.ValueChanged += (s, e) =>
            { if (_cables.Count > 0) { FilterList(); UpdateStats(); _applyState.MarkStale("기준일 변경"); } };
            datePanel.Controls.Add(_dtpReference);
            datePanel.Controls.Add(new Label { Text = "(stage 날짜 없으면 하이라이트 단색)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(6, 4, 0, 0) });

            var colorPanel = BuildColorPanel();

            // 1행(가시화)
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply       = new Button { Text = "가시화 적용",       Width = 90 };
            _btnHideOthers  = new Button { Text = "체크 단계 외 숨김",  Width = 130 };
            _btnIsolateSel  = new Button { Text = "선택 케이블만 보기", Width = 130 };
            _btnApply.Click      += BtnApply_Click;
            _btnHideOthers.Click += BtnHideOthers_Click;
            _btnIsolateSel.Click += BtnIsolateSel_Click;
            _applyState.AttachApplyButton(_btnApply);
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnHideOthers, _btnIsolateSel, _applyState });

            // 2행(해제)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            _btnReset       = new Button { Text = "전체 가시화 해제", Width = 130 };
            _btnResetModule.Click += BtnResetModule_Click;
            _btnReset.Click       += BtnReset_Click;
            btnPanelReset.Controls.AddRange(new Control[] { _btnResetModule, _btnReset });

            // 3행(출력)
            var btnPanel2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd       = new Button { Text = "NWD Export",     Width = 110 };
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel2.Controls.AddRange(new Control[] { _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            // Stats row: scoped stats left, fixed 미매칭(모델 없음) count pinned right (§7/L3).
            var statsRow = new Panel { Dock = DockStyle.Fill, Height = 36 };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false };
            _lblUnmatched = new Label
            {
                Dock = DockStyle.Right, Width = 150, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, ForeColor = Color.Gray, Text = "",
            };
            statsRow.Controls.Add(_lblStats);
            statsRow.Controls.Add(_lblUnmatched);
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

            // 버튼·안내 라벨이 많아 한 줄을 넘으므로 줄바꿈 허용 (버튼 행들과 동일 패턴)
            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = true };
            searchPanel.Controls.Add(new Label { Text = "검색(Cable/Equip):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 170 };
            // 검색 리스트는 debounce로만 갱신 — Enter는 즉시 확정 (성능 audit P0-1).
            // (구 "필터 포커스" 투명 dim은 2026-07 사용자 결정으로 삭제 — 원래 의도였던
            //  "가시화할 케이블 리스트만 보기"는 'Cable 찾기'(텍스트 입력)가 담당. clash 추출 버튼도
            //  삭제 — 집계 범위 Clipping 영역 + 매칭 Status 엑셀 출력으로 동일 결과.)
            _searchDebounce = new Debouncer(FilterList);
            _txtSearch.TextChanged += (s, e) => _searchDebounce.Trigger();
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    _searchDebounce.Flush();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 엑셀 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);
            // 선택 행(없으면 표시 중인 전체 행)을 클립보드로 복사 — Ctrl+C 대체 버튼.
            var btnCopy = new Button { Text = "선택항목 클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            new System.Windows.Forms.ToolTip().SetToolTip(btnCopy, "선택한 행을 복사합니다. 선택이 없으면 표시 중인 전체 행을 복사합니다.");
            btnCopy.Click += (s, e) => CopyListToClipboard();
            searchPanel.Controls.Add(btnCopy);
            _btnListFilter = new Button { Text = "Cable 찾기", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0) };
            _btnListFilter.Click += BtnListFilter_Click;
            new System.Windows.Forms.ToolTip().SetToolTip(_btnListFilter,
                "가시화하고 싶은 케이블 번호를 텍스트로 붙여넣어(공백/개행/콤마 구분) 리스트와 3D를 그 부분집합만 표시합니다 (재클릭 = 해제).");
            searchPanel.Controls.Add(_btnListFilter);
            // 버튼명만으로는 용도가 안 보여 상시 안내 병기 (사용자 요청 2026-07)
            searchPanel.Controls.Add(new Label
            {
                Text = "← 가시화하고 싶은 케이블 리스트 입력",
                ForeColor = Color.Gray,
                AutoSize = true,
                Padding = new Padding(0, 5, 6, 0),
            });

            _scopePanel = new ScopePanel { Dock = DockStyle.Fill };
            _scopePanel.ApplyRequested += (s, e) => ApplyScope();

            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 240 };
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
                Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true,
                View = View.Details, HideSelection = false,
                // 가상 모드 (성능 audit P0-2) — 2만 케이블에서 보이는 행만 생성.
                VirtualMode = true, VirtualListSize = 0,
            };
            _listView.Columns.Add("#", 40);
            _listView.Columns.Add("Cable No", 150);
            _listView.Columns.Add("단계", 70);
            _listView.Columns.Add("From", 110);
            _listView.Columns.Add("To", 110);
            _listView.Columns.Add("Design", 60);
            _listView.Columns.Add("Pulled", 60);
            _listView.Columns.Add("%", 46);
            _listView.Columns.Add("매칭", 40);
            _listView.RetrieveVirtualItem += (s, e) =>
            {
                e.Item = (e.ItemIndex >= 0 && e.ItemIndex < _viewRows.Count)
                    ? BuildRow(_viewRows[e.ItemIndex], e.ItemIndex)
                    : new ListViewItem("");
            };
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            // ListView는 기본적으로 Ctrl+C를 지원하지 않으므로 공용 헬퍼로 배선.
            ListViewClipboard.EnableCtrlC(_listView, ShowCopied);
            tabAll.Controls.Add(_listView);

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상 (하이라이트 단색 포함)", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
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
            // Cable의 Clipping 영역은 타 공종(BoundingBox 중심점 판정)과 달리 형상 선분
            // clash(선분-vs-볼륨)로 판정한다 — 케이블은 start–end가 이어진 긴 형상이라
            // 중심점이 볼륨 밖이어도 몸통이 단면을 관통하면 집계된다. 이 차이를 화면에서
            // 바로 알 수 있게 상시 remark로 노출 (사용자 요청 2026-07).
            layout.Controls.Add(new Label
            {
                Text = "※ Clipping 영역: 단면 볼륨을 통과(관통)하는 케이블이 집계됩니다.",
                ForeColor = Color.Gray,
                Dock = DockStyle.Fill,
                Height = 16,
                AutoSize = false,
            });
            layout.Controls.Add(_tabFilter);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var allStages = new[] { CableLineStage.NotStarted }.Concat(CableLineStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
            for (int col = 0; col < 4; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col % 4 == 0 ? 100 : (col % 4 == 3 ? 62 : (col % 4 == 1 ? 36 : 22))));

            int r = 0;
            foreach (var stage in allStages)
            {
                AddColorRow(layout, r++, CableLineStageInfo.Labels[stage], _colorSettings[stage],
                    () => _colorSettings[stage].DisplayColor,
                    c => { _colorSettings[stage].DisplayColor = c; IncrementalUpdate(stage.ToString()); },
                    t => { _colorSettings[stage].Transparency = t; IncrementalUpdate(stage.ToString()); },
                    chk => _colorRows[stage] = (null, null, null, chk));
            }
            // 하이라이트(단색) 행 — 하이라이트 우선 모드에서만 사용, 체크박스 없음.
            AddColorRow(layout, r, "하이라이트", _highlightSetting,
                () => _highlightSetting.DisplayColor,
                c => { _highlightSetting.DisplayColor = c; IncrementalUpdate(ColorOverrideEngine.CableLineHighlightGroup); },
                t => { _highlightSetting.Transparency = t; IncrementalUpdate(ColorOverrideEngine.CableLineHighlightGroup); },
                null);

            panel.Controls.Add(layout);
            return panel;
        }

        /// <summary>색상 행 1개 배치. hasCheck=null이면 체크박스 대신 라벨(하이라이트 행).</summary>
        private void AddColorRow(TableLayoutPanel layout, int row, string label, ColorSetting setting,
            Func<Color> getColor, Action<Color> setColor, Action<double> setTransparency, Action<CheckBox> registerCheck)
        {
            Control first;
            if (registerCheck != null)
            {
                var chk = new CheckBox { Text = label, Checked = true, AutoSize = true };
                chk.CheckedChanged += (s, e) => _applyState.MarkStale("단계 선택 변경");
                registerCheck(chk);
                first = chk;
            }
            else
            {
                first = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            }

            var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
            var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
            colorBtn.FlatAppearance.BorderSize = 0;
            var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                transparencyBox.Items.Add(t);
            transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

            colorBtn.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = getColor() })
                    if (dlg.ShowDialog() == DialogResult.OK) { setColor(dlg.Color); colorBox.BackColor = dlg.Color; }
            };
            transparencyBox.SelectedIndexChanged += (s, e) =>
            {
                if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    setTransparency(pct / 100.0);
            };

            layout.Controls.Add(first, 0, row);
            layout.Controls.Add(colorBox, 1, row);
            layout.Controls.Add(colorBtn, 2, row);
            layout.Controls.Add(transparencyBox, 3, row);
        }

        private void IncrementalUpdate(string groupKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.CableLine)) return;
            if (groupKey == ColorOverrideEngine.CableLineHighlightGroup)
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.CableLine, groupKey, _highlightSetting);
            else if (Enum.TryParse<CableLineStage>(groupKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.CableLine, groupKey, setting);
        }

        private void LoadExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Cable(형상) Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var list = ExcelLoader.LoadCable(dlg.FileName);
                    _cablesBySource[TabDataSource.Excel] = list;
                    _srcPanel.SetLoaded(TabDataSource.Excel, list.Count,
                        $"{Path.GetFileName(dlg.FileName)} · {DateTime.Now:HH:mm}");
                    if (_srcPanel.ActiveSource == TabDataSource.Excel)
                        ApplyActiveSourceData();
                }
                catch (Exception ex)
                {
                    _srcPanel.SetFailed(TabDataSource.Excel, "로드 실패");
                    MessageBox.Show($"Excel 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadCable(settings);
                _cablesBySource[TabDataSource.Oasis] = list;
                // EIT_Cable엔 프로젝트 컬럼이 없어(§9) 전체 로드 — 라벨에 프로젝트 미표기.
                _srcPanel.SetLoaded(TabDataSource.Oasis, list.Count,
                    $"{settings.Database} · {DateTime.Now:HH:mm}");
                if (_srcPanel.ActiveSource == TabDataSource.Oasis)
                    ApplyActiveSourceData();
            }
            catch (Exception ex)
            {
                _srcPanel.SetFailed(TabDataSource.Oasis, "로드 실패");
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 적용 기준 소스의 리스트로 화면을 전환한다. 매칭 결과·범위 판정은 소스별로
        /// 다르므로 초기화하고, 레벨 타겟 인덱스는 활성 태그 셋 기반이라 재빌드를 강제한다.
        /// 자동 재색칠은 안 함(§6) — 색이 이전 소스 기준이면 경고만.
        /// </summary>
        private void ApplyActiveSourceData()
        {
            _cables = _cablesBySource.TryGetValue(_srcPanel.ActiveSource, out var list)
                ? list : new List<CableLineData>();
            _matchedCableNos.Clear();
            _unmatchedCableNos.Clear();
            _scopeFilter.Reset();
            _scopeKeys = null;
            _scopePanel.ResetToFullModel();
            _needsIndexRebuild = true;
            _listFilter = null;   // 리스트 필터는 소스별 부분집합 — 소스 전환 시 해제
            _cableFilterText = null;
            if (_btnListFilter != null) _btnListFilter.Text = "Cable 찾기";
            _tabFilter.TabPages[0].Text = $"전체 ({_cables.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            FilterList();
            UpdateStats();
            // 색이 이전 소스 기준으로 남아 있으면 상태 표시기로 경고 (통계 라벨은 통계만 — P0-1)
            _applyState.MarkStale("데이터 변경");
        }

        private void ExportComparison()
        {
            if (!_cablesBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_cablesBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }

            var fields = new List<SourceComparer.Field<CableLineData>>
            {
                new SourceComparer.Field<CableLineData>("Pulling",
                    c => SourceComparer.FormatDate(c.StageDates.TryGetValue(CableLineStage.Pulling, out var d) ? d : null)),
                new SourceComparer.Field<CableLineData>("Pulled",
                    c => SourceComparer.FormatDate(c.StageDates.TryGetValue(CableLineStage.Pulled, out var d) ? d : null)),
                new SourceComparer.Field<CableLineData>("From Conn", c => SourceComparer.FormatDate(c.FromConnDate)),
                new SourceComparer.Field<CableLineData>("To Conn",   c => SourceComparer.FormatDate(c.ToConnDate)),
                new SourceComparer.Field<CableLineData>("Design Lth", c => c.DesignLth?.ToString("0.#") ?? ""),
                new SourceComparer.Field<CableLineData>("Pulled Lth", c => c.PulledLth?.ToString("0.#") ?? ""),
            };
            var lines = SourceComparer.BuildCsv("Cable No", excelList, oasisList, c => c.CableNo, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"CableLine_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "Excel↔OASIS 비교 출력", path);
        }

        private void ExportInputTemplate()
        {
            try
            {
                string path = InputTemplate.ExportCable();
                SaveNotifier.ShowSaved(this, "Template 출력", path,
                    "작성 후 Excel 형식(.xlsx)으로 저장해 Import 하세요.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"입력 양식 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            var cableNoSet = new HashSet<string>(_cables.Select(c => c.CableNo));
            _main.CableLineSearcher.BuildIndexForTags(doc, cableNoSet, NwdScope.Cable);
            _needsIndexRebuild = false;
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _cables.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_needsIndexRebuild || _main.CableLineSearcher.NeedsRebuild(doc))
                BuildIndex();

            // 색칠 전 isolate 숨김 해제 (hide는 §10 누적 리셋이 안 잡음).
            RestoreHidden(doc);

            // 하이라이트 우선 모드: 진척 신호(stage 날짜 또는 길이 완료)가 하나도 없으면 단색 하이라이트.
            _lastHighlightMode = !_cables.Any(c => c.HasProgressSignal);

            var activeSettings = new Dictionary<CableLineStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check != null && kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            OverrideResult result;
            _btnApply.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "색상 적용 중…";
            Application.DoEvents();
            try
            {
                result = _main.OverrideEngine.ApplyCableLines(doc, _cables, activeSettings, referenceDate,
                    _lastHighlightMode ? _highlightSetting : null);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
                _btnApply.Enabled = true;
            }

            _unmatchedCableNos = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedCableNos = new HashSet<string>(
                _cables.Select(c => c.CableNo).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            _applyState.SetApplied(
                (_srcPanel.ActiveSource == TabDataSource.Oasis ? "OASIS" : "Excel")
                + (_lastHighlightMode ? " · 하이라이트" : $" · 기준일 {referenceDate:MM-dd}"));
            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
            SubscribeSelection(doc);
        }

        // ----- 겹침 완화 (숨김 isolate) -----

        private void RestoreHidden(Document doc)
        {
            if (_cableHidden != null)
            {
                doc.Models.SetHidden(_cableHidden, false);
                _cableHidden = null;
            }
            _btnHideOthers.Text = "체크 단계 외 숨김";
            _btnIsolateSel.Text = "선택 케이블만 보기";
        }

        /// <summary>체크된 단계(기준일 기준)의 매칭 케이블만 남기고 나머지 매칭 케이블을 숨긴다(토글).</summary>
        private void BtnHideOthers_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_cableHidden != null) { RestoreHidden(doc); return; }
            if (_matchedCableNos.Count == 0) { MessageBox.Show("먼저 가시화 적용을 실행하세요."); return; }

            var checkedStages = new HashSet<CableLineStage>(
                _colorRows.Where(kv => kv.Value.check != null && kv.Value.check.Checked).Select(kv => kv.Key));
            var referenceDate = _dtpReference.Value;
            var byCable = _cables.ToDictionary(c => c.CableNo, StringComparer.OrdinalIgnoreCase);

            var toHide = new ModelItemCollection();
            foreach (var cableNo in _matchedCableNos)
            {
                if (!byCable.TryGetValue(cableNo, out var cable)) continue;
                if (checkedStages.Contains(cable.GetStageAtDate(referenceDate))) continue;
                var col = _main.OverrideEngine.GetCableLineItems(cableNo);
                if (col != null) foreach (ModelItem mi in col) toHide.Add(mi);
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다 (모든 매칭 케이블이 체크된 단계입니다)."); return; }

            doc.Models.SetHidden(toHide, true);
            _cableHidden = toHide;
            _btnHideOthers.Text = "전체 보기";
        }

        /// <summary>현재 선택(3D 또는 목록)한 케이블만 남기고 나머지 매칭 케이블을 숨긴다(토글).</summary>
        private void BtnIsolateSel_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_cableHidden != null) { RestoreHidden(doc); return; }
            if (_matchedCableNos.Count == 0) { MessageBox.Show("먼저 가시화 적용을 실행하세요."); return; }

            var keep = ResolveSelectedCableNos(doc);
            foreach (int i in _listView.SelectedIndices)
                if (i >= 0 && i < _viewRows.Count) keep.Add(_viewRows[i].CableNo);
            if (keep.Count == 0) { MessageBox.Show("3D 뷰나 목록에서 케이블을 선택하세요."); return; }

            var toHide = new ModelItemCollection();
            foreach (var cableNo in _matchedCableNos)
            {
                if (keep.Contains(cableNo)) continue;
                var col = _main.OverrideEngine.GetCableLineItems(cableNo);
                if (col != null) foreach (ModelItem mi in col) toHide.Add(mi);
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다."); return; }

            doc.Models.SetHidden(toHide, true);
            _cableHidden = toHide;
            _btnIsolateSel.Text = "전체 보기";
        }

        /// <summary>
        /// 텍스트로 붙여넣은 케이블 번호 목록으로 부분집합 필터(토글). 진척 데이터가 아니라 "보여줄
        /// 케이블 목록"만 — 리스트를 그 집합으로 좁히고, 가시화 적용 상태면 3D도 그 케이블만 남기고
        /// 나머지 매칭 케이블을 숨긴다(기존 isolate 숨김 메커니즘 공유 — 케이블은 투명 dim 대신
        /// 숨김 isolate: 2만 케이블에서 투명은 프레임레이트 붕괴, §13). 다시 누르면 해제.
        /// Spool '복수 Spool 찾기'와 동일한 TextListPrompt 텍스트창(공백/개행/콤마 구분).
        /// </summary>
        private void BtnListFilter_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();

            if (_listFilter != null)
            {
                _listFilter = null;
                _cableFilterText = null;
                _btnListFilter.Text = "Cable 찾기";
                if (doc != null && _cableHidden != null) RestoreHidden(doc);
                FilterList();
                UpdateStats();
                return;
            }

            if (_cables.Count == 0)
            {
                MessageBox.Show("먼저 케이블 데이터(Excel/OASIS)를 로드하세요. Cable 찾기는 로드된 케이블의 부분집합입니다.");
                return;
            }

            var text = TextListPrompt.Prompt(this, "Cable 찾기",
                "가시화할 케이블 번호를 붙여넣으세요 (공백·개행·콤마 어느 구분이든 인식).\n" +
                "리스트에 있는 케이블만 리스트/3D에 남고 나머지는 숨겨집니다.",
                _cableFilterText);
            if (text == null) return;   // 취소

            var tokens = TextListPrompt.Parse(text);
            if (tokens.Count == 0)
            {
                MessageBox.Show("케이블 번호를 하나 이상 입력하세요.");
                return;
            }

            var filter = new HashSet<string>(
                tokens.Select(CableLineData.NormalizeCableNo), StringComparer.OrdinalIgnoreCase);
            int hit = _cables.Count(c => filter.Contains(CableLineData.NormalizeCableNo(c.CableNo)));

            _listFilter = filter;
            _cableFilterText = text;
            _btnListFilter.Text = $"Cable 찾기 해제 ({hit:N0}건)";
            FilterList();
            UpdateStats();

            // 3D isolate: 가시화 적용된 매칭 케이블 중 리스트 밖은 숨긴다.
            if (doc != null && _matchedCableNos.Count > 0)
            {
                if (_cableHidden != null) RestoreHidden(doc);
                var toHide = new ModelItemCollection();
                foreach (var cableNo in _matchedCableNos)
                {
                    if (filter.Contains(CableLineData.NormalizeCableNo(cableNo))) continue;
                    var col = _main.OverrideEngine.GetCableLineItems(cableNo);
                    if (col != null) foreach (ModelItem mi in col) toHide.Add(mi);
                }
                if (toHide.Count > 0)
                {
                    doc.Models.SetHidden(toHide, true);
                    _cableHidden = toHide;
                }
            }

            MessageBox.Show($"입력 {tokens.Count:N0}건 중 로드 데이터와 일치 {hit:N0}건.\n" +
                (_matchedCableNos.Count > 0
                    ? "3D에는 리스트 케이블만 남기고 나머지 매칭 케이블을 숨겼습니다 (버튼 재클릭으로 해제)."
                    : "가시화 적용 전이라 리스트만 필터됐습니다 — [가시화 적용] 후 다시 필터하면 3D에도 반영됩니다."));
        }

        private static bool CableMatchesKeyword(CableLineData c, string keywordUpper)
        {
            // SearchKey = 로드 시 1회 대문자화한 캐시 (audit P0-3 — 검색마다 3필드 ToUpper 방지)
            return c.SearchKey.Contains(keywordUpper);
        }

        // ----- 현황 집계 범위 (Clipping = clash) -----

        private Dictionary<string, List<ModelItem>> BuildScopeItemsByKey()
        {
            var found = _main.CableLineSearcher.FindBySpoolIds(
                _matchedCableNos.Select(CableLineData.NormalizeCableNo).Distinct(StringComparer.OrdinalIgnoreCase));
            var itemsByKey = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _matchedCableNos)
                itemsByKey[id] = found.TryGetValue(CableLineData.NormalizeCableNo(id), out var items)
                    ? items : new List<ModelItem>();
            return itemsByKey;
        }

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
                if (doc == null || _matchedCableNos.Count == 0)
                {
                    MessageBox.Show("먼저 가시화 적용을 실행하세요. 집계 범위는 매칭된 케이블에 적용됩니다.");
                    return;
                }
                if (_needsIndexRebuild || _main.CableLineSearcher.NeedsRebuild(doc))
                {
                    MessageBox.Show("모델 또는 데이터가 변경되었습니다. 가시화 적용을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }
                if (scope == MatchScope.ClippingVolume) { _clash.EnsureFresh(doc); _clash.ResetBatchCounters(); }

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey(), _volumeJudge);
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

        private void ReapplyCurrentScope(Document doc)
        {
            var scope = _scopePanel.CurrentScope;
            if (scope == MatchScope.FullModel) { _scopeKeys = null; return; }
            if (scope == MatchScope.ClippingVolume) { _clash.EnsureFresh(doc); _clash.ResetBatchCounters(); }
            _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey(), _volumeJudge);
        }

        // ----- Buttons -----

        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreHidden(doc);
            _main.OverrideEngine.ResetCableLineModule(doc);
            _lblStats.Text = "이 탭 가시화 해제 완료 (Cable 형상 색만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreHidden(doc);
            _main.OverrideEngine.Reset(doc);
            _scopeFilter.Reset();
            _scopeKeys = null;
            _scopePanel.ResetToFullModel();
            _lblStats.Text = "전체 가시화 해제 완료";
            _lblUnmatched.Text = "";
            _applyState.SetCleared();
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"Cable_{DateTime.Now:yyyyMMdd_HHmm}";
            try { _main.ExportSvc.SaveViewpoint(doc, name); MessageBox.Show($"Viewpoint '{name}' 저장 완료"); }
            catch (Exception ex) { MessageBox.Show($"Viewpoint 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnNwd_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.ExportSvc.ExportNwdWithDialog(doc);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_cables.Count == 0) { MessageBox.Show("Excel을 먼저 로드하세요."); return; }
            var referenceDate = _dtpReference.Value;
            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add($"인덱스 스코프,\"{_main.CableLineSearcher.LastScopeNote ?? "-"}\"");
            if (_scopePanel.CurrentScope == MatchScope.ClippingVolume)
            {
                // 구 clash 전용 CSV의 진단 대체(L5): 활성평면 수는 범위 진단에 포함 —
                // 1~2개면 볼륨이 반쪽 공간으로 퇴화(회전 박스 → COM fallback)라 오탐 1순위.
                // pre-cull이 안 먹으면(사전배제 소수) 첫 판정이 COM 추출로 느려진 것.
                lines.Add($"범위 진단,\"{_scopeFilter.Diagnostics}\"");
                lines.Add($"clash 진단(bbox 사전배제/추출/세그AABB배제),{_clash.LastPreCulled}/{_clash.LastExtracted}/{_clash.LastCulled}");
            }
            lines.Add("Cable No,단계,From,To,Design,Pulled,%,Matched");
            foreach (var c in _cables)
            {
                if (!InScope(c.CableNo)) continue;
                string stageLabel = _lastHighlightMode ? "하이라이트" : CableLineStageInfo.Labels[c.GetStageAtDate(referenceDate)];
                bool matched = _matchedCableNos.Count == 0 || _matchedCableNos.Contains(c.CableNo);
                string pct = c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "";
                lines.Add($"\"{c.CableNo}\",\"{stageLabel}\",\"{FromText(c)}\",\"{ToText(c)}\"," +
                          $"{(c.DesignLth?.ToString("0.##") ?? "")},{(c.PulledLth?.ToString("0.##") ?? "")},\"{pct}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Cable_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, string.Join("\r\n", lines), new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "매칭 Status 엑셀 출력", path);
        }

        // ----- List / selection -----

        /// <summary>Overview 탭 상태 노출 — 인메모리 조회만 (IOverviewSource).</summary>
        public OverviewStatus GetOverviewStatus()
        {
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            string src = _srcPanel.ActiveSource == TabDataSource.Oasis ? "OASIS" : "Excel";
            return new OverviewStatus
            {
                DataLoaded = _cables.Count > 0,
                DataText = _cables.Count > 0 ? $"{src} {_cables.Count:N0}건" : "미로드",
                IndexText = _main.CableLineSearcher.IsIndexBuilt
                    ? _main.CableLineSearcher.IndexedCount.ToString("N0") : "-",
                ApplyStateText = _applyState.Text,
                ApplyStale = _applyState.IsStale,
                MatchedText = hasApplied ? _matchedCableNos.Count.ToString("N0") : "-",
                UnmatchedText = hasApplied ? _unmatchedCableNos.Count.ToString("N0") : "-",
                UnmatchedCount = hasApplied ? _unmatchedCableNos.Count : 0,
                ScopeNote = _main.CableLineSearcher.LastScopeNote ?? "-",
                ScopeFellBack = _main.CableLineSearcher.LastScopeFellBack,
            };
        }

        private bool InScope(string id) =>
            _scopeKeys == null || !_matchedCableNos.Contains(id) || _scopeKeys.Contains(id);

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null ? _matchedCableNos.Count : _matchedCableNos.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _cables.Count : _cables.Count(c => InScope(c.CableNo));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedCableNos.Count})";
        }

        private void FilterList()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _cables.AsEnumerable();
            if (tabIndex == 1 && _matchedCableNos.Count > 0)
                filtered = filtered.Where(c => _matchedCableNos.Contains(c.CableNo));
            else if (tabIndex == 2 && _unmatchedCableNos.Count > 0)
                filtered = filtered.Where(c => _unmatchedCableNos.Contains(c.CableNo));

            if (_listFilter != null)
                filtered = filtered.Where(c => _listFilter.Contains(CableLineData.NormalizeCableNo(c.CableNo)));
            if (_scopeKeys != null)
                filtered = filtered.Where(c => InScope(c.CableNo));
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(c => CableMatchesKeyword(c, keyword));

            var rows = filtered.ToList();
            if (_sortColumn > 0)
            {
                var key = SortKeySelector(_sortColumn);
                rows = (_sortAscending
                    ? rows.OrderBy(key, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            // 가상 모드: 백킹 리스트 교체 + 크기 통지만 — ListViewItem은 만들지 않는다.
            _viewRows = rows;
            _listView.SelectedIndices.Clear();
            _listView.VirtualListSize = rows.Count;
            _listView.Invalidate();
            PerfLog.Record("리스트 갱신(Cable)", sw.ElapsedMilliseconds, rows: rows.Count);
        }

        /// <summary>가상 리스트의 한 행 생성 — RetrieveVirtualItem이 보이는 행에 대해서만 호출한다.</summary>
        private ListViewItem BuildRow(CableLineData c, int index)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            var stage = c.GetStageAtDate(referenceDate);
            string stageLabel = _lastHighlightMode && hasApplied ? "하이라이트" : CableLineStageInfo.Labels[stage];
            string matchLabel = !hasApplied ? "-" : (_matchedCableNos.Contains(c.CableNo) ? "O" : "X");
            string pct = c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "-";

            var item = new ListViewItem((index + 1).ToString());
            item.UseItemStyleForSubItems = false;
            item.SubItems.Add(c.CableNo ?? "");
            var stageSub = item.SubItems.Add(stageLabel);
            if (!_lastHighlightMode && _colorSettings.TryGetValue(stage, out var setting))
                stageSub.ForeColor = setting.DisplayColor;
            item.SubItems.Add(FromText(c));
            item.SubItems.Add(ToText(c));
            item.SubItems.Add(c.DesignLth?.ToString("0.##") ?? "-");
            item.SubItems.Add(c.PulledLth?.ToString("0.##") ?? "-");
            item.SubItems.Add(pct);
            var matchSub = item.SubItems.Add(matchLabel);
            if (matchLabel == "X") matchSub.ForeColor = Color.Red;
            item.Tag = c;
            return item;
        }

        /// <summary>정렬 키 추출 (열 번호 → 셀 텍스트와 동일 값) — 백킹 리스트 정렬용.</summary>
        private Func<CableLineData, string> SortKeySelector(int column)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            switch (column)
            {
                case 1: return c => c.CableNo ?? "";
                case 2: return c => _lastHighlightMode && hasApplied
                    ? "하이라이트" : CableLineStageInfo.Labels[c.GetStageAtDate(referenceDate)];
                case 3: return c => FromText(c);
                case 4: return c => ToText(c);
                case 5: return c => c.DesignLth?.ToString("0.##") ?? "-";
                case 6: return c => c.PulledLth?.ToString("0.##") ?? "-";
                case 7: return c => c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "-";
                case 8: return c => !hasApplied ? "-" : (_matchedCableNos.Contains(c.CableNo) ? "O" : "X");
                default: return c => "";
            }
        }

        private static string FromText(CableLineData c) =>
            string.IsNullOrEmpty(c.FromModule) ? (c.FromEquip ?? "") : $"{c.FromModule}/{c.FromEquip}";
        private static string ToText(CableLineData c) =>
            string.IsNullOrEmpty(c.ToModule) ? (c.ToEquip ?? "") : $"{c.ToModule}/{c.ToEquip}";

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;

            var statBasis = _cables.Where(c => InScope(c.CableNo));
            if (hasApplied) statBasis = statBasis.Where(c => _matchedCableNos.Contains(c.CableNo));

            var parts = new List<string>();
            if (_lastHighlightMode && hasApplied)
            {
                parts.Add($"하이라이트 {statBasis.Count()}");
            }
            else
            {
                var counts = statBasis.GroupBy(c => c.GetStageAtDate(referenceDate)).ToDictionary(g => g.Key, g => g.Count());
                var allStages = new[] { CableLineStage.NotStarted }.Concat(CableLineStageInfo.OrderedStages).Reverse();
                foreach (var stage in allStages)
                    if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                        parts.Add($"{CableLineStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null ? _matchedCableNos.Count : _matchedCableNos.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope}";
                if (_scopeKeys != null) line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준)";
                if (result != null && _lastHighlightMode) line2 += "  · 하이라이트 모드(stage 날짜 없음)";
            }
            _lblStats.Text = string.Join("  ", parts) + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            _lblUnmatched.Text = hasApplied ? $"미매칭 {_unmatchedCableNos.Count}건" : "";
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressSelectionSync || _listView.SelectedIndices.Count == 0) return;
            var doc = _main.GetDocument();
            if (doc == null) return;

            var combined = new ModelItemCollection();
            foreach (int i in _listView.SelectedIndices)
            {
                if (i < 0 || i >= _viewRows.Count) continue;
                var col = _main.OverrideEngine.GetCableLineItems(_viewRows[i].CableNo);
                if (col != null) foreach (ModelItem mi in col) combined.Add(mi);
            }
            if (combined.Count == 0) return;
            _suppressSelectionSync = true;
            try { doc.CurrentSelection.CopyFrom(combined); doc.ActiveView.FocusOnCurrentSelection(); }
            finally { _suppressSelectionSync = false; }
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
            if (e.Column == 0) return;
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            // 가상 모드에선 ListView.Sort()가 지원되지 않는다 — 백킹 리스트를 정렬해 다시 표시.
            FilterList();
        }

        /// <summary>3D 선택 → cable-no 해석: 각 선택 아이템 + 조상 DisplayName을 정규화해 매칭 케이블과 대조.</summary>
        private HashSet<string> ResolveSelectedCableNos(Document doc)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return result;

            var keyToCable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cableNo in _matchedCableNos)
                keyToCable[CableLineData.NormalizeCableNo(cableNo).ToUpperInvariant()] = cableNo;

            foreach (ModelItem selected in doc.CurrentSelection.SelectedItems)
            {
                for (var item = selected; item != null; item = item.Parent)
                {
                    string key = CableLineData.NormalizeCableNo(item.DisplayName ?? "").ToUpperInvariant();
                    if (key.Length > 0 && keyToCable.TryGetValue(key, out var cableNo)) { result.Add(cableNo); break; }
                }
            }
            return result;
        }

        // ----- 3D → list selection sync -----

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
            var cableNos = ResolveSelectedCableNos(doc);
            if (cableNos.Count == 0) return;
            string first = cableNos.First();

            _suppressSelectionSync = true;
            try
            {
                int idx = _viewRows.FindIndex(
                    c => string.Equals(c.CableNo, first, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _listView.SelectedIndices.Clear();
                    _listView.SelectedIndices.Add(idx);
                    _listView.EnsureVisible(idx);
                }
            }
            finally { _suppressSelectionSync = false; }
        }

        private Dictionary<CableLineStage, ColorSetting> CloneDefaults(Dictionary<CableLineStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<CableLineStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
