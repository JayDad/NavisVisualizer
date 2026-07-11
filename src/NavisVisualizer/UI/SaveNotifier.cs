using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 파일 저장 완료 알림 (UX audit P2) — 모달 MessageBox 대신 비모달 소형 창으로
    /// 경로를 보여주고 [파일 열기]/[폴더 열기]를 제공한다. 저장 후 바로 결과물을 여는
    /// 동선을 한 클릭으로 줄이고, 작업 흐름을 끊지 않는다(비모달). 오류·확인이 반드시
    /// 필요한 경우에만 MessageBox를 유지한다.
    /// </summary>
    public static class SaveNotifier
    {
        /// <param name="ownerControl">알림의 소유 폼을 찾을 기준 컨트롤 (탭 자신).</param>
        /// <param name="title">창 제목 (어떤 출력인지 — 예: "매칭 Status 엑셀 출력").</param>
        /// <param name="filePath">저장된 파일 전체 경로.</param>
        /// <param name="extraInfo">부가 안내 (예: "작성 후 .xlsx로 저장해 Import 하세요"). null이면 생략.</param>
        public static void ShowSaved(Control ownerControl, string title, string filePath, string extraInfo = null)
        {
            try
            {
                var form = BuildForm(title, filePath, extraInfo);
                var owner = ownerControl?.FindForm();
                if (owner != null) form.Show(owner);
                else form.Show();
            }
            catch
            {
                // 알림 창 생성이 실패해도 저장 자체는 끝났다 — 기존 방식으로 경로만 전달.
                MessageBox.Show($"저장 완료: {filePath}" +
                    (string.IsNullOrEmpty(extraInfo) ? "" : $"\n{extraInfo}"));
            }
        }

        private static Form BuildForm(string title, string filePath, string extraInfo)
        {
            var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 12, 14, 10),
            };

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };

            layout.Controls.Add(new Label
            {
                Text = $"✓ {Path.GetFileName(filePath)} 저장 완료",
                AutoSize = true,
                Font = new Font(form.Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
            });
            layout.Controls.Add(new Label
            {
                Text = filePath,
                AutoSize = true,
                MaximumSize = new Size(440, 0),   // 긴 경로 줄바꿈
                ForeColor = Color.Gray,
                Margin = new Padding(0, 0, 0, 4),
            });
            if (!string.IsNullOrEmpty(extraInfo))
            {
                layout.Controls.Add(new Label
                {
                    Text = extraInfo,
                    AutoSize = true,
                    MaximumSize = new Size(440, 0),
                    Margin = new Padding(0, 0, 0, 4),
                });
            }

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
            };
            var btnOpen = new Button { Text = "파일 열기", AutoSize = true };
            btnOpen.Click += (s, e) => TryStart(filePath, null);
            var btnFolder = new Button { Text = "폴더 열기", AutoSize = true };
            btnFolder.Click += (s, e) => TryStart("explorer.exe", $"/select,\"{filePath}\"");
            var btnClose = new Button { Text = "닫기", AutoSize = true };
            btnClose.Click += (s, e) => form.Close();
            buttons.Controls.Add(btnOpen);
            buttons.Controls.Add(btnFolder);
            buttons.Controls.Add(btnClose);
            layout.Controls.Add(buttons);

            form.CancelButton = btnClose;   // ESC로 닫기
            form.Controls.Add(layout);
            return form;
        }

        private static void TryStart(string fileName, string arguments)
        {
            try
            {
                if (arguments == null) Process.Start(fileName);
                else Process.Start(fileName, arguments);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"열기 실패: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
