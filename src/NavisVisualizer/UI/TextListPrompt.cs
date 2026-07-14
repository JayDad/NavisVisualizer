using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 여러 id를 텍스트로 붙여넣어 받는 공용 유틸 — 공백·탭·개행·콤마·세미콜론 어느 구분이든 인식.
    /// Spool '복수 Spool 찾기', Cable 'Cable 찾기'가 공유(엑셀 import 대신 텍스트창).
    /// </summary>
    public static class TextListPrompt
    {
        /// <summary>구분자(공백/탭/개행/콤마/세미콜론)로 쪼개 대소문자 무시 id 셋으로 (빈 토큰 제거).</summary>
        public static HashSet<string> Parse(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(text))
                foreach (var tok in text.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = tok.Trim();
                    if (t.Length > 0) set.Add(t);
                }
            return set;
        }

        /// <summary>멀티라인 입력 모달. [적용]이면 입력 원문, [취소]/닫기면 null.</summary>
        public static string Prompt(IWin32Window owner, string title, string prompt, string prefill)
        {
            using (var form = new Form
            {
                Text = title,
                Width = 460,
                Height = 380,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
            })
            {
                var lbl = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 46,
                    Padding = new Padding(8, 6, 8, 0),
                    Text = prompt,
                };
                var txt = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    AcceptsReturn = true,   // 멀티라인이라 Enter는 줄바꿈(AcceptButton 안 탐)
                    WordWrap = false,
                    Font = new Font("Consolas", 9F),
                    Text = prefill ?? "",
                };
                var pnl = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                var ok = new Button { Text = "적용", Width = 90, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
                pnl.Controls.Add(ok);
                pnl.Controls.Add(cancel);
                form.Controls.Add(txt);   // Fill 먼저 Add 후 Top/Bottom (도킹 순서)
                form.Controls.Add(pnl);
                form.Controls.Add(lbl);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
            }
        }
    }
}
