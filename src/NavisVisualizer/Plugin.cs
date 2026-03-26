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
        public override int Execute(params string[] parameters)
        {
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

            return 0;
        }
    }
}
