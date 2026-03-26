using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisVisualizer.UI;

namespace NavisVisualizer
{
    internal class NativeWindowHelper : IWin32Window
    {
        public IntPtr Handle
        {
            get { return Autodesk.Navisworks.Api.Application.Gui.MainWindow; }
        }
    }

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
        private static Form _activeForm;

        public override int Execute(params string[] parameters)
        {
            // DockPane 방식 시도
            try
            {
                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins
                    .FindPlugin("NavisVisualizer.DockPane.HDHHI_OE");
                if (pluginRecord != null)
                {
                    if (pluginRecord.IsLoaded)
                    {
                        // 이미 로드됨 — DockPane 표시 토글
                        var dpRecord = pluginRecord as DockPanePluginRecord;
                        if (dpRecord != null)
                        {
                            dpRecord.IsActive = !dpRecord.IsActive;
                            return 0;
                        }
                    }
                    pluginRecord.LoadPlugin();
                    return 0;
                }
            }
            catch { }

            // Fallback: 독립 창으로 표시
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
            _activeForm.Show(new NativeWindowHelper());

            return 0;
        }
    }
}
