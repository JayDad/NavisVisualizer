using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisVisualizer.UI;

namespace NavisVisualizer
{
    [Plugin("NavisVisualizer", "HDHHI_OE",
        DisplayName = "Navis Visualizer",
        ToolTip = "Hydrotest / Spool Visualizer")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class VisualizerEntryPlugin : AddInPlugin
    {
        private static Form _activeForm;

        public override int Execute(params string[] parameters)
        {
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
                StartPosition = FormStartPosition.CenterScreen
            };
            panel.Dock = DockStyle.Fill;
            _activeForm.Controls.Add(panel);
            _activeForm.Show();

            return 0;
        }
    }
}
