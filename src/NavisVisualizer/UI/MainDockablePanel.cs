using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    public class MainDockablePanel : UserControl
    {
        public ModelItemSearcher Searcher { get; } = new ModelItemSearcher();
        public ColorOverrideEngine OverrideEngine { get; }
        public ExportService ExportSvc { get; } = new ExportService();
        public UserDataService UserDataSvc { get; } = new UserDataService();

        private TabControl _tabControl;
        private HydrotestTab _hydrotestTab;
        private SpoolTab _spoolTab;
        private ToolsTab _toolsTab;

        public MainDockablePanel()
        {
            OverrideEngine = new ColorOverrideEngine(Searcher);
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

            var toolPage = new TabPage("Tools");
            _toolsTab = new ToolsTab(this);
            _toolsTab.Dock = DockStyle.Fill;
            toolPage.Controls.Add(_toolsTab);

            _tabControl.TabPages.Add(htPage);
            _tabControl.TabPages.Add(spPage);
            _tabControl.TabPages.Add(toolPage);

            Controls.Add(_tabControl);
        }

        public Document GetDocument() =>
            Autodesk.Navisworks.Api.Application.ActiveDocument;
    }
}
