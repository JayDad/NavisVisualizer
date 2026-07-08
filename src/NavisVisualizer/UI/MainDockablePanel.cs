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

        /// <summary>EIT Tray 전용 — 스코프 EIT.</summary>
        public ModelItemSearcher ElecTagSearcher { get; } = new ModelItemSearcher();

        /// <summary>Sub-system 전용 — Equipment TAG + Piping PKG를 모두 찾아야 하므로 스코프 MEQ·SPL·HYDROPKG.</summary>
        public ModelItemSearcher SubSystemSearcher { get; } = new ModelItemSearcher();

        /// <summary>Dedicated level-targeted index for Equipment (different match strategy) — 스코프 MEQ.</summary>
        public ModelItemSearcher EquipmentSearcher { get; } = new ModelItemSearcher();

        /// <summary>Dedicated index for Cable Pull boxes (key = prefix before "-BOX") — 스코프 CABLE.</summary>
        public ModelItemSearcher CableBoxSearcher { get; } = new ModelItemSearcher();

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
        private CableTab _cableTab;
        private SubSystemTab _subSystemTab;
        private ToolsTab _toolsTab;

        public MainDockablePanel()
        {
            OverrideEngine = new ColorOverrideEngine(
                SpoolTagSearcher, HydroTagSearcher, ElecTagSearcher, SubSystemSearcher,
                EquipmentSearcher, CableBoxSearcher);
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

            var cablePage = new TabPage("Cable Pull");
            _cableTab = new CableTab(this);
            _cableTab.Dock = DockStyle.Fill;
            cablePage.Controls.Add(_cableTab);

            var subSysPage = new TabPage("Sub-system");
            _subSystemTab = new SubSystemTab(this);
            _subSystemTab.Dock = DockStyle.Fill;
            subSysPage.Controls.Add(_subSystemTab);

            var toolPage = new TabPage("Tools");
            _toolsTab = new ToolsTab(this);
            _toolsTab.Dock = DockStyle.Fill;
            toolPage.Controls.Add(_toolsTab);

            _tabControl.TabPages.Add(htPage);
            _tabControl.TabPages.Add(spPage);
            _tabControl.TabPages.Add(eqPage);
            _tabControl.TabPages.Add(eitPage);
            _tabControl.TabPages.Add(cablePage);
            _tabControl.TabPages.Add(subSysPage);
            _tabControl.TabPages.Add(toolPage);

            Controls.Add(_tabControl);
        }

        public Document GetDocument() =>
            Autodesk.Navisworks.Api.Application.ActiveDocument;
    }
}
