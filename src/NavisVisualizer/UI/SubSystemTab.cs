using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// Sub-system 탭: OASIS의 Sub-system 마스터([Navis].[System_Summary] —
    /// Walkdown/P-MCC/MCC/PCC 실적일 + ITR/Punch 수치)와 요소 데이터 4공종
    /// (Equipment Mech_EQ / Piping Hydrotest PKG / EIT EQ / Cable —
    /// 각 테이블의 SUB-SYSTEM 컬럼 기준. EIT Tray는 매핑 컬럼이 없어 제외)을 묶어,
    /// 선택한 Sub-system들의 요소를 3D에 가시화하고 현황 리포트를 출력한다.
    ///
    /// - 데이터: OASIS 전용. 마스터 미구성 시 요소 파생 목록으로 자동 fallback.
    ///   EIT 계열은 공종별 try/catch — 컬럼 미구성이어도 나머지는 정상 로드(라벨에 사유)
    /// - 매칭: 공종마다 자기 nwd 하나만 레벨 타겟(general walk 없음, 하드 스코프):
    ///   Equipment=MEQ / Piping=HYDROPKG / EIT EQ=EIT / Cable=CABLE. 4개 사유 searcher,
    ///   SearcherFor로 라우팅(엔진 ApplySubSystem에 리졸버 주입).
    /// - 가시화 2모드: Sub-system 단계별(Walkdown→PCC 6색, 마스터 필요) /
    ///   요소 진행상태별(미착수·진행중·완료)
    /// - 선택 UI: 좌측 검색+상태 테이블(~400개) ↔ [▶ ◀ ▶▶ ◀◀] ↔ 우측 선택 누적
    ///   테이블 + 하단 개수 라벨. 다중 선택 후 화살표로 이동
    /// </summary>
    public class SubSystemTab : UserControl
    {
        private enum ApplyMode { Stage, Progress }

        private readonly MainDockablePanel _main;

        private List<SubSystemElement> _elements = new List<SubSystemElement>();
        private Dictionary<string, List<SubSystemElement>> _bySubSystem
            = new Dictionary<string, List<SubSystemElement>>(StringComparer.OrdinalIgnoreCase);
        // 공종별 매칭 인덱스 — 각자 자기 nwd 하나만 레벨 타겟(general walk 없음). 로드된
        // 요소 태그 셋에 종속이라 다른 탭과 공유 불가(사유 인스턴스). 하드 스코프 = 그 nwd에서만.
        private readonly ModelItemSearcher _eqSearcher = new ModelItemSearcher();       // Mech_EQ  → MEQ
        private readonly ModelItemSearcher _pipingSearcher = new ModelItemSearcher();   // Hydro PKG → HYDROPKG
        private readonly ModelItemSearcher _eitEqSearcher = new ModelItemSearcher();    // EIT_EQ   → EIT
        private readonly ModelItemSearcher _cableSearcher = new ModelItemSearcher();    // EIT_Cable → CABLE

        // 인덱스는 로드 셋·모델에 종속 → 데이터 재로드/모델 변경 시 재빌드. _indexSig로 모델 변경 감지.
        private bool _needsIndexRebuild;
        private bool _indexBuilt;
        private string _indexSig;

        /// <summary>공종 → 그 공종의 nwd 스코프 매칭 인덱스 (엔진/FindItemsFor 공용 리졸버).</summary>
        private ModelItemSearcher SearcherFor(SubSystemDiscipline d)
        {
            switch (d)
            {
                case SubSystemDiscipline.Piping:       return _pipingSearcher;
                case SubSystemDiscipline.EitEquipment: return _eitEqSearcher;
                case SubSystemDiscipline.Cable:        return _cableSearcher;
                default:                               return _eqSearcher; // Equipment
            }
        }

        private static string DocSig(Autodesk.Navisworks.Api.Document doc)
        {
            try { return $"{doc?.FileName}|{doc?.Models.Count}"; } catch { return "?"; }
        }

        /// <summary>인덱스가 현재 데이터·모델 기준으로 최신인가 (아니면 [적용] 시 재빌드).</summary>
        private bool IndexStale(Autodesk.Navisworks.Api.Document doc) =>
            !_indexBuilt || _needsIndexRebuild || _indexSig != DocSig(doc);

        /// <summary>요소가 있는 공종들의 인덱스 스코프 노트를 합쳐 리포트에 표기 (fallback 여부 확인).</summary>
        private string ScopeNotes()
        {
            var parts = new List<string>();
            if (_eqSearcher.IsIndexBuilt)     parts.Add($"Eq[{_eqSearcher.LastScopeNote}]");
            if (_pipingSearcher.IsIndexBuilt) parts.Add($"Piping[{_pipingSearcher.LastScopeNote}]");
            if (_eitEqSearcher.IsIndexBuilt)  parts.Add($"EIT EQ[{_eitEqSearcher.LastScopeNote}]");
            if (_cableSearcher.IsIndexBuilt)  parts.Add($"Cable[{_cableSearcher.LastScopeNote}]");
            return parts.Count > 0 ? string.Join(" · ", parts) : "-";
        }
        /// <summary>null = 마스터 미구성(요소 파생 fallback).</summary>
        private Dictionary<string, SubSystemMasterData> _master;
        private List<string> _subSystemNames = new List<string>();
        private string _loadLabel = "";

        // 선택 상태 — _selected가 진실의 원천, _selectionOrder는 우측 테이블 누적 순서
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _selectionOrder = new List<string>();

        // 마지막 적용 결과 (적용된 Sub-system 스냅샷 기준으로만 매칭 O/X가 유효)
        private HashSet<string> _matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _unmatchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _appliedSubSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _appliedOnce;
        private ApplyMode? _appliedMode;

        private Dictionary<SubSystemStage, ColorSetting> _stageSettings;
        private Dictionary<ProgressStatus, ColorSetting> _progressSettings;

        private Label _dotOasis;
        private Label _lblOasis;
        // 로드 요약이 라벨 폭을 넘으면 잘리므로 전체 문구를 툴팁으로 노출
        private readonly ToolTip _loadTip = new ToolTip { AutoPopDelay = 15000 };
        private RadioButton _rdoByStage;
        private RadioButton _rdoByProgress;
        private DateTimePicker _dtpReference;
        private Panel _stagePanel;
        private Panel _progressPanel;
        private TextBox _txtFilter;
        private Button _btnDelayed;
        private Button _btnDetail;
        private ListView _lvAll;
        private ListView _lvSelected;
        private Label _lblSelCount;
        private ProgressBar _progressBar;
        private Label _lblStats;
        private ApplyStatePanel _applyState;   // 3D 적용 상태 표시 (선택/모드/기준일↔3D 어긋남 경고 전담)

        // 그리드 밖에서는 단계별 체크 상태만 필요 (색/투명도는 빌더 클로저가 직접 갱신)
        private Dictionary<SubSystemStage, CheckBox> _stageChecks = new Dictionary<SubSystemStage, CheckBox>();
        private Dictionary<ProgressStatus, CheckBox> _progressChecks = new Dictionary<ProgressStatus, CheckBox>();

        private static readonly Color DotLoaded = Color.FromArgb(0, 160, 60);
        private static readonly Color DotEmpty = Color.FromArgb(170, 170, 170);
        private static readonly Color DotFailed = Color.FromArgb(200, 40, 40);
        /// <summary>좌측 목록에서 이미 우측에 담긴 행의 배경.</summary>
        private static readonly Color PickedBack = Color.FromArgb(223, 240, 230);

        public SubSystemTab(MainDockablePanel main)
        {
            _main = main;
            _stageSettings = CloneDefaults(ColorSetting.SubSystemStageDefaults);
            _progressSettings = CloneDefaults(ColorSetting.ProgressDefaults);
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

            // 색상 그리드·선택/모드/기준일 핸들러가 참조하므로 먼저 생성 (버튼 연결은 버튼 행에서).
            _applyState = new ApplyStatePanel();

            // ----- OASIS 로드 행 -----
            var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            var btnOasis = new Button { Text = "OASIS 로드", Width = 100, Height = 24 };
            btnOasis.Click += (s, e) => LoadOasis();
            _dotOasis = new Label { Text = "●", AutoSize = true, ForeColor = DotEmpty, Padding = new Padding(4, 5, 0, 0) };
            _lblOasis = new Label { Text = "(미로드)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(0, 5, 0, 0) };
            loadPanel.Controls.Add(btnOasis);
            loadPanel.Controls.Add(_dotOasis);
            loadPanel.Controls.Add(_lblOasis);

            // ----- 시각화 모드 + 기준일 -----
            var modeGroup = new GroupBox { Text = "시각화 모드", Dock = DockStyle.Fill, Height = 50 };
            var modeFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(4, 0, 4, 0),
            };
            // 단계 모드는 마스터가 있어야 의미가 있음 — 로드 성공 시 활성화
            _rdoByStage = new RadioButton { Text = "Sub-system 단계별", AutoSize = true, Checked = true, Enabled = false };
            _rdoByProgress = new RadioButton { Text = "요소 진행상태별", AutoSize = true, Margin = new Padding(8, 3, 0, 0) };
            _rdoByStage.CheckedChanged += (s, e) => ModeChanged();
            modeFlow.Controls.Add(_rdoByStage);
            modeFlow.Controls.Add(_rdoByProgress);
            modeFlow.Controls.Add(new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 105,
            };
            _dtpReference.ValueChanged += (s, e) =>
            {
                // 단계/스와치 컬럼이 기준일에 의존 → 목록·통계 재표시
                if (_subSystemNames.Count > 0)
                { RefreshLeftList(); RefreshRightList(); UpdateStats(); _applyState.MarkStale("기준일 변경"); }
            };
            modeFlow.Controls.Add(_dtpReference);
            modeGroup.Controls.Add(modeFlow);

            _stagePanel = BuildColorGrid(
                SubSystemStageInfo.GridOrder,
                SubSystemStageInfo.Labels, _stageSettings, _stageChecks, ApplyMode.Stage);
            _progressPanel = BuildColorGrid(
                ProgressStatusInfo.Ordered,
                ProgressStatusInfo.Labels, _progressSettings, _progressChecks, ApplyMode.Progress);
            _progressPanel.Visible = false;

            var selGroup = BuildSelectionGroup();

            // ----- 버튼 행 -----
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            var btnApply = new Button { Text = "가시화 적용", Width = 90 };
            var btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            var btnReset = new Button { Text = "전체 가시화 해제", Width = 130 };
            var btnReport = new Button { Text = "현황 리포트 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            var btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            var btnNwd = new Button { Text = "NWD Export", Width = 110 };
            btnApply.Click += BtnApply_Click;
            btnResetModule.Click += BtnResetModule_Click;
            btnReset.Click += BtnReset_Click;
            btnReport.Click += BtnReport_Click;
            btnViewpoint.Click += BtnViewpoint_Click;
            btnNwd.Click += BtnNwd_Click;
            _applyState.AttachApplyButton(btnApply);
            btnPanel.Controls.AddRange(new Control[]
                { btnApply, btnResetModule, btnReset, btnReport, btnViewpoint, btnNwd, _applyState });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 78 };

            layout.Controls.Add(loadPanel);
            layout.Controls.Add(modeGroup);
            layout.Controls.Add(new Label { Text = "단계 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(_stagePanel);
            layout.Controls.Add(_progressPanel);
            // 색상 편집(▼·투명도)은 기본 접힘 — 체크박스·스와치만 상시 노출 (UX audit P1)
            layout.Controls.Add(ColorEditCollapse.BuildToggleRow(_stagePanel, _progressPanel));
            layout.Controls.Add(selGroup);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        /// <summary>공통 색상 그리드 (2열 × N행) — 단계/진행상태 두 모드가 공유하는 빌더.</summary>
        private Panel BuildColorGrid<T>(T[] keys, Dictionary<T, string> labels,
            Dictionary<T, ColorSetting> settings,
            Dictionary<T, CheckBox> checks,
            ApplyMode mode)
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var setting = settings[key];

                var chk = new CheckBox { Text = labels[key], Checked = true, AutoSize = true };
                chk.CheckedChanged += (s, e) => _applyState.MarkStale("단계 선택 변경");
                var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var ck = key;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = settings[ck].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            settings[ck].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(mode, ck.ToString(), settings[ck]);
                            if (mode == ApplyMode.Stage) RefreshRightList(); // 우측 스와치 동기화
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        settings[ck].Transparency = pct / 100.0;
                        IncrementalUpdate(mode, ck.ToString(), settings[ck]);
                    }
                };

                checks[key] = chk;

                int row = i / 2;
                int colOffset = (i % 2) * 4;
                grid.Controls.Add(chk, colOffset + 0, row);
                grid.Controls.Add(colorBox, colOffset + 1, row);
                grid.Controls.Add(colorBtn, colOffset + 2, row);
                grid.Controls.Add(transparencyBox, colOffset + 3, row);
            }

            panel.Controls.Add(grid);
            return panel;
        }

        private GroupBox BuildSelectionGroup()
        {
            var group = new GroupBox { Text = "Sub-system 선택", Dock = DockStyle.Fill, Height = 366 };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            // 좌측 상단: 검색 (코드 + 설명 매칭) + MCC 지연 일괄 선택
            var leftTop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            leftTop.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _txtFilter = new TextBox { Width = 120, Margin = new Padding(0, 3, 3, 0) };
            _txtFilter.TextChanged += (s, e) => RefreshLeftList();
            leftTop.Controls.Add(_txtFilter);
            _btnDelayed = new Button
            {
                Text = "MCC 지연 담기",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 0, 6, 0),
                Margin = new Padding(0, 2, 0, 0),
                BackColor = Color.FromArgb(250, 224, 224),
                Enabled = false, // 마스터 로드 후에만 활성 (지연 판정에 계획일 필요)
            };
            _btnDelayed.Click += (s, e) => AddDelayedToSelection();
            leftTop.Controls.Add(_btnDelayed);

            // 좌측 상태 목록(_lvAll) 선택 행(없으면 전체)을 클립보드로 복사 — Ctrl+C 대체 버튼.
            var btnCopyAll = new Button
            {
                Text = "클립보드 복사",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 0, 6, 0),
                Margin = new Padding(0, 2, 0, 0),
            };
            btnCopyAll.Click += (s, e) => ShowCopied(ListViewClipboard.CopySelectedOrAll(_lvAll));
            leftTop.Controls.Add(btnCopyAll);

            var rightTopLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택됨 (더블클릭 = 제거)",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
            };

            // 가운데 화살표 열: ▶ 추가, ◀ 제거, ▶▶ 표시 전체 추가, ◀◀ 전체 해제
            var arrows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 46, 0, 0),
                Margin = Padding.Empty,
            };
            var btnAdd = MakeArrow("▶", "선택 항목 추가");
            var btnRemove = MakeArrow("◀", "선택 항목 제거");
            var btnAddAll = MakeArrow("▶▶", "표시(필터) 전체 추가");
            var btnRemoveAll = MakeArrow("◀◀", "전체 해제");
            btnAdd.Click += (s, e) => AddHighlightedLeft();
            btnRemove.Click += (s, e) => RemoveHighlightedRight();
            btnAddAll.Click += (s, e) => AddAllShown();
            btnRemoveAll.Click += (s, e) => ClearSelection();
            arrows.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnAddAll, btnRemoveAll });

            _lvAll = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvAll.Columns.Add("Sub-system", 92);
            _lvAll.Columns.Add("Description", 98);
            _lvAll.Columns.Add("단계", 52);
            _lvAll.Columns.Add("MCC계획", 66);
            _lvAll.Columns.Add("A-ITR", 50);
            _lvAll.Columns.Add("B-ITR", 50);
            _lvAll.Columns.Add("C-ITR", 50);
            _lvAll.Columns.Add("P.A", 46);
            _lvAll.Columns.Add("P.B", 46);
            _lvAll.Columns.Add("요소", 40);
            _lvAll.DoubleClick += (s, e) => AddHighlightedLeft();

            _lvSelected = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvSelected.Columns.Add("", 20);
            _lvSelected.Columns.Add("Sub-system", 86);
            _lvSelected.Columns.Add("단계", 50);
            _lvSelected.Columns.Add("MCC계획", 62);
            _lvSelected.Columns.Add("요소", 36);
            _lvSelected.Columns.Add("매칭", 42);
            _lvSelected.SelectedIndexChanged += LvSelected_SelectedIndexChanged;
            _lvSelected.DoubleClick += (s, e) => RemoveHighlightedRight();

            // ListView는 기본적으로 Ctrl+C를 지원하지 않으므로 양쪽 리스트 모두 공용 헬퍼로 배선.
            ListViewClipboard.EnableCtrlC(_lvAll, ShowCopied);
            ListViewClipboard.EnableCtrlC(_lvSelected, ShowCopied);

            _lblSelCount = new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택된 Sub-system: 0개",
                TextAlign = ContentAlignment.MiddleRight,
            };

            // 선택 박스 하단: 선택된 sub-system의 공종·요소별 상세 현황을 별도 창으로
            _btnDetail = new Button
            {
                Dock = DockStyle.Fill,
                Text = "선택 Sub-system 상세 현황 보기…",
                Margin = new Padding(0, 1, 0, 0),
            };
            _btnDetail.Click += (s, e) => ShowDetailWindow();

            grid.Controls.Add(leftTop, 0, 0);
            grid.Controls.Add(rightTopLbl, 2, 0);
            grid.Controls.Add(_lvAll, 0, 1);
            grid.Controls.Add(arrows, 1, 1);
            grid.Controls.Add(_lvSelected, 2, 1);
            grid.Controls.Add(_lblSelCount, 0, 2);
            grid.SetColumnSpan(_lblSelCount, 3);
            grid.Controls.Add(_btnDetail, 2, 3);

            group.Controls.Add(grid);
            return group;
        }

        private void ShowCopied(int n)
        {
            if (n > 0) _lblStats.Text = $"클립보드에 {n}행 복사됨";
        }

        private static Button MakeArrow(string text, string tip)
        {
            return new Button
            {
                Text = text,
                Width = 30,
                Height = 26,
                Margin = new Padding(1, 2, 1, 2),
                AccessibleDescription = tip,
            };
        }

        // ----- 데이터 로드 -----

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadSubSystemElements(settings, out int noSubSystem,
                    out List<string> disciplineNotes);

                // 마스터는 별도 시도 — 테이블 미구성 환경에서도 요소 기준으로 동작해야 함
                Dictionary<string, SubSystemMasterData> master = null;
                string masterNote;
                try
                {
                    master = new Dictionary<string, SubSystemMasterData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in SqlLoader.LoadSubSystemMaster(settings))
                        master[m.SubSystemNo] = m;
                    masterNote = $" · 마스터 {master.Count}개";
                }
                catch
                {
                    master = null;
                    masterNote = " · 마스터 미구성(요소 기준)";
                }

                _elements = list;
                _master = master;
                RebuildNames(out int outsideMaster);

                _selected.RemoveWhere(name => !_subSystemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                _selectionOrder.RemoveAll(name => !_selected.Contains(name));

                _matchedIds.Clear();
                _unmatchedIds.Clear();
                _appliedSubSystems.Clear();
                _appliedOnce = false;
                _appliedMode = null;

                // 단계 모드·MCC 지연 담기는 마스터가 있을 때만
                _rdoByStage.Enabled = _master != null;
                _btnDelayed.Enabled = _master != null;
                if (_master == null && _rdoByStage.Checked)
                    _rdoByProgress.Checked = true;

                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _loadLabel = $"{settings.Database}/{prj} · {DateTime.Now:HH:mm}";
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.ForeColor = Color.Black;
                _lblOasis.Text = $"요소 {list.Count:N0}건 · Sub-system {_subSystemNames.Count}개{masterNote}"
                    + (outsideMaster > 0 ? $" · 마스터 외 {outsideMaster}개" : "")
                    + (noSubSystem > 0 ? $" · 미지정 {noSubSystem}건 제외" : "")
                    + (disciplineNotes.Count > 0 ? " · " + string.Join(" · ", disciplineNotes) : "");
                // 문구가 길어 창 폭에 잘릴 수 있음 — 전체 문구는 마우스 오버 툴팁으로 제공
                _loadTip.SetToolTip(_lblOasis, _lblOasis.Text);
                _needsIndexRebuild = true;   // Cable 레벨 타겟 인덱스는 로드 셋 기반 — 재빌드 강제

                RefreshLeftList();
                RefreshRightList();
                UpdateSelCount();
                UpdateStats();
                // 3D 색이 남아 있으면 이전 로드 기준 — 상태 표시기로 경고 (P0-1)
                _applyState.MarkStale("데이터 재로드");
            }
            catch (Exception ex)
            {
                _dotOasis.ForeColor = DotFailed;
                _lblOasis.ForeColor = DotFailed;
                _lblOasis.Text = "로드 실패";
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 좌측 목록의 이름 축을 만든다. 마스터가 있으면 마스터가 기준(요소 0건 포함)이고
        /// 마스터에 없는 요소 sub-system도 진단 목적으로 함께 노출한다("마스터 외").
        /// 마스터 미구성이면 요소 파생 목록만 사용한다.
        /// </summary>
        private void RebuildNames(out int outsideMaster)
        {
            _bySubSystem = _elements
                .GroupBy(el => el.SubSystem, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var names = new HashSet<string>(_bySubSystem.Keys, StringComparer.OrdinalIgnoreCase);
            outsideMaster = 0;
            if (_master != null)
            {
                outsideMaster = names.Count(n => !_master.ContainsKey(n));
                names.UnionWith(_master.Keys);
            }
            _subSystemNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private SubSystemMasterData GetMaster(string name) =>
            _master != null && _master.TryGetValue(name, out var m) ? m : null;

        private int ElementCount(string name) =>
            _bySubSystem.TryGetValue(name, out var els) ? els.Count : 0;

        /// <summary>마스터의 실적 단계(기준일). 마스터 없으면 null. 지연은 색으로 반영하지
        /// 않는다 — 지연이어도 달성 단계(Walkdown 등)를 그대로 칠하고, 지연은 MCC계획 컬럼과
        /// 'MCC 지연 담기'로만 다룬다.</summary>
        private SubSystemStage? StageOf(SubSystemMasterData m, DateTime referenceDate) =>
            m?.GetStageAtDate(referenceDate);

        // ----- 선택 UI (dual-list + 화살표) -----

        private void RefreshLeftList()
        {
            _lvAll.BeginUpdate();
            _lvAll.Items.Clear();

            string keyword = _txtFilter.Text.Trim();
            var referenceDate = _dtpReference.Value;

            foreach (var name in _subSystemNames)
            {
                var m = GetMaster(name);
                string desc = m != null ? (m.Description ?? "") : (_master != null ? "(마스터 외)" : "");
                if (keyword.Length > 0
                    && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                    && desc.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int count = ElementCount(name);
                var item = new ListViewItem(name) { Tag = name };
                item.SubItems.Add(desc);
                item.SubItems.Add(m != null ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)] : "-");
                item.SubItems.Add(m?.PlanText(referenceDate) ?? "-");  // 지연 시 "지연 Nd" 텍스트만
                item.SubItems.Add(m?.ItrAText ?? "-");
                item.SubItems.Add(m?.ItrBText ?? "-");
                item.SubItems.Add(m?.ItrCText ?? "-");
                item.SubItems.Add(m?.PunchAText ?? "-");
                item.SubItems.Add(m?.PunchBText ?? "-");
                item.SubItems.Add(count.ToString());
                if (count == 0) item.ForeColor = Color.Gray;  // 마스터에만 있고 요소 미배정
                if (_selected.Contains(name)) item.BackColor = PickedBack;
                _lvAll.Items.Add(item);
            }

            _lvAll.EndUpdate();
        }

        private void AddSelection(string name)
        {
            if (!_selected.Add(name)) return;
            _selectionOrder.Add(name);
            _applyState.MarkStale("선택 변경");
        }

        private void RemoveSelection(string name)
        {
            if (!_selected.Remove(name)) return;
            _selectionOrder.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            _applyState.MarkStale("선택 변경");
        }

        /// <summary>▶ — 좌측에서 하이라이트한 행들을 우측에 추가.</summary>
        private void AddHighlightedLeft()
        {
            if (_lvAll.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvAll.SelectedItems)
            {
                AddSelection((string)item.Tag);
                item.BackColor = PickedBack;
            }
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>◀ — 우측에서 하이라이트한 행들을 제거.</summary>
        private void RemoveHighlightedRight()
        {
            if (_lvSelected.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvSelected.SelectedItems)
                RemoveSelection((string)item.Tag);
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>MCC 지연 담기 — 기준일 기준 지연 sub-system을 우측 선택 박스에 일괄 추가.</summary>
        private void AddDelayedToSelection()
        {
            if (_master == null)
            {
                MessageBox.Show("Sub-system 마스터가 없어 MCC 지연을 판정할 수 없습니다.");
                return;
            }
            var referenceDate = _dtpReference.Value;
            var delayed = _subSystemNames
                .Where(n => { var m = GetMaster(n); return m != null && m.IsDelayed(referenceDate); })
                .ToList();
            if (delayed.Count == 0)
            {
                MessageBox.Show($"기준일({referenceDate:yyyy-MM-dd}) 기준 MCC 지연 Sub-system이 없습니다.");
                return;
            }
            int added = delayed.Count(n => !_selected.Contains(n));
            foreach (var n in delayed) AddSelection(n);
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
            _lblStats.Text += $"\nMCC 지연 {delayed.Count}개 선택에 추가 (신규 {added}개)";
        }

        /// <summary>▶▶ — 현재 표시(필터 통과)된 항목 전부 추가.</summary>
        private void AddAllShown()
        {
            foreach (ListViewItem item in _lvAll.Items)
            {
                AddSelection((string)item.Tag);
                item.BackColor = PickedBack;
            }
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>◀◀ — 전체 해제.</summary>
        private void ClearSelection()
        {
            if (_selected.Count > 0) _applyState.MarkStale("선택 변경");
            _selected.Clear();
            _selectionOrder.Clear();
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        private void RefreshRightList()
        {
            var referenceDate = _dtpReference.Value;
            _lvSelected.BeginUpdate();
            _lvSelected.Items.Clear();

            foreach (var name in _selectionOrder)
            {
                var m = GetMaster(name);
                int count = ElementCount(name);

                var item = new ListViewItem("■") { UseItemStyleForSubItems = false, Tag = name };
                // 스와치 = 실제 칠해질 마스터 단계색(기준일) — 마스터 없으면 중립 회색
                var stg = StageOf(m, referenceDate);
                item.ForeColor = stg.HasValue && _stageSettings.TryGetValue(stg.Value, out var st)
                    ? st.DisplayColor : Color.DimGray;
                item.SubItems.Add(name);
                item.SubItems.Add(m != null ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)] : "-");
                item.SubItems.Add(m?.PlanText(referenceDate) ?? "-");
                item.SubItems.Add(count.ToString());

                string matchText = "-";
                Color matchColor = Color.Black;
                if (_appliedOnce && _appliedSubSystems.Contains(name) && count > 0
                    && _bySubSystem.TryGetValue(name, out var els))
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchText = matched.ToString();
                    if (matched < count) matchColor = Color.Red;
                }
                var matchSub = item.SubItems.Add(matchText);
                matchSub.ForeColor = matchColor;

                _lvSelected.Items.Add(item);
            }

            _lvSelected.EndUpdate();
        }

        private void UpdateSelCount()
        {
            int elemCount = _selectionOrder.Sum(ElementCount);
            _lblSelCount.Text = $"선택된 Sub-system: {_selected.Count}개 · 요소 {elemCount:N0}건";
        }

        /// <summary>공종별 스코프 라우팅(엔진과 동일)으로 요소들의 매칭 아이템을 모은다.
        /// 미빌드 인덱스의 공종은 0건으로 조용히 빠진다 (선택 연동은 best-effort).</summary>
        private Autodesk.Navisworks.Api.ModelItemCollection FindItemsFor(IEnumerable<SubSystemElement> els)
        {
            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (var group in els.GroupBy(el => SearcherFor(el.Discipline)))
            {
                if (!group.Key.IsIndexBuilt) continue;
                var found = group.Key.FindBySpoolIds(group.Select(el => el.ElementId).Distinct());
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }
            return collection;
        }

        /// <summary>우측 행 클릭 → 해당 Sub-system의 매칭 아이템을 3D에서 선택·포커스.</summary>
        private void LvSelected_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvSelected.SelectedItems.Count == 0) return;
            var doc = _main.GetDocument();
            if (doc == null || IndexStale(doc)) return;

            var els = new List<SubSystemElement>();
            foreach (ListViewItem item in _lvSelected.SelectedItems)
            {
                if (_bySubSystem.TryGetValue((string)item.Tag, out var found))
                    els.AddRange(found);
            }
            if (els.Count == 0) return;

            var collection = FindItemsFor(els);
            if (collection.Count == 0) return;

            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        // ----- 가시화 -----

        private void ModeChanged()
        {
            if (_stagePanel == null || _progressPanel == null) return; // 초기화 중 이벤트 방어
            _stagePanel.Visible = _rdoByStage.Checked;
            _progressPanel.Visible = !_rdoByStage.Checked;
            // 색이 이전 모드 기준으로 남아 있으면 상태 표시기로 경고 (통계 라벨은 통계만 — P0-1)
            _applyState.MarkStale("시각화 모드 변경");
        }

        /// <summary>색/투명도 증분 변경 — 마지막 적용 모드와 같은 그리드에서만 유효.</summary>
        private void IncrementalUpdate(ApplyMode mode, string key, ColorSetting setting)
        {
            var doc = _main.GetDocument();
            if (doc == null || _appliedMode != mode || !_main.OverrideEngine.HasCachedData(VisualModule.SubSystem)) return;
            _main.OverrideEngine.UpdateStageColor(doc, VisualModule.SubSystem, key, setting);
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            // 단순 marquee만으로는 무엇을 하는지 알 수 없어 단계 문구 병기 (UX audit P0-3)
            _lblStats.Text = "모델 태그 인덱스 생성 중… (공종별)";
            Application.DoEvents();

            // 공종마다 자기 태그 셋으로 자기 nwd 하나만 레벨 타겟 (general walk 없음, 하드 스코프
            // = 그 파일에서만 — 미발견 시 전체 트리 안 훑고 0건 + 진단). 요소 있는 공종만 빌드.
            BuildDiscipline(doc, SubSystemDiscipline.Equipment,    _eqSearcher,     NwdScope.Equipment);
            BuildDiscipline(doc, SubSystemDiscipline.Piping,       _pipingSearcher, NwdScope.Hydrotest);
            BuildDiscipline(doc, SubSystemDiscipline.EitEquipment, _eitEqSearcher,  NwdScope.EitTray);
            BuildDiscipline(doc, SubSystemDiscipline.Cable,        _cableSearcher,  NwdScope.Cable);

            _needsIndexRebuild = false;
            _indexBuilt = true;
            _indexSig = DocSig(doc);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        /// <summary>그 공종 요소의 ElementId 셋으로 지정 nwd 스코프만 레벨 타겟 인덱싱(하드 스코프).
        /// 요소 0건이면 스킵. §2 리스크: 요소가 여러 깊이에 섞이면 첫 매칭 깊이만 인덱싱 —
        /// general walk 시절과 매칭 건수 대조 필요(Windows).</summary>
        private void BuildDiscipline(Autodesk.Navisworks.Api.Document doc,
            SubSystemDiscipline disc, ModelItemSearcher searcher, NwdScope scope)
        {
            var ids = new HashSet<string>(
                _elements.Where(el => el.Discipline == disc).Select(el => el.ElementId),
                StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) { searcher.Reset(); return; }
            searcher.BuildIndexForTags(doc, ids, scope, hardScope: true);
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _elements.Count == 0)
            {
                MessageBox.Show("OASIS 데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_selected.Count == 0)
            {
                MessageBox.Show("가시화할 Sub-system을 먼저 선택하세요.");
                return;
            }
            if (_rdoByStage.Checked && _master == null)
            {
                MessageBox.Show("Sub-system 마스터가 없어 단계별 가시화를 할 수 없습니다.\n요소 진행상태별 모드를 사용하세요.");
                return;
            }
            if (IndexStale(doc))
                BuildIndex();

            var targets = _elements.Where(el => _selected.Contains(el.SubSystem)).ToList();
            var referenceDate = _dtpReference.Value;

            OverrideResult result;
            ApplyMode mode;
            // 색칠은 permanent override 배치 — 진행바+단계 문구로 UI 프리즈 인상 제거 (P0-3).
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "색상 적용 중…";
            Application.DoEvents();
            try
            {
                if (_rdoByStage.Checked)
                {
                    mode = ApplyMode.Stage;
                    var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in _stageChecks)
                        if (kv.Value.Checked)
                            groupSettings[kv.Key.ToString()] = _stageSettings[kv.Key];

                    // 요소는 자기 sub-system의 마스터 실적 단계색을 받는다(지연 여부와 무관).
                    // 마스터 외 sub-system 요소는 그룹 키 null → 색칠 제외(매칭 집계에는 포함).
                    result = _main.OverrideEngine.ApplySubSystem(doc, targets,
                        el => StageOf(GetMaster(el.SubSystem), referenceDate)?.ToString(),
                        groupSettings, SearcherFor);
                }
                else
                {
                    mode = ApplyMode.Progress;
                    var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in _progressChecks)
                        if (kv.Value.Checked)
                            groupSettings[kv.Key.ToString()] = _progressSettings[kv.Key];

                    result = _main.OverrideEngine.ApplySubSystem(doc, targets,
                        el => el.StatusAt(referenceDate).ToString(), groupSettings, SearcherFor);
                }
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
            }

            _unmatchedIds = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedIds = new HashSet<string>(
                targets.Select(el => el.ElementId).Where(id => !_unmatchedIds.Contains(id)),
                StringComparer.OrdinalIgnoreCase);
            _appliedSubSystems = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
            _appliedOnce = true;
            _appliedMode = mode;
            _applyState.SetApplied(
                $"{(mode == ApplyMode.Stage ? "단계별" : "진행상태별")} · {_appliedSubSystems.Count}개 sub-system");

            RefreshRightList();
            UpdateStats(result);
        }

        /// <summary>이 탭 가시화 해제: Sub-system 색만 제거 — 다른 공종 색은 유지 (§10 ResetModule).</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.ResetModule(doc, VisualModule.SubSystem);
            _lblStats.Text = "이 탭 가시화 해제 완료 (Sub-system 색만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 가시화 해제 완료";
            _applyState.SetCleared();
        }

        // ----- 현황 리포트 -----

        /// <summary>
        /// CSV 리포트 (CLAUDE.md 8번 단기안): 헤더 블록 + Sub-system별 요약(마스터
        /// 단계·ITR·Punch 병기) + 상세 리스트. 선택이 있으면 선택만, 없으면 전체.
        /// 매칭 O/X는 마지막 [적용]에 포함됐던 Sub-system에만 유효 — 그 외는 "-".
        /// </summary>
        private void BtnReport_Click(object sender, EventArgs e)
        {
            if (_subSystemNames.Count == 0)
            {
                MessageBox.Show("OASIS 데이터를 먼저 로드하세요.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            bool selectedOnly = _selected.Count > 0;
            var names = selectedOnly ? _selectionOrder.ToList() : _subSystemNames.ToList();

            var lines = new List<string>();
            lines.Add("Sub-system 현황 리포트");
            lines.Add($"출력 시각,{DateTime.Now:yyyy-MM-dd HH:mm}");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv("OASIS " + _loadLabel)}");
            lines.Add($"Sub-system 마스터,{(_master != null ? $"{_master.Count}개 로드됨" : "미구성 — 요소 파생 목록 기준")}");
            int delayedCount = names.Count(n => { var mm = GetMaster(n); return mm != null && mm.IsDelayed(referenceDate); });
            lines.Add($"MCC 지연,{delayedCount}개 (계획일 경과·P-MCC/MCC 실적 미입력)");
            lines.Add($"집계 대상,{(selectedOnly ? "선택" : "전체")} Sub-system {names.Count}개");
            lines.Add($"매칭 기준,{(_appliedOnce ? "가시화 적용 결과 (적용된 Sub-system만 산정)" : "미적용 — 매칭 미산정")}");
            lines.Add($"인덱스 스코프,{Csv(ScopeNotes())}");

            lines.Add("");
            lines.Add("[Sub-system별 요약]");
            lines.Add("Sub-system,Description,단계,MCC계획,지연(일),A-ITR,B-ITR,C-ITR,Punch A,Punch B,요소,Equipment,Piping,EIT EQ,Cable,매칭,미매칭,미착수,진행중,완료,완료율(%)");

            int tElems = 0, tEq = 0, tPip = 0, tEitEq = 0, tCable = 0,
                tMatched = 0, tUnmatched = 0, tNs = 0, tIp = 0, tDone = 0;
            bool anyMatchInfo = false;
            foreach (var name in names)
            {
                var m = GetMaster(name);
                var els = _bySubSystem.TryGetValue(name, out var found) ? found : new List<SubSystemElement>();
                int eq    = els.Count(el => el.Discipline == SubSystemDiscipline.Equipment);
                int pip   = els.Count(el => el.Discipline == SubSystemDiscipline.Piping);
                int eitEq = els.Count(el => el.Discipline == SubSystemDiscipline.EitEquipment);
                int cable = els.Count(el => el.Discipline == SubSystemDiscipline.Cable);

                int ns = 0, ip = 0, done = 0;
                foreach (var el in els)
                {
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }

                string matchedText = "-", unmatchedText = "-";
                if (_appliedOnce && _appliedSubSystems.Contains(name) && els.Count > 0)
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchedText = matched.ToString();
                    unmatchedText = (els.Count - matched).ToString();
                    tMatched += matched;
                    tUnmatched += els.Count - matched;
                    anyMatchInfo = true;
                }

                string stageLabel = m != null
                    ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)]
                    : (_master != null ? "마스터 외" : "-");
                string planStr = m?.MccPlan?.ToString("yyyy-MM-dd") ?? "";
                string delayStr = (m != null && m.IsDelayed(referenceDate)) ? m.DelayDays(referenceDate).ToString() : "-";
                string doneRate = els.Count > 0 ? (done * 100.0 / els.Count).ToString("F1") : "-";
                lines.Add($"{Csv(name)},{Csv(m?.Description ?? "")},{Csv(stageLabel)},{planStr},{delayStr}," +
                    $"{Csv(m?.ItrAText ?? "-")},{Csv(m?.ItrBText ?? "-")},{Csv(m?.ItrCText ?? "-")}," +
                    $"{Csv(m?.PunchAText ?? "-")},{Csv(m?.PunchBText ?? "-")}," +
                    $"{els.Count},{eq},{pip},{eitEq},{cable},{matchedText},{unmatchedText},{ns},{ip},{done},{doneRate}");
                tElems += els.Count; tEq += eq; tPip += pip; tEitEq += eitEq; tCable += cable;
                tNs += ns; tIp += ip; tDone += done;
            }
            string totalRate = tElems > 0 ? (tDone * 100.0 / tElems).ToString("F1") : "-";
            lines.Add($"합계 ({names.Count}개),,,,,,,,,,{tElems},{tEq},{tPip},{tEitEq},{tCable}," +
                $"{(anyMatchInfo ? tMatched.ToString() : "-")}," +
                $"{(anyMatchInfo ? tUnmatched.ToString() : "-")},{tNs},{tIp},{tDone},{totalRate}");

            lines.Add("");
            lines.Add("[상세 리스트]");
            lines.Add("Sub-system,공종,요소 ID,설명,현재 단계,진행 상태,매칭");
            foreach (var name in names)
            {
                if (!_bySubSystem.TryGetValue(name, out var els)) continue;
                var ordered = els
                    .OrderBy(el => el.Discipline)
                    .ThenBy(el => el.ElementId, StringComparer.OrdinalIgnoreCase);
                foreach (var el in ordered)
                {
                    string matched = "-";
                    if (_appliedOnce && _appliedSubSystems.Contains(name))
                        matched = _matchedIds.Contains(el.ElementId) ? "O" : "X";
                    lines.Add($"{Csv(name)},{SubSystemDisciplineInfo.Labels[el.Discipline]},{Csv(el.ElementId)},{Csv(el.Description)}," +
                        $"{Csv(el.StageLabelAt(referenceDate))},{ProgressStatusInfo.Labels[el.StatusAt(referenceDate)]},{matched}");
                }
            }

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SubSystem_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "현황 리포트 출력", path);
        }

        private static string Csv(string s) =>
            "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        // ----- 상세 현황 별도 창 -----

        /// <summary>
        /// 선택된 sub-system의 공종·요소별 상세 현황을 별도 창(비모달 Form)으로 띄운다.
        /// 다공종(Equipment/Piping) 요소가 한 그리드에 sub-system → 공종 → 요소 순으로
        /// 나열되며, 상단 검색으로 좁히고 [CSV 출력]으로 엑셀 저장도 된다.
        /// </summary>
        private void ShowDetailWindow()
        {
            if (_selected.Count == 0)
            {
                MessageBox.Show("먼저 Sub-system을 선택하세요.");
                return;
            }
            var referenceDate = _dtpReference.Value;
            var names = _selectionOrder.Where(_bySubSystem.ContainsKey).ToList();
            if (names.Count == 0)
            {
                MessageBox.Show("선택된 Sub-system에 요소가 없습니다 (마스터에만 존재).");
                return;
            }

            var form = new Form
            {
                Text = $"Sub-system 상세 현황 — 선택 {names.Count}개 · 기준일 {referenceDate:yyyy-MM-dd}",
                Width = 940,
                Height = 620,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = true,
            };

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 0) };
            var lblSummary = new Label { AutoSize = true, Padding = new Padding(0, 5, 12, 0) };
            var txtSearch = new TextBox { Width = 160, Margin = new Padding(0, 3, 6, 0) };
            var btnCsv = new Button { Text = "CSV 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 0, 8, 0) };
            top.Controls.Add(lblSummary);
            top.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            top.Controls.Add(txtSearch);
            top.Controls.Add(btnCsv);
            var btnCopyDetail = new Button { Text = "클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 0, 8, 0) };
            top.Controls.Add(btnCopyDetail);

            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
            };
            lv.Columns.Add("Sub-system", 110);
            lv.Columns.Add("Description", 150);
            lv.Columns.Add("MCC계획", 90);
            lv.Columns.Add("공종", 80);
            lv.Columns.Add("요소 ID", 180);
            lv.Columns.Add("설명", 150);
            lv.Columns.Add("현재 단계", 90);
            lv.Columns.Add("진행 상태", 70);
            lv.Columns.Add("매칭", 46);
            ListViewClipboard.EnableCtrlC(lv);   // Ctrl+C 복사 (상세 창)

            // 3D 선택 연동: 행 더블클릭 → 그 요소를 뷰에서 선택·포커스
            lv.DoubleClick += (s, e) =>
            {
                if (lv.SelectedItems.Count == 0) return;
                var el = lv.SelectedItems[0].Tag as SubSystemElement;
                var doc = _main.GetDocument();
                if (el == null || doc == null || IndexStale(doc)) return;
                var collection = FindItemsFor(new[] { el });
                if (collection.Count == 0) return;
                doc.CurrentSelection.CopyFrom(collection);
                doc.ActiveView.FocusOnCurrentSelection();
            };

            Action<string> populate = keyword =>
            {
                lv.BeginUpdate();
                lv.Items.Clear();
                int shown = 0;
                foreach (var name in names)
                {
                    var m = GetMaster(name);
                    string plan = m?.PlanText(referenceDate) ?? "-";
                    string desc = m?.Description ?? "";
                    var ordered = _bySubSystem[name]
                        .OrderBy(el => el.Discipline)
                        .ThenBy(el => el.ElementId, StringComparer.OrdinalIgnoreCase);
                    foreach (var el in ordered)
                    {
                        if (!string.IsNullOrEmpty(keyword)
                            && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                            && (el.ElementId ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                            && (el.Description ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        string matched = "-";
                        if (_appliedOnce && _appliedSubSystems.Contains(name))
                            matched = _matchedIds.Contains(el.ElementId) ? "O" : "X";

                        var item = new ListViewItem(name) { UseItemStyleForSubItems = false, Tag = el };
                        item.SubItems.Add(desc);
                        item.SubItems.Add(plan);
                        item.SubItems.Add(SubSystemDisciplineInfo.Labels[el.Discipline]);
                        item.SubItems.Add(el.ElementId ?? "");
                        item.SubItems.Add(el.Description ?? "");
                        item.SubItems.Add(el.StageLabelAt(referenceDate));
                        item.SubItems.Add(ProgressStatusInfo.Labels[el.StatusAt(referenceDate)]);
                        var ms = item.SubItems.Add(matched);
                        if (matched == "X") ms.ForeColor = Color.Red;
                        lv.Items.Add(item);
                        shown++;
                    }
                }
                lv.EndUpdate();
                lblSummary.Text = $"요소 {shown:N0}건";
            };

            txtSearch.TextChanged += (s, e) => populate(txtSearch.Text.Trim());
            btnCsv.Click += (s, e) => ExportDetailCsv(names, referenceDate);
            btnCopyDetail.Click += (s, e) => ListViewClipboard.CopySelectedOrAll(lv);

            populate("");

            form.Controls.Add(lv);
            form.Controls.Add(top);
            form.Show();  // 비모달 — 창을 열어둔 채 3D 작업 가능
        }

        /// <summary>상세 창의 CSV 출력 — sub-system·공종·요소별 status 전체를 바탕화면에 저장.</summary>
        private void ExportDetailCsv(List<string> names, DateTime referenceDate)
        {
            var lines = new List<string>();
            lines.Add("Sub-system 공종·요소별 상세 현황");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv("OASIS " + _loadLabel)}");
            lines.Add($"매칭 기준,{(_appliedOnce ? "가시화 적용 결과" : "미적용 — 매칭 미산정")}");
            lines.Add("");
            lines.Add("Sub-system,Description,MCC계획,공종,요소 ID,설명,현재 단계,진행 상태,매칭");
            foreach (var name in names)
            {
                var m = GetMaster(name);
                string plan = m?.MccPlan?.ToString("yyyy-MM-dd") ?? "";
                var ordered = _bySubSystem[name]
                    .OrderBy(el => el.Discipline)
                    .ThenBy(el => el.ElementId, StringComparer.OrdinalIgnoreCase);
                foreach (var el in ordered)
                {
                    string matched = "-";
                    if (_appliedOnce && _appliedSubSystems.Contains(name))
                        matched = _matchedIds.Contains(el.ElementId) ? "O" : "X";
                    lines.Add($"{Csv(name)},{Csv(m?.Description ?? "")},{plan}," +
                        $"{SubSystemDisciplineInfo.Labels[el.Discipline]},{Csv(el.ElementId)},{Csv(el.Description)}," +
                        $"{Csv(el.StageLabelAt(referenceDate))},{ProgressStatusInfo.Labels[el.StatusAt(referenceDate)]},{matched}");
                }
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SubSystem_Detail_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "선택 Sub-system 상세 현황", path);
        }

        // ----- 기타 -----

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"SubSystem_{DateTime.Now:yyyyMMdd_HHmm}";
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

        private void UpdateStats(OverrideResult result = null)
        {
            if (_subSystemNames.Count == 0)
            {
                _lblStats.Text = "로드된 데이터 없음";
                return;
            }

            var referenceDate = _dtpReference.Value;
            var linesOut = new List<string>();

            if (_selected.Count == 0)
            {
                linesOut.Add($"요소 {_elements.Count:N0}건 · Sub-system {_subSystemNames.Count}개 — 좌측에서 선택하세요");
            }
            else
            {
                int total = _selectionOrder.Sum(ElementCount);
                linesOut.Add($"선택 Sub-system {_selected.Count}개 · 요소 {total:N0}건 (기준일 {referenceDate:yyyy-MM-dd})");

                // 마스터가 있으면 sub-system 실적 단계 분포 (+ 지연 개수 병기)
                if (_master != null)
                {
                    var stageCounts = new Dictionary<SubSystemStage, int>();
                    int delayed = 0;
                    foreach (var name in _selectionOrder)
                    {
                        var m = GetMaster(name);
                        if (m == null) continue;
                        var stg = m.GetStageAtDate(referenceDate);
                        stageCounts[stg] = stageCounts.TryGetValue(stg, out int c) ? c + 1 : 1;
                        if (m.IsDelayed(referenceDate)) delayed++;
                    }
                    var parts = new List<string>();
                    var order = new[] { SubSystemStage.Pcc, SubSystemStage.Mcc,
                        SubSystemStage.PartialMcc, SubSystemStage.Walkdown, SubSystemStage.NotStarted };
                    foreach (var st in order)
                        if (stageCounts.TryGetValue(st, out int c) && c > 0)
                            parts.Add($"{SubSystemStageInfo.Labels[st]} {c}");
                    string line = "단계: " + string.Join(" · ", parts);
                    if (delayed > 0) line += $"   (MCC 지연 {delayed})";
                    if (parts.Count > 0) linesOut.Add(line);
                }

                int ns = 0, ip = 0, done = 0;
                foreach (var el in _elements)
                {
                    if (!_selected.Contains(el.SubSystem)) continue;
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }
                linesOut.Add($"요소 진행: 미착수 {ns} · 진행중 {ip} · 완료 {done}");
            }

            if (_appliedOnce)
            {
                string mode = _appliedMode == ApplyMode.Stage ? "Sub-system 단계별" : "요소 진행상태별";
                linesOut.Add($"매칭 {_matchedIds.Count:N0} / 미매칭 {_unmatchedIds.Count:N0} ({mode} 적용됨)");
            }

            _lblStats.Text = string.Join("\n", linesOut);
        }

        private Dictionary<T, ColorSetting> CloneDefaults<T>(Dictionary<T, ColorSetting> defaults)
        {
            var clone = new Dictionary<T, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
