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
    /// 전체 Sub-system의 요소를 단계색으로 3D에 가시화하고 현황 리포트를 출력한다.
    ///
    /// - 데이터: OASIS 전용. 마스터 미구성 시 요소 파생 목록으로 자동 fallback.
    ///   EIT 계열은 공종별 try/catch — 컬럼 미구성이어도 나머지는 정상 로드(라벨에 사유)
    /// - 매칭: 공종마다 자기 nwd 하나만 레벨 타겟(general walk 없음, 하드 스코프):
    ///   Equipment=MEQ / Piping=HYDROPKG / EIT EQ=EIT / Cable=CABLE. 4개 사유 searcher,
    ///   SearcherFor로 라우팅(엔진 ApplySubSystem에 리졸버 주입).
    /// - 가시화 2모드: Sub-system 단계별(Walkdown→PCC 6색, 마스터 필요) /
    ///   요소 진행상태별(미착수·진행중·완료)
    /// - [가시화 적용]은 선택과 무관하게 전체 Sub-system을 단계색으로 칠한다 (다른 탭과 동일 —
    ///   버튼 행은 색상 그리드 바로 아래, 선택창 위에 위치). 색칠 전 ResetModule로 누적분 원복.
    /// - 선택 UI(하단 dual-list)는 색칠이 아니라 "선택 항목만 남김"(isolate 숨김 토글)과
    ///   "선택 Sub-system 상세 현황 보기"의 대상: 좌측 검색+상태 테이블(~400개) ↔
    ///   [▶ ◀ ▶▶ ◀◀] ↔ 우측 선택 누적 테이블 + 하단 개수 라벨·선택 액션 버튼.
    /// </summary>
    public class SubSystemTab : UserControl, IOverviewSource
    {
        private enum ApplyMode { Stage, Progress, SystemPalette }

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

        private static string DocSig(Autodesk.Navisworks.Api.Document doc) =>
            doc == null ? "?" : ModelItemSearcher.DocumentFingerprint(doc);

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

        /// <summary>Overview 탭 상태 노출 — 인메모리 조회만 (IOverviewSource). 인덱스는 공종별 4개 합산.</summary>
        public OverviewStatus GetOverviewStatus()
        {
            int idx = 0;
            bool anyBuilt = false, fellBack = false;
            foreach (var s in new[] { _eqSearcher, _pipingSearcher, _eitEqSearcher, _cableSearcher })
            {
                if (!s.IsIndexBuilt) continue;
                anyBuilt = true;
                idx += s.IndexedCount;
                fellBack |= s.LastScopeFellBack;
            }
            return new OverviewStatus
            {
                DataLoaded = _elements.Count > 0,
                DataText = _elements.Count > 0
                    ? $"{(_paletteMode ? "Excel 형상" : "OASIS")} 요소 {_elements.Count:N0}건 · SS {_subSystemNames.Count}개" : "미로드",
                IndexText = anyBuilt ? idx.ToString("N0") : "-",
                ApplyStateText = _applyState.Text + (_hiddenByKeepOnly != null ? " · 숨김 활성" : ""),
                ApplyStale = _applyState.IsStale,
                MatchedText = _appliedOnce ? _matchedIds.Count.ToString("N0") : "-",
                UnmatchedText = _appliedOnce ? _unmatchedIds.Count.ToString("N0") : "-",
                UnmatchedCount = _appliedOnce ? _unmatchedIds.Count : 0,
                ScopeNote = ScopeNotes(),
                ScopeFellBack = fellBack,
            };
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
        // Excel 형상 import 팔레트: 시스템(sub-system 앞2자리)별 색. 390+ sub-system을 개별 색칠하면
        // 구분이 안 되므로 앞2자리(=큰 시스템)로 묶어 색을 배정한다 (사용자 결정 2026-07).
        private Dictionary<string, ColorSetting> _systemColors = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
        private bool _paletteMode;   // true = Excel 형상 소스(단계/진행 대신 시스템 팔레트로 색칠)

        private Label _dotOasis;
        private Label _lblOasis;
        // 로드 요약이 라벨 폭을 넘으면 잘리므로 전체 문구를 툴팁으로 노출
        private readonly ToolTip _loadTip = new ToolTip { AutoPopDelay = 15000 };
        private GroupBox _modeGroup;          // 시각화 모드 그룹 (팔레트 모드에선 숨김)
        private RadioButton _rdoByStage;
        private RadioButton _rdoByProgress;
        private DateTimePicker _dtpReference;
        private Label _stageColorLabel;       // "단계 색상" 헤더 (팔레트 모드에선 숨김)
        private Panel _stagePanel;
        private Panel _progressPanel;
        private Control _colorEditRow;        // 색상 편집 토글 행 (팔레트 모드에선 숨김)
        private Label _paletteLabel;          // "시스템(앞2자리) 색상" 헤더 (팔레트 모드에서만)
        private Panel _paletteLegend;         // 시스템→색 범례 (팔레트 모드에서만, 로드 후 동적 구성)
        private TextBox _txtFilter;
        private Debouncer _filterDebounce;   // 키 입력마다 좌측 목록 재생성 방지 (성능 audit P0-1)
        private Button _btnDelayed;
        private Button _btnDetail;
        private Button _btnKeepOnly;        // 선택 항목만 남김 (선택 sub-system만 격리, isolate 토글)
        private Button _btnHideUnmatched;   // 매칭 안 된 항목 숨기기 (전체 매칭분 유지, isolate 토글)
        // 두 isolate 토글이 공유하는 숨김 복원용 — 재로드/해제/문서 전환 시 원복
        private Autodesk.Navisworks.Api.ModelItemCollection _hiddenByKeepOnly;
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
            // 문서 전환/같은 파일 재로드 → 사유 searcher 4개도 함께 무효화 (2차 audit P1 —
            // 지문이 같아지는 재로드는 이벤트로만 잡힌다). 다음 [적용]에서 IndexStale이 재빌드.
            _main.IndexesInvalidated += OnIndexesInvalidated;
            this.Disposed += (s, e) => _main.IndexesInvalidated -= OnIndexesInvalidated;
        }

        private void OnIndexesInvalidated()
        {
            _indexBuilt = false;
            _indexSig = null;
            // isolate 숨김 상태는 여기서 건드리지 않는다 (CableLineTab과 동일하게 보존). 이 이벤트는
            // 진짜 문서 전환뿐 아니라 같은 문서의 FileNameChanged(§16 — NWD Export의 SaveFile)에서도
            // 오므로, 참조를 버리면 숨김 요소가 복원 불가로 남는다("전체 보기"·해제·재로드가 못 되살림).
            // 문서 전환 후의 stale 컬렉션은 다음 RestoreKeepOnlyHidden(해제/재로드/토글) 호출의
            // try/catch가 안전하게 정리한다.
            _eqSearcher.Reset();
            _pipingSearcher.Reset();
            _eitEqSearcher.Reset();
            _cableSearcher.Reset();
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

            // ----- 데이터 로드 행 (OASIS 정식 / Excel 형상) -----
            var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            var btnOasis = new Button { Text = "OASIS 로드", Width = 100, Height = 24 };
            btnOasis.Click += (s, e) => LoadOasis();
            // 형상 전용 Excel(시트: Hydrotest/MEQ/Cable) — 정식 진척이 아니라 sub-system별 형상 보기용.
            var btnExcel = new Button { Text = "Excel 형상 import", Width = 130, Height = 24 };
            btnExcel.Click += (s, e) => LoadExcelShapes();
            _dotOasis = new Label { Text = "●", AutoSize = true, ForeColor = DotEmpty, Padding = new Padding(4, 5, 0, 0) };
            _lblOasis = new Label { Text = "(미로드)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(0, 5, 0, 0) };
            loadPanel.Controls.Add(btnOasis);
            loadPanel.Controls.Add(btnExcel);
            loadPanel.Controls.Add(_dotOasis);
            loadPanel.Controls.Add(_lblOasis);

            // ----- 시각화 모드 + 기준일 -----
            _modeGroup = new GroupBox { Text = "시각화 모드", Dock = DockStyle.Fill, Height = 50 };
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
            _modeGroup.Controls.Add(modeFlow);

            _stageColorLabel = new Label { Text = "단계 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 };
            _stagePanel = BuildColorGrid(
                SubSystemStageInfo.GridOrder,
                SubSystemStageInfo.Labels, _stageSettings, _stageChecks, ApplyMode.Stage);
            _progressPanel = BuildColorGrid(
                ProgressStatusInfo.Ordered,
                ProgressStatusInfo.Labels, _progressSettings, _progressChecks, ApplyMode.Progress);
            _progressPanel.Visible = false;
            _colorEditRow = ColorEditCollapse.BuildToggleRow(_stagePanel, _progressPanel);

            // 팔레트 모드(Excel 형상) 전용: 시스템(앞2자리)→색 범례 — 로드 후 RebuildPaletteLegend가 채움.
            _paletteLabel = new Label { Text = "시스템(앞2자리) 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18, Visible = false };
            _paletteLegend = new Panel { Dock = DockStyle.Fill, AutoSize = true, Visible = false };

            var selGroup = BuildSelectionGroup();

            // ----- 버튼 행 (색상 그리드 바로 아래 = 선택창 위. 다른 탭과 동일 배치) -----
            // 1행(가시화): 선택과 무관하게 전체 Sub-system을 칠한다. "매칭 안 된 항목 숨기기"는
            // 선택과 무관한 전역 정리라 이 행에, "선택 항목만 남김"은 선택 대상이라 선택창 하단에 둔다.
            var btnRowVis = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            var btnApply = new Button { Text = "가시화 적용", Width = 100 };
            _btnHideUnmatched = new Button { Text = "매칭 안 된 항목 숨기기", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnApply.Click += BtnApply_Click;
            _btnHideUnmatched.Click += BtnHideUnmatched_Click;
            _applyState.AttachApplyButton(btnApply);
            btnRowVis.Controls.AddRange(new Control[] { btnApply, _btnHideUnmatched, _applyState });

            // 2행(해제)
            var btnRowReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            var btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            var btnReset = new Button { Text = "전체 가시화 해제", Width = 130 };
            btnResetModule.Click += BtnResetModule_Click;
            btnReset.Click += BtnReset_Click;
            btnRowReset.Controls.AddRange(new Control[] { btnResetModule, btnReset });

            // 3행(출력)
            var btnRowOut = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            var btnReport = new Button { Text = "현황 리포트 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            var btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            var btnNwd = new Button { Text = "NWD Export", Width = 110 };
            btnReport.Click += BtnReport_Click;
            btnViewpoint.Click += BtnViewpoint_Click;
            btnNwd.Click += BtnNwd_Click;
            btnRowOut.Controls.AddRange(new Control[] { btnReport, btnViewpoint, btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            // 최대 6줄(전체·단계·요소진행·선택·매칭 + MCC 지연 담기 append) 대비 높이 확보.
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 110 };

            layout.Controls.Add(loadPanel);
            layout.Controls.Add(_modeGroup);
            layout.Controls.Add(_stageColorLabel);
            layout.Controls.Add(_stagePanel);
            layout.Controls.Add(_progressPanel);
            // 색상 편집(▼·투명도)은 기본 접힘 — 체크박스·스와치만 상시 노출 (UX audit P1)
            layout.Controls.Add(_colorEditRow);
            // 팔레트 모드 전용 헤더·범례 (Excel 형상 소스일 때만 보임)
            layout.Controls.Add(_paletteLabel);
            layout.Controls.Add(_paletteLegend);
            // 버튼 행을 선택창 위로 (사용자 요청): 색상 그리드 → 버튼 → 통계 → 선택창.
            layout.Controls.Add(btnRowVis);
            layout.Controls.Add(btnRowReset);
            layout.Controls.Add(btnRowOut);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);
            layout.Controls.Add(selGroup);

            Controls.Add(layout);
            UpdateModeUiVisibility();
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
            // 선택은 색칠 대상이 아니라 상세보기·"선택 항목만 남김"(isolate)의 대상임을 제목에 명시.
            var group = new GroupBox { Text = "Sub-system 선택 (상세 보기·선택 항목만 남김 대상)", Dock = DockStyle.Fill, Height = 372 };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // 좌측 상단: 검색 (코드 + 설명 매칭) + MCC 지연 일괄 선택
            var leftTop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            leftTop.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _txtFilter = new TextBox { Width = 120, Margin = new Padding(0, 3, 3, 0) };
            // 입력 즉시가 아니라 입력이 멈춘 뒤 1회만 목록 갱신 (성능 audit P0-1)
            _filterDebounce = new Debouncer(RefreshLeftList);
            _txtFilter.TextChanged += (s, e) => _filterDebounce.Trigger();
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
                Text = "선택항목 클립보드 복사",
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

            // 선택 박스 하단: 선택 대상에 대한 액션 2개 — isolate 숨김 토글 + 상세 현황 창.
            // 선택 항목만 남김: 선택 외 Sub-system 요소를 3D에서 숨겨 선택 요소만 남긴다(토글).
            _btnKeepOnly = new Button
            {
                Text = "선택 항목만 남김",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 1, 8, 1),
                Margin = new Padding(0, 2, 6, 0),
            };
            _btnKeepOnly.Click += BtnKeepOnly_Click;

            _btnDetail = new Button
            {
                Text = "선택 Sub-system 상세 현황 보기…",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 1, 8, 1),
                Margin = new Padding(0, 2, 0, 0),
            };
            _btnDetail.Click += (s, e) => ShowDetailWindow();

            var selActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            selActions.Controls.Add(_btnKeepOnly);
            selActions.Controls.Add(_btnDetail);

            grid.Controls.Add(leftTop, 0, 0);
            grid.Controls.Add(rightTopLbl, 2, 0);
            grid.Controls.Add(_lvAll, 0, 1);
            grid.Controls.Add(arrows, 1, 1);
            grid.Controls.Add(_lvSelected, 2, 1);
            grid.Controls.Add(_lblSelCount, 0, 2);
            grid.SetColumnSpan(_lblSelCount, 3);
            grid.Controls.Add(selActions, 0, 3);
            grid.SetColumnSpan(selActions, 3);

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
            // 재로드 시 매칭 셋이 바뀌므로 isolate 숨김을 먼저 원복 (옛 상태 잔존 방지).
            RestoreKeepOnlyHidden(_main.GetDocument());
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
                _paletteMode = false;          // OASIS = 단계/진행 색칠
                RebuildNames(out int outsideMaster);
                AfterElementsLoaded();

                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _loadLabel = $"OASIS {settings.Database}/{prj} · {DateTime.Now:HH:mm}";
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.ForeColor = Color.Black;
                _lblOasis.Text = $"요소 {list.Count:N0}건 · Sub-system {_subSystemNames.Count}개{masterNote}"
                    + (outsideMaster > 0 ? $" · 마스터 외 {outsideMaster}개" : "")
                    + (noSubSystem > 0 ? $" · 미지정 {noSubSystem}건 제외" : "")
                    + (disciplineNotes.Count > 0 ? " · " + string.Join(" · ", disciplineNotes) : "");
                // 문구가 길어 창 폭에 잘릴 수 있음 — 전체 문구는 마우스 오버 툴팁으로 제공
                _loadTip.SetToolTip(_lblOasis, _lblOasis.Text);
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
        /// Sub-system 형상 전용 Excel(시트 Hydrotest/MEQ/Cable) import — 정식 진척이 아니라
        /// sub-system별 형상을 시스템(앞2자리) 색으로 보여주기 위한 소스. 마스터/날짜 없음 →
        /// 팔레트 모드로 전환. 마지막 로드 소스가 활성(비정식 단일 슬롯 — OASIS와 상호 교체).
        /// </summary>
        private void LoadExcelShapes()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Sub-system 형상 Excel (시트명에 Hydrotest / MEQ / Cable 포함)",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                // 재로드 시 매칭 셋이 바뀌므로 isolate 숨김을 먼저 원복.
                RestoreKeepOnlyHidden(_main.GetDocument());
                try
                {
                    var list = ExcelLoader.LoadSubSystemShapes(dlg.FileName, out List<string> notes);
                    if (list.Count == 0)
                    {
                        _dotOasis.ForeColor = DotFailed;
                        _lblOasis.ForeColor = DotFailed;
                        _lblOasis.Text = "형상 0건 — 시트/컬럼 확인";
                        _loadTip.SetToolTip(_lblOasis, string.Join(" · ", notes));
                        MessageBox.Show("형상 데이터를 찾지 못했습니다.\n시트명에 Hydrotest/MEQ/Cable이 포함돼 있고 ID·Sub-system 컬럼이 있는지 확인하세요.\n\n"
                            + string.Join("\n", notes), "형상 Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // OASIS 마스터(System_Summary)를 선택적으로 로드 — 연결되면 설명·그룹·전체 정의를
                    // 마스터에서 가져오고, 안 되면 Excel 기준으로 graceful degrade (사용자 결정 Q1-b).
                    // 형상은 Excel, sub-system 정의는 마스터 — 매칭은 여전히 요소 ID 기반이라 무관.
                    Dictionary<string, SubSystemMasterData> master = null;
                    string masterNote;
                    try
                    {
                        var settings = SqlConnectionSettings.Load();
                        master = new Dictionary<string, SubSystemMasterData>(StringComparer.OrdinalIgnoreCase);
                        foreach (var m in SqlLoader.LoadSubSystemMaster(settings))
                            master[m.SubSystemNo] = m;
                        masterNote = $" · 마스터 {master.Count}개";
                    }
                    catch
                    {
                        master = null;
                        masterNote = " · 마스터 미연결(Excel 기준)";
                    }

                    _elements = list;
                    _master = master;              // 있으면 설명·그룹 조회에 사용 (목록 축은 Excel — Q2-b)
                    _paletteMode = true;           // 시스템(앞2자리) 팔레트로 색칠 (Q3)
                    RebuildNames(out int outsideMaster);
                    AfterElementsLoaded();         // 여기서 RebuildSystemColors도 실행됨

                    _loadLabel = $"Excel 형상: {Path.GetFileName(dlg.FileName)} · {DateTime.Now:HH:mm}";
                    _dotOasis.ForeColor = DotLoaded;
                    _lblOasis.ForeColor = Color.Black;
                    _lblOasis.Text = $"형상 요소 {list.Count:N0}건 · Sub-system {_subSystemNames.Count}개 · 시스템 {_systemColors.Count}개{masterNote}"
                        + (outsideMaster > 0 ? $" · 마스터 외 {outsideMaster}개" : "")
                        + " · " + string.Join(" · ", notes);
                    _loadTip.SetToolTip(_lblOasis, _lblOasis.Text);
                }
                catch (Exception ex)
                {
                    _dotOasis.ForeColor = DotFailed;
                    _lblOasis.ForeColor = DotFailed;
                    _lblOasis.Text = "Excel 로드 실패";
                    MessageBox.Show($"Excel 형상 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>OASIS/Excel 공통 로드 후처리 — 선택 정리·적용 상태 리셋·모드 게이팅·UI 갱신.
        /// 호출 전 _elements·_master·_paletteMode와 RebuildNames가 완료돼 있어야 한다.</summary>
        private void AfterElementsLoaded()
        {
            _selected.RemoveWhere(name => !_subSystemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
            _selectionOrder.RemoveAll(name => !_selected.Contains(name));

            _matchedIds.Clear();
            _unmatchedIds.Clear();
            _appliedSubSystems.Clear();
            _appliedOnce = false;
            _appliedMode = null;

            // 팔레트 모드(Excel)는 단계/진행이 의미 없음 — 단계 모드·MCC 지연은 마스터가 있을 때만.
            _rdoByStage.Enabled = !_paletteMode && _master != null;
            _btnDelayed.Enabled = !_paletteMode && _master != null;
            if (_master == null && _rdoByStage.Checked)
                _rdoByProgress.Checked = true;

            if (_paletteMode)
                RebuildSystemColors();      // 시스템(앞2자리)→색 배정 + 범례 구성
            UpdateModeUiVisibility();

            _needsIndexRebuild = true;      // 레벨 타겟 인덱스는 로드 셋 기반 — 재빌드 강제

            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
            // 3D 색이 남아 있으면 이전 로드 기준 — 상태 표시기로 경고 (P0-1)
            _applyState.MarkStale("데이터 재로드");
        }

        // ----- 시스템(앞2자리) 팔레트 -----

        /// <summary>sub-system 코드의 앞 2자리 = 큰 시스템 구분 (0104-00 → "01"). 2자 미만이면 전체.</summary>
        private static string SystemPrefix(string subSystem)
        {
            string s = (subSystem ?? "").Trim();
            return s.Length >= 2 ? s.Substring(0, 2) : s;
        }

        /// <summary>로드된 요소의 시스템(앞2자리)마다 구분색을 배정하고 범례를 다시 그린다.
        /// 정렬된 시스템 목록 기준 인덱스라 재로드/재적용 시 색이 안정적이다.</summary>
        private void RebuildSystemColors()
        {
            var prefixes = _elements
                .Select(el => SystemPrefix(el.SubSystem))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _systemColors = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < prefixes.Count; i++)
                _systemColors[prefixes[i]] = new ColorSetting { DisplayColor = PaletteColor(i, prefixes.Count), Transparency = 0.0 };

            RebuildPaletteLegend(prefixes);
        }

        /// <summary>범례(시스템→색 스와치)를 팔레트 패널에 다시 채운다 — 로드 후 시스템 수에 맞춰 동적 구성.</summary>
        private void RebuildPaletteLegend(List<string> prefixes)
        {
            // 이전 범례를 dispose 후 제거 — 반복 Excel 로드 시 컨트롤 핸들 누수 방지.
            var old = _paletteLegend.Controls.Cast<Control>().ToList();
            _paletteLegend.Controls.Clear();
            foreach (var c in old) c.Dispose();
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 12, AutoSize = true };
            for (int i = 0; i < prefixes.Count; i++)
            {
                var swatch = new Panel
                {
                    Width = 20, Height = 16, BorderStyle = BorderStyle.FixedSingle,
                    BackColor = _systemColors[prefixes[i]].DisplayColor,
                    Margin = new Padding(2, 2, 0, 2),
                };
                var lbl = new Label { Text = prefixes[i], AutoSize = true, Padding = new Padding(2, 2, 8, 0) };
                int col = (i % 6) * 2;
                int row = i / 6;
                grid.Controls.Add(swatch, col, row);
                grid.Controls.Add(lbl, col + 1, row);
            }
            _paletteLegend.Controls.Add(grid);
        }

        /// <summary>golden-angle HSV로 시스템 수에 맞춰 구분색 생성 — 몇 개든 인접색이 안 겹치게 배분.</summary>
        private static Color PaletteColor(int index, int total)
        {
            double hue = (index * 137.508) % 360.0;      // 황금각 — 균등 분산
            return ColorFromHsv(hue, 0.62, 0.92);
        }

        private static Color ColorFromHsv(double hue, double sat, double val)
        {
            int hi = (int)(hue / 60) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);
            double v = val * 255, p = v * (1 - sat), q = v * (1 - f * sat), t = v * (1 - (1 - f) * sat);
            int V = (int)Math.Round(v), P = (int)Math.Round(p), Q = (int)Math.Round(q), T = (int)Math.Round(t);
            switch (hi)
            {
                case 0: return Color.FromArgb(V, T, P);
                case 1: return Color.FromArgb(Q, V, P);
                case 2: return Color.FromArgb(P, V, T);
                case 3: return Color.FromArgb(P, Q, V);
                case 4: return Color.FromArgb(T, P, V);
                default: return Color.FromArgb(V, P, Q);
            }
        }

        /// <summary>팔레트 모드(Excel 형상)면 단계/진행 UI를 숨기고 시스템 범례를, 아니면 반대로 보인다.</summary>
        private void UpdateModeUiVisibility()
        {
            bool pal = _paletteMode;
            _modeGroup.Visible = !pal;
            _stageColorLabel.Visible = !pal;
            _stagePanel.Visible = !pal && _rdoByStage.Checked;
            _progressPanel.Visible = !pal && !_rdoByStage.Checked;
            _colorEditRow.Visible = !pal;
            _paletteLabel.Visible = pal;
            _paletteLegend.Visible = pal;
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
                // OASIS 모드는 마스터 전체 목록(요소 0건 포함)을 축으로 쓴다. 팔레트(Excel 형상) 모드는
                // "형상 있는 sub-system만" 보여주되(Q2-b) 설명·단계·그룹은 GetMaster로 마스터에서 조회 —
                // 전체 390+로 목록을 부풀리지 않는다. outsideMaster는 두 모드 다 Excel/요소 코드 오탐 지표.
                if (!_paletteMode)
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
                // 스와치 = 실제 칠해질 색. 팔레트 모드는 시스템(앞2자리) 색, 아니면 마스터 단계색(기준일).
                if (_paletteMode)
                {
                    item.ForeColor = _systemColors.TryGetValue(SystemPrefix(name), out var sc)
                        ? sc.DisplayColor : Color.DimGray;
                }
                else
                {
                    var stg = StageOf(m, referenceDate);
                    item.ForeColor = stg.HasValue && _stageSettings.TryGetValue(stg.Value, out var st)
                        ? st.DisplayColor : Color.DimGray;
                }
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
            UpdateModeUiVisibility();   // 팔레트 모드 여부 + 단계/진행 그리드 가시성 일괄 반영
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
            // EIT EQ는 넓은 Eit("EIT") 스코프 — EQ 파일이 필요하므로 트레이 전용 EitTray("TRAY")가 아님.
            BuildDiscipline(doc, SubSystemDiscipline.EitEquipment, _eitEqSearcher,  NwdScope.Eit);
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
                MessageBox.Show("데이터(OASIS 또는 Excel 형상)를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (!_paletteMode && _rdoByStage.Checked && _master == null)
            {
                MessageBox.Show("Sub-system 마스터가 없어 단계별 가시화를 할 수 없습니다.\n요소 진행상태별 모드를 사용하세요.");
                return;
            }
            if (IndexStale(doc))
                BuildIndex();

            // 선택과 무관하게 전체 Sub-system 요소를 칠한다 (사용자 요청 — 선택은 상세보기·isolate 전용).
            var targets = _elements;
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
                if (_paletteMode)
                {
                    // Excel 형상: 시스템(앞2자리)별 팔레트 색 — 단계/진행 없이 형상을 색으로 구분.
                    mode = ApplyMode.SystemPalette;
                    var groupSettings = new Dictionary<string, ColorSetting>(_systemColors, StringComparer.OrdinalIgnoreCase);
                    result = _main.OverrideEngine.ApplySubSystem(doc, targets,
                        el => SystemPrefix(el.SubSystem), groupSettings, SearcherFor);
                }
                else if (_rdoByStage.Checked)
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
            // 전체 적용 — 요소를 가진 모든 sub-system이 적용 대상 (매칭 O/X는 이 스냅샷 기준 유효).
            _appliedSubSystems = new HashSet<string>(_bySubSystem.Keys, StringComparer.OrdinalIgnoreCase);
            _appliedOnce = true;
            _appliedMode = mode;
            _applyState.SetApplied($"{ModeLabel(mode)} · 전체 {_appliedSubSystems.Count}개 sub-system");

            RefreshRightList();
            UpdateStats(result);
        }

        private static string ModeLabel(ApplyMode mode)
        {
            switch (mode)
            {
                case ApplyMode.Stage:         return "단계별";
                case ApplyMode.Progress:      return "진행상태별";
                default:                      return "시스템 팔레트";
            }
        }

        /// <summary>
        /// 선택 항목만 남김(토글): 선택한 Sub-system의 매칭 항목만 남기고, 데이터가 있는 모든 공종
        /// nwd 파일(MEQ/HYDROPKG/EIT/CABLE) 스코프 안의 나머지(비선택 매칭 + 매칭 안 된 geometry)를
        /// 전부 숨긴다 — 선택에 기계가 없어도 MEQ 파일 전체가 스코프라 비선택 기계가 통째로 숨겨진다.
        /// 데이터 없는 공종(Structure=STR nwd)은 스코프가 아니라 그대로. 색칠과 독립. 다시 누르면 원복.
        /// (EIT EQ 데이터가 있으면 EIT 스코프에 Tray 파일이 포함될 수 있음 — Windows 실측 확인 대상.)
        /// </summary>
        private void BtnKeepOnly_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) { MessageBox.Show("모델을 먼저 열어주세요."); return; }
            if (_hiddenByKeepOnly != null) { RestoreKeepOnlyHidden(doc); _lblStats.Text = "전체 보기로 복원되었습니다."; return; }
            if (_elements.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            if (_selected.Count == 0)
            {
                MessageBox.Show("먼저 남길 Sub-system을 선택하세요 (좌측 목록에서 ▶로 우측에 담기).");
                return;
            }
            if (IndexStale(doc)) BuildIndex();

            var keep = FindItemsFor(_elements.Where(el => _selected.Contains(el.SubSystem)));
            if (keep.Count == 0) { MessageBox.Show("선택한 Sub-system의 매칭 항목이 모델에 없습니다."); return; }
            ApplyScopedHide(doc, keep,
                $"선택 항목만 남김: 선택 {_selected.Count}개 Sub-system만 표시");
        }

        /// <summary>
        /// 매칭 안 된 항목 숨기기(토글): 데이터에 매칭된 요소가 있는 nwd 파일 스코프 안에서
        /// 매칭 요소만 남기고 나머지(매칭 안 된 geometry)를 숨긴다. 선택과 무관 — 전체 매칭분 유지.
        /// 무관한 파일(Structure 등)은 그대로. 다시 누르면 원복.
        /// </summary>
        private void BtnHideUnmatched_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) { MessageBox.Show("모델을 먼저 열어주세요."); return; }
            if (_hiddenByKeepOnly != null) { RestoreKeepOnlyHidden(doc); _lblStats.Text = "전체 보기로 복원되었습니다."; return; }
            if (_elements.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            if (IndexStale(doc)) BuildIndex();

            var keep = FindItemsFor(_elements);   // 전체 매칭분 유지
            if (keep.Count == 0) { MessageBox.Show("매칭된 항목이 없습니다 (가시화 적용/데이터를 확인하세요)."); return; }
            ApplyScopedHide(doc, keep,
                "매칭 안 된 항목 숨김: 스코프 내 데이터 매칭 요소만 표시");
        }

        /// <summary>keep 항목이 속한 공종 파일 스코프 안에서 keep 외를 숨긴다(두 isolate 토글 공용 실행부).</summary>
        private void ApplyScopedHide(Autodesk.Navisworks.Api.Document doc,
            Autodesk.Navisworks.Api.ModelItemCollection keep, string statsMsg)
        {
            // 스코프 트리 순회 + SetHidden은 대형 모델에서 수 초 걸릴 수 있어 진행바로 프리즈 인상 제거.
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "숨김 대상 계산 중…";
            Application.DoEvents();
            Autodesk.Navisworks.Api.ModelItemCollection toHide;
            try
            {
                toHide = ComputeScopedHide(keep);
                if (toHide.Count > 0) doc.Models.SetHidden(toHide, true);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다."); return; }
            _hiddenByKeepOnly = toHide;
            // 숨김 활성 상태에선 두 토글 다 "전체 보기"로 — 어느 쪽을 눌러도 복원(라벨-동작 일치).
            _btnKeepOnly.Text = "전체 보기";
            _btnHideUnmatched.Text = "전체 보기";
            _lblStats.Text = $"{statsMsg} (숨김 {toHide.Count:N0}개 — 다시 누르면 복원)";
        }

        /// <summary>
        /// 숨김 스코프 = 데이터가 있는 공종 nwd 파일 전체 (searcher가 매칭 때 찾은 스코프 루트의 합집합).
        /// keep(선택) 항목이 하나도 없는 공종 파일도 포함 — 그래야 선택 sub-system에 기계가 없어도
        /// MEQ 파일 전체가 스코프에 들어가 비선택 기계가 통째로 숨겨진다. (구 버그: keep 항목이 속한
        /// 파일만 스코프라, 선택 밖 공종 파일·중첩 sub-nwc의 비선택 항목이 안 숨겨졌다.)
        /// 다른 파일(Structure=STR nwd 등 데이터 없는 공종)은 스코프 루트가 아니라 그대로 남는다.
        /// </summary>
        private List<Autodesk.Navisworks.Api.ModelItem> ScopeRootsForHide()
        {
            var roots = new HashSet<Autodesk.Navisworks.Api.ModelItem>();
            foreach (var s in new[] { _eqSearcher, _pipingSearcher, _eitEqSearcher, _cableSearcher })
                if (s.IsIndexBuilt)
                    foreach (var r in s.ScopeRoots) roots.Add(r);
            // 다른 루트의 자손인 루트는 제거 (상위가 이미 커버 — 이중 순회 방지).
            return roots.Where(r =>
            {
                for (var p = r.Parent; p != null; p = p.Parent) if (roots.Contains(p)) return false;
                return true;
            }).ToList();
        }

        /// <summary>
        /// 공종 파일 스코프 안에서 keep 경로 밖 서브트리를 숨김 대상으로 모은다.
        /// keep = 유지할 매칭 항목(선택만 남김=선택분 / 매칭 안된 것 숨김=전체 매칭분).
        /// 스코프 루트를 prune 순회: keep 서브트리는 유지, keep 경로(조상)면 하강, 그 외는 서브트리째 숨김
        /// (geometry는 안 내려감 — 비용은 인덱스 빌드 수준). HashSet&lt;ModelItem&gt; 값 동등성 의존
        /// (ScopeFilter와 동일 — Windows 실측 검증 대상).
        /// </summary>
        private Autodesk.Navisworks.Api.ModelItemCollection ComputeScopedHide(
            Autodesk.Navisworks.Api.ModelItemCollection keep)
        {
            var keepSet = new HashSet<Autodesk.Navisworks.Api.ModelItem>();
            foreach (var mi in keep) keepSet.Add(mi);

            var ancestors = new HashSet<Autodesk.Navisworks.Api.ModelItem>();
            foreach (var mi in keep)
                for (var cur = mi; cur != null; cur = cur.Parent) ancestors.Add(cur);

            var scopeRoots = ScopeRootsForHide();
            if (scopeRoots.Count == 0)
            {
                // 예외적으로 searcher 루트가 없으면 keep 항목의 파일 노드로 폴백 (기존 방식).
                var derived = new HashSet<Autodesk.Navisworks.Api.ModelItem>();
                foreach (var mi in keep)
                {
                    Autodesk.Navisworks.Api.ModelItem fileNode = null, top = mi;
                    for (var cur = mi; cur != null; cur = cur.Parent)
                    { if (fileNode == null && NwdScope.LooksLikeFileNode(cur.DisplayName?.Trim())) fileNode = cur; top = cur; }
                    derived.Add(fileNode ?? top);
                }
                scopeRoots = derived.ToList();
            }

            var toHide = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (var root in scopeRoots) PruneHide(root, keepSet, ancestors, toHide);
            return toHide;
        }

        private static void PruneHide(Autodesk.Navisworks.Api.ModelItem item,
            HashSet<Autodesk.Navisworks.Api.ModelItem> keepSet,
            HashSet<Autodesk.Navisworks.Api.ModelItem> ancestors,
            Autodesk.Navisworks.Api.ModelItemCollection toHide)
        {
            if (keepSet.Contains(item)) return;                 // keep 서브트리 유지
            if (ancestors.Contains(item))                        // keep 경로 — 하강해 형제 가지만 숨김
            {
                foreach (var child in item.Children) PruneHide(child, keepSet, ancestors, toHide);
                return;
            }
            toHide.Add(item);                                    // keep 경로 밖 — 서브트리째 숨김
        }

        /// <summary>isolate 숨김 복원 — 두 토글이 공유. 실패(문서 전환으로 stale 컬렉션 등)해도
        /// 조용히 상태만 정리하고 두 버튼 표기를 원복한다.</summary>
        private void RestoreKeepOnlyHidden(Autodesk.Navisworks.Api.Document doc)
        {
            if (_hiddenByKeepOnly == null) return;
            try
            {
                if (doc != null) doc.Models.SetHidden(_hiddenByKeepOnly, false);
            }
            catch
            {
                // 닫힌/전환된 문서의 숨김은 어차피 소멸 — 참조만 버린다.
            }
            _hiddenByKeepOnly = null;
            if (_btnKeepOnly != null) _btnKeepOnly.Text = "선택 항목만 남김";
            if (_btnHideUnmatched != null) _btnHideUnmatched.Text = "매칭 안 된 항목 숨기기";
        }

        /// <summary>이 탭 가시화 해제: Sub-system 색만 제거 — 다른 공종 색은 유지 (§10 ResetModule) + isolate 복원.</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreKeepOnlyHidden(doc);
            _main.OverrideEngine.ResetModule(doc, VisualModule.SubSystem);
            _lblStats.Text = "이 탭 가시화 해제 완료 (Sub-system 색만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreKeepOnlyHidden(doc);
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
                MessageBox.Show("데이터(OASIS 또는 Excel 형상)를 먼저 로드하세요.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            bool selectedOnly = _selected.Count > 0;
            var names = selectedOnly ? _selectionOrder.ToList() : _subSystemNames.ToList();

            var lines = new List<string>();
            lines.Add("Sub-system 현황 리포트");
            lines.Add($"출력 시각,{DateTime.Now:yyyy-MM-dd HH:mm}");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv(_loadLabel)}");
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
            // 비모달 창이라 여는 동안 데이터가 재로드되면 _bySubSystem가 새 dict로 바뀐다.
            // populate/CSV가 _bySubSystem[name]로 직접 인덱싱하면 KeyNotFoundException → 여는 시점에
            // 요소 리스트를 스냅샷해 창이 자기 데이터로 독립 동작하게 한다.
            var elementsByName = names.ToDictionary(n => n, n => _bySubSystem[n], StringComparer.OrdinalIgnoreCase);

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
            var btnCopyDetail = new Button { Text = "선택항목 클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 0, 8, 0) };
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
                    var ordered = elementsByName[name]
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

            // 상세 창 검색도 debounce — 요소 수천 건 그리드 재생성이 키 입력마다 돌지 않게.
            var detailDebounce = new Debouncer(() => populate(txtSearch.Text.Trim()));
            txtSearch.TextChanged += (s, e) => detailDebounce.Trigger();
            form.Disposed += (s, e) => detailDebounce.Dispose();
            btnCsv.Click += (s, e) => ExportDetailCsv(names, elementsByName, referenceDate);
            btnCopyDetail.Click += (s, e) => ListViewClipboard.CopySelectedOrAll(lv);

            populate("");

            form.Controls.Add(lv);
            form.Controls.Add(top);
            form.Show();  // 비모달 — 창을 열어둔 채 3D 작업 가능
        }

        /// <summary>상세 창의 CSV 출력 — sub-system·공종·요소별 status 전체를 바탕화면에 저장.
        /// 요소는 창 열 때 스냅샷한 elementsByName에서 조회(재로드 후에도 안전).</summary>
        private void ExportDetailCsv(List<string> names,
            Dictionary<string, List<SubSystemElement>> elementsByName, DateTime referenceDate)
        {
            var lines = new List<string>();
            lines.Add("Sub-system 공종·요소별 상세 현황");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv(_loadLabel)}");
            lines.Add($"매칭 기준,{(_appliedOnce ? "가시화 적용 결과" : "미적용 — 매칭 미산정")}");
            lines.Add("");
            lines.Add("Sub-system,Description,MCC계획,공종,요소 ID,설명,현재 단계,진행 상태,매칭");
            foreach (var name in names)
            {
                if (!elementsByName.TryGetValue(name, out var els)) continue;
                var m = GetMaster(name);
                string plan = m?.MccPlan?.ToString("yyyy-MM-dd") ?? "";
                var ordered = els
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

            // [가시화 적용]은 전체에 적용되므로 통계도 전체 기준 (선택은 별도 라인으로 병기).
            linesOut.Add($"전체 Sub-system {_subSystemNames.Count}개 · 요소 {_elements.Count:N0}건 (기준일 {referenceDate:yyyy-MM-dd})");

            // 마스터가 있으면 전체 sub-system 실적 단계 분포 (+ 지연 개수 병기)
            if (_master != null)
            {
                var stageCounts = new Dictionary<SubSystemStage, int>();
                int delayed = 0;
                foreach (var name in _subSystemNames)
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

            if (_paletteMode)
            {
                // Excel 형상: 색은 시스템(앞2자리) 기준. 마스터가 있으면 위 '단계:' 줄이 그 sub-system들의
                // 커미셔닝 단계 분포(마스터 기준)를 함께 보여준다(요소 자체엔 날짜가 없음).
                linesOut.Add($"시스템(앞2자리) {_systemColors.Count}개 색 구분 (형상 보기 — 색은 시스템 기준)");
            }
            else
            {
                int ns = 0, ip = 0, done = 0;
                foreach (var el in _elements)
                {
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }
                linesOut.Add($"요소 진행: 미착수 {ns} · 진행중 {ip} · 완료 {done}");
            }

            // 선택은 색칠 대상이 아니라 상세보기·선택 항목만 남김(isolate)의 대상임을 안내.
            if (_selected.Count > 0)
            {
                int selElems = _selectionOrder.Sum(ElementCount);
                linesOut.Add($"선택 {_selected.Count}개 · 요소 {selElems:N0}건 (상세보기·선택 항목만 남김 대상)");
            }

            if (_appliedOnce)
            {
                string mode = _appliedMode.HasValue ? ModeLabel(_appliedMode.Value) : "";
                linesOut.Add($"매칭 {_matchedIds.Count:N0} / 미매칭 {_unmatchedIds.Count:N0} ({mode} · 전체 적용됨)");
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
