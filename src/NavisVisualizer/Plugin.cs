using System;
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
        public override Control CreateControlPane()
        {
            return new MainDockablePanel();
        }

        public override void DestroyControlPane(Control pane)
        {
            pane.Dispose();
        }
    }

    [Plugin("NavisVisualizer", "HDHHI_OE",
        DisplayName = "Navis Visualizer",
        ToolTip = "Hydrotest / Spool Visualizer")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class VisualizerEntryPlugin : AddInPlugin
    {
        private static Form _activeForm;

        public override int Execute(params string[] parameters)
        {
            // DockPane attempt
            try
            {
                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins
                    .FindPlugin("NavisVisualizer.DockPane.HDHHI_OE");

                if (pluginRecord != null)
                {
                    if (!pluginRecord.IsLoaded)
                        pluginRecord.LoadPlugin();

                    var dockPane = pluginRecord.LoadedPlugin as DockPanePlugin;
                    if (dockPane != null)
                    {
                        dockPane.ActivatePane();
                        return 0;
                    }
                }
            }
            catch { }

            // Fallback: Form
            if (_activeForm != null && !_activeForm.IsDisposed)
            {
                _activeForm.BringToFront();
                return 0;
            }

            var panel = new MainDockablePanel();
            _activeForm = new Form
            {
                Text = "Navis Visualizer",
                Width = 380,
                Height = 750,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true
            };
            panel.Dock = DockStyle.Fill;
            _activeForm.Controls.Add(panel);
            _activeForm.Show();

            return 0;
        }
    }
}
