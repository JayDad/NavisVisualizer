using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class MainDockablePanel : UserControl
    {
        /// <summary>
        /// Shared full-walk index for Spool / Hydrotest / EIT Tray.
        /// All three match by "digit-containing DisplayName" in the same tree traversal pattern.
        /// </summary>
        public ModelItemSearcher TagSearcher { get; } = new ModelItemSearcher();

        /// <summary>Dedicated level-targeted index for Equipment (different match strategy).</summary>
        public ModelItemSearcher EquipmentSearcher { get; } = new ModelItemSearcher();

        /// <summary>Dedicated index for Cable Pull boxes (key = prefix before "-BOX").</summary>
        public ModelItemSearcher CableBoxSearcher { get; } = new ModelItemSearcher();

        public ColorOverrideEngine OverrideEngine { get; }
        public ExportService ExportSvc { get; } = new ExportService();
        public UserDataService UserDataSvc { get; } = new UserDataService();

        private TabControl _tabControl;
        private HydrotestTab _hydrotestTab;
        private SpoolTab _spoolTab;
        private EquipmentTab _equipmentTab;
        private EitTrayTab _eitTrayTab;
        private CableTab _cableTab;
        private ToolsTab _toolsTab;

        public MainDockablePanel()
        {
            OverrideEngine = new ColorOverrideEngine(TagSearcher, EquipmentSearcher, CableBoxSearcher);
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

            var toolPage = new TabPage("Tools");
            _toolsTab = new ToolsTab(this);
            _toolsTab.Dock = DockStyle.Fill;
            toolPage.Controls.Add(_toolsTab);

            _tabControl.TabPages.Add(htPage);
            _tabControl.TabPages.Add(spPage);
            _tabControl.TabPages.Add(eqPage);
            _tabControl.TabPages.Add(eitPage);
            _tabControl.TabPages.Add(cablePage);
            _tabControl.TabPages.Add(toolPage);

            Controls.Add(_tabControl);
        }

        public Document GetDocument() =>
            Autodesk.Navisworks.Api.Application.ActiveDocument;
    }
}
