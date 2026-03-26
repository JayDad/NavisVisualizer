using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisVisualizer.UI;

namespace NavisVisualizer
{
    [Plugin("NavisVisualizer.DockPane", "HDHHI_OE",
        DisplayName = "Navis Visualizer")]
    [DockPanePlugin(320, 700, FixedSize = false)]
    public class MainDockPanePlugin : DockPanePlugin
    {
        private MainDockablePanel _panel;

        public override Control CreateControlPane()
        {
            _panel = new MainDockablePanel();
            return _panel;
        }

        public override void DestroyControlPane(Control pane)
        {
            pane.Dispose();
        }
    }

    [Plugin("NavisVisualizer", "HDHHI_OE",
        DisplayName = "Navis Visualizer 열기",
        ToolTip = "Hydrotest / Spool 시각화 패널 열기")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class VisualizerEntryPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins
                    .FindPlugin("NavisVisualizer.DockPane.HDHHI_OE");
                pluginRecord?.LoadPlugin();
            }
            catch
            {
                var panel = new MainDockablePanel();
                var form = new Form
                {
                    Text = "Navis Visualizer",
                    Width = 360,
                    Height = 750,
                    FormBorderStyle = FormBorderStyle.SizableToolWindow
                };
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
            }

            return 0;
        }
    }
}
