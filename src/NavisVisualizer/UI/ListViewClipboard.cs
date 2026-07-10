using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// ListView 행을 클립보드로 복사하는 공용 헬퍼. WinForms ListView는 기본적으로
    /// Ctrl+C를 지원하지 않으므로 각 탭이 이 헬퍼를 KeyDown / [클립보드 복사] 버튼에
    /// 배선한다. 선택 행(없으면 표시 중인 전체 행)을 헤더 포함 탭 구분 텍스트로 복사 →
    /// Excel에 그대로 붙여넣으면 열이 나뉜다.
    /// </summary>
    public static class ListViewClipboard
    {
        /// <summary>
        /// 선택 행(없으면 전체 표시 행)을 헤더 포함 탭 구분 텍스트로 복사.
        /// 복사한 행 수를 반환한다(0 = 복사 안 함 / 빈 리스트 / 실패).
        /// </summary>
        public static int CopySelectedOrAll(ListView lv)
        {
            if (lv == null || lv.Items.Count == 0) return 0;

            var rows = lv.SelectedItems.Count > 0
                ? lv.SelectedItems.Cast<ListViewItem>()
                : lv.Items.Cast<ListViewItem>();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", lv.Columns.Cast<ColumnHeader>().Select(c => c.Text)));
            int count = 0;
            foreach (var item in rows)
            {
                sb.AppendLine(string.Join("\t",
                    item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(si => si.Text)));
                count++;
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                return count;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"클립보드 복사 실패:\n{ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
        }

        /// <summary>ListView에 Ctrl+C 복사를 배선한다(KeyDown). onCopied = 복사 후 콜백(행 수).</summary>
        public static void EnableCtrlC(ListView lv, Action<int> onCopied = null)
        {
            lv.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    int n = CopySelectedOrAll(lv);
                    onCopied?.Invoke(n);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }
    }
}
