using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class MainDockablePanel : UserControl
    {
        // Tag 인덱스는 NWD 파일 스코프(NwdScope)별로 분리 — 공유하면 스코프가 다른 탭 간
        // 재빌드 핑퐁이 생기고, 단일 스코프로는 최적화가 안 되기 때문 (CLAUDE.md 1번).
        // 매칭 전략(digit 포함 DisplayName full-walk)은 인스턴스 전부 동일하다.

        /// <summary>Spool 전용 — 스코프 SPL, SPL 파일이 없으면 HYDROPKG로 체인 fallback (스풀이 그 안에 있음).</summary>
        public ModelItemSearcher SpoolTagSearcher { get; } = new ModelItemSearcher();

        /// <summary>Hydrotest 전용 — 스코프 HYDROPKG.</summary>
        public ModelItemSearcher HydroTagSearcher { get; } = new ModelItemSearcher();

        /// <summary>EIT Tray 전용 — 스코프 EIT (레벨 타겟, 트레이 ID 셋 기반).</summary>
        public ModelItemSearcher ElecTagSearcher { get; } = new ModelItemSearcher();

        /// <summary>Dedicated level-targeted index for Equipment (different match strategy) — 스코프 MEQ.</summary>
        public ModelItemSearcher EquipmentSearcher { get; } = new ModelItemSearcher();

        /// <summary>Cable node box 인덱스 (key = "-BOX" 앞 접두, 스코프 CABLE) — Tools 탭
        /// box 중복 검사 전용 (구 Cable(Node) 탭은 2026-07 삭제됨).</summary>
        public ModelItemSearcher CableBoxSearcher { get; } = new ModelItemSearcher();

        /// <summary>Cable(형상) 탭 — cable-no를 컴포넌트에 직접 매칭 (레벨 타겟, 스코프 CABLE).
        /// CableBoxSearcher와 매칭 전략이 달라(box 접두 vs cable-no 정확 일치) 별도 인스턴스.</summary>
        public ModelItemSearcher CableLineSearcher { get; } = new ModelItemSearcher();

        // Sub-system 탭의 공종별 매칭 searcher(Equipment/Piping/EIT EQ/Cable)는 그 탭이
        // 사유(私有)한다 — 각자 자기 nwd 하나만 레벨 타겟이라 다른 탭과 공유 불가(§11).

        public ColorOverrideEngine OverrideEngine { get; }
        public ExportService ExportSvc { get; } = new ExportService();
        public UserDataService UserDataSvc { get; } = new UserDataService();

        /// <summary>Reads active section/clip planes for the "보이는 것만" visibility filter.</summary>
        public SectionService SectionSvc { get; } = new SectionService();

        private TabControl _tabControl;
        private OverviewTab _overviewTab;
        private HydrotestTab _hydrotestTab;
        private SpoolTab _spoolTab;
        private EquipmentTab _equipmentTab;
        private EitTrayTab _eitTrayTab;
        private CableLineTab _cableLineTab;
        private SubSystemTab _subSystemTab;
        private ToolsTab _toolsTab;

        /// <summary>
        /// 문서 이벤트(문서 전환·같은 파일 재로드)로 모든 인덱스가 무효화됐을 때 발생.
        /// 자기 searcher/캐시를 사유(私有)하는 탭(SubSystemTab의 공종별 4개, CableLineTab의
        /// clash 형상 캐시)이 구독해 함께 리셋한다 — 지문 비교(DocumentFingerprint)로는
        /// "같은 파일 재로드"를 못 잡기 때문(2차 audit P1: 오래된 ModelItem 캐시 재사용 위험).
        /// </summary>
        public event Action IndexesInvalidated;

        // FileNameChanged 구독 중인 문서 (활성 문서 전환 시 재배선)
        private Document _hookedDoc;

        public MainDockablePanel()
        {
            OverrideEngine = new ColorOverrideEngine(
                SpoolTagSearcher, HydroTagSearcher, ElecTagSearcher,
                EquipmentSearcher, CableLineSearcher);
            InitializeComponent();
            HookDocumentEvents();
        }

        /// <summary>
        /// 문서 전환/재로드 시 인덱스 자동 무효화 배선. Navisworks는 같은 Document 인스턴스를
        /// 재사용하며 파일을 갈아끼우므로, 지문이 같아지는 "같은 파일 다시 열기"는 이벤트로만
        /// 잡을 수 있다. 이벤트 미지원 환경(Automation 등)이어도 지문 비교(NeedsRebuild)는
        /// 그대로 동작하므로 구독 실패는 조용히 무시한다.
        /// </summary>
        private void HookDocumentEvents()
        {
            try
            {
                Autodesk.Navisworks.Api.Application.ActiveDocumentChanged += OnActiveDocumentChanged;
                HookFileNameChanged(GetDocument());
            }
            catch { /* 이벤트 미지원 환경 — 지문 비교만으로 동작 */ }
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            try { HookFileNameChanged(GetDocument()); } catch { }
            InvalidateIndexes();
        }

        private void HookFileNameChanged(Document doc)
        {
            if (_hookedDoc == doc) return;
            if (_hookedDoc != null)
            {
                try { _hookedDoc.FileNameChanged -= OnDocFileNameChanged; } catch { }
            }
            _hookedDoc = doc;
            if (doc != null)
            {
                try { doc.FileNameChanged += OnDocFileNameChanged; } catch { }
            }
        }

        private void OnDocFileNameChanged(object sender, EventArgs e) => InvalidateIndexes();

        /// <summary>패널 소유 searcher 전부 리셋 + 사유 캐시 보유 탭들에 통지.</summary>
        private void InvalidateIndexes()
        {
            SpoolTagSearcher.Reset();
            HydroTagSearcher.Reset();
            ElecTagSearcher.Reset();
            EquipmentSearcher.Reset();
            CableBoxSearcher.Reset();
            CableLineSearcher.Reset();
            IndexesInvalidated?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { Autodesk.Navisworks.Api.Application.ActiveDocumentChanged -= OnActiveDocumentChanged; } catch { }
                HookFileNameChanged(null);
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // 첫 화면 = Overview (UX audit P1): 공종별 데이터/인덱스/3D 적용 상태 + NWD
            // 파일명 규약 preflight를 한 표로 — 각 탭을 열어보지 않아도 현재 상태를 알 수 있다.
            var ovPage = new TabPage("Overview");
            _overviewTab = new OverviewTab(this);
            _overviewTab.Dock = DockStyle.Fill;
            ovPage.Controls.Add(_overviewTab);

            var htPage = new TabPage("Hydrotest");
            _hydrotestTab = new HydrotestTab(this);
            _hydrotestTab.Dock = DockStyle.Fill;
            htPage.Controls.Add(_hydrotestTab);

            var spPage = new TabPage("Spool");
            _spoolTab = new SpoolTab(this);
            _spoolTab.Dock = DockStyle.Fill;
            spPage.Controls.Add(_spoolTab);

            var eqPage = new TabPage("Equipment");
            _equipmentTab = new EquipmentTab(this);
            _equipmentTab.Dock = DockStyle.Fill;
            eqPage.Controls.Add(_equipmentTab);

            var eitPage = new TabPage("EIT Tray");
            _eitTrayTab = new EitTrayTab(this);
            _eitTrayTab.Dock = DockStyle.Fill;
            eitPage.Controls.Add(_eitTrayTab);

            // 형상 탭 — 07_Trion_All_Cable.nwd의 cable-no 컴포넌트를 직접 매칭·하이라이트.
            // (구 노드/박스 집계 탭 Cable(Node)은 2026-07 사용자 결정으로 삭제 — DB에 노드
            //  route가 없어 존재 의의가 사라짐. Tools 탭 box 중복 검사는 유지.)
            var cableLinePage = new TabPage("Cable");
            _cableLineTab = new CableLineTab(this);
            _cableLineTab.Dock = DockStyle.Fill;
            cableLinePage.Controls.Add(_cableLineTab);

            var subSysPage = new TabPage("Sub-system");
            _subSystemTab = new SubSystemTab(this);
            _subSystemTab.Dock = DockStyle.Fill;
            subSysPage.Controls.Add(_subSystemTab);

            // 개발·진단 전용 기능 모음 — 일반 업무 탭과 구분되도록 명칭으로 표시 (UX audit P2).
            var toolPage = new TabPage("고급 진단");
            _toolsTab = new ToolsTab(this);
            _toolsTab.Dock = DockStyle.Fill;
            toolPage.Controls.Add(_toolsTab);

            _tabControl.TabPages.Add(ovPage);
            _tabControl.TabPages.Add(htPage);
            _tabControl.TabPages.Add(spPage);
            _tabControl.TabPages.Add(eqPage);
            _tabControl.TabPages.Add(eitPage);
            _tabControl.TabPages.Add(cableLinePage);
            _tabControl.TabPages.Add(subSysPage);
            _tabControl.TabPages.Add(toolPage);

            // Overview 상태 조회 대상 + 행 더블클릭 이동 배선. 상태는 캐시하지 않으므로
            // Overview 탭이 선택될 때마다 자동 재조회한다 (숨김/문서 전환 이벤트 없이도 최신).
            _overviewTab.Configure(_tabControl, new (string, IOverviewSource, TabPage)[]
            {
                ("Hydrotest",  _hydrotestTab, htPage),
                ("Spool",      _spoolTab,     spPage),
                ("Equipment",  _equipmentTab, eqPage),
                ("EIT Tray",   _eitTrayTab,   eitPage),
                ("Cable",      _cableLineTab, cableLinePage),
                ("Sub-system", _subSystemTab, subSysPage),
            });
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (_tabControl.SelectedTab == ovPage)
                    _overviewTab.RefreshOverview();
            };

            Controls.Add(_tabControl);
        }

        public Document GetDocument() =>
            Autodesk.Navisworks.Api.Application.ActiveDocument;
    }
}
