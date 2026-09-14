using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisVisualizer.UI;

namespace NavisVisualizer
{
    [Plugin("NavisVisualizer.DockPane", "HDHHI_OE",
        DisplayName = "Navis Visualizer")]
    [DockPanePlugin(320, 820, FixedSize = false)]
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
                Height = 870,
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

    /// <summary>
    /// 일괄 갱신(배치) 진입점 (CLAUDE.md §21). 도크 패널 없이 BatchPanel을 모달 창으로 띄운다.
    /// Automation 러너(tools/NavisBatch)가 <c>ExecuteAddInPlugin("NavisVisualizer.Batch.HDHHI_OE", …)</c>로
    /// 호출하며, Navisworks Add-ins 리본에서도 직접 실행할 수 있다.
    /// 인자: "auto" = 기억된 선택으로 즉시 실행, "close" = 실행 후 창 닫기. 반환 = 저장 실패 job 수.
    /// </summary>
    [Plugin("NavisVisualizer.Batch", "HDHHI_OE",
        DisplayName = "Navis Visualizer 일괄 갱신",
        ToolTip = "등록된 모델을 열어 OASIS 최신 실적으로 색칠 후 다른 이름으로 저장")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class BatchEntryPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                return BatchPanel.ShowStandalone(parameters);
            }
            catch (Exception ex)
            {
                // 무인 실행에서도 원인이 남도록 — 창이 뜨기 전 예외는 화면에 보일 곳이 없다.
                NavisVisualizer.Services.ErrorLog.Append("배치 진입점", ex);
                MessageBox.Show("일괄 갱신 창을 열지 못했습니다.\n" + ex.Message, "Navis Visualizer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }
        }
    }
}
