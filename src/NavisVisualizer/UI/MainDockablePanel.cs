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
        private HydrotestTab _hydrotestTab;
        private SpoolTab _spoolTab;
        private EquipmentTab _equipmentTab;
        private EitTrayTab _eitTrayTab;
        private CableLineTab _cableLineTab;
        private SubSystemTab _subSystemTab;
        private ToolsTab _toolsTab;

        public MainDockablePanel()
        {
            OverrideEngine = new ColorOverrideEngine(
                SpoolTagSearcher, HydroTagSearcher, ElecTagSearcher,
                EquipmentSearcher, CableLineSearcher);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            _tabControl = new TabControl { Dock = DockStyle.Fill };

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

            _tabControl.TabPages.Add(htPage);
            _tabControl.TabPages.Add(spPage);
            _tabControl.TabPages.Add(eqPage);
            _tabControl.TabPages.Add(eitPage);
            _tabControl.TabPages.Add(cableLinePage);
            _tabControl.TabPages.Add(subSysPage);
            _tabControl.TabPages.Add(toolPage);

            Controls.Add(_tabControl);
        }

        public Document GetDocument() =>
            Autodesk.Navisworks.Api.Application.ActiveDocument;
    }
}
