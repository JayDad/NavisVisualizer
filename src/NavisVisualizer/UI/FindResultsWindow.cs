using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// '복수 Spool 찾기' / 'Cable 찾기' 매칭 결과를 띄우는 공용 비모달 창.
    /// 행을 선택하면 <c>onFocus(선택 id들)</c>이 호출돼 3D에서 선택·포커스한다
    /// (실제 3D 조회는 콜백이 문서를 그때그때 다시 얻어 수행 — 창이 문서를 붙들지 않음).
    /// 미매칭(모델에 없음) 행은 회색+X로 표시하고 포커스 대상에서 제외한다.
    /// </summary>
    public static class FindResultsWindow
    {
        public sealed class Row
        {
            public string Id;
            public string Stage;   // 표시용 단계 라벨 (없으면 "-")
            public bool Matched;   // 모델에서 찾음(O) / 데이터엔 있으나 모델엔 없음(X)
        }

        /// <summary>
        /// 비모달 결과 창을 띄우고 Form을 반환한다. 호출부가 반환값을 보관해 재호출 시
        /// 이전 창을 <c>Close()</c>하면 창이 쌓이지 않는다.
        /// </summary>
        public static Form Show(IWin32Window owner, string title, string idHeader,
            IReadOnlyList<Row> rows, Action<IReadOnlyList<string>> onFocus, string note = null)
        {
            var form = new Form
            {
                Text = title,
                Width = 460,
                Height = 560,
                // 비모달 Show()에선 CenterParent가 동작 안 함(Owner가 Form이 아니라 UserControl) →
                // 화면 중앙(SubSystemTab 상세 창과 동일).
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = true,
            };

            int matched = rows.Count(r => r.Matched);
            var lblSummary = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 6, 8, 0),
                Text = $"매칭 {matched:N0} · 미매칭 {rows.Count - matched:N0}"
                     + (string.IsNullOrEmpty(note) ? "" : " · " + note)
                     + "\n행을 선택하면 3D에서 포커스/하이라이트합니다 (미매칭 행은 제외).",
            };

            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
            };
            lv.Columns.Add(idHeader, 210);
            lv.Columns.Add("현재 단계", 120);
            lv.Columns.Add("매칭", 60);

            // 매칭 우선 정렬(찾은 것부터) → id
            foreach (var r in rows.OrderByDescending(r => r.Matched)
                                  .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(r.Id ?? "") { UseItemStyleForSubItems = false, Tag = r };
                item.SubItems.Add(string.IsNullOrEmpty(r.Stage) ? "-" : r.Stage);
                var ms = item.SubItems.Add(r.Matched ? "O" : "X");
                if (!r.Matched) { ms.ForeColor = Color.Red; item.ForeColor = Color.Gray; }
                lv.Items.Add(item);
            }

            // "선택하면 focus/하일라이트" — 선택 변경 시 매칭된 행 id들로 3D 포커스.
            // 단일 클릭도 SelectedIndexChanged가 2번(옛 해제+새 선택) 뜨므로, 직전과 동일한
            // id 집합이면 카메라 재이동을 생략한다(중복 포커스 방지).
            List<string> lastIds = null;
            lv.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    var ids = new List<string>();
                    foreach (ListViewItem it in lv.SelectedItems)
                        if (it.Tag is Row r && r.Matched && !string.IsNullOrEmpty(r.Id)) ids.Add(r.Id);
                    if (ids.Count == 0) return;
                    if (lastIds != null && lastIds.Count == ids.Count && !ids.Except(lastIds).Any()) return;
                    lastIds = ids;
                    onFocus(ids);
                }
                catch { /* 문서 닫힘·창 teardown 등 — 창은 유지 */ }
            };

            ListViewClipboard.EnableCtrlC(lv);   // Ctrl+C 복사

            form.Controls.Add(lv);          // Fill 먼저 Add 후 Top (도킹 순서)
            form.Controls.Add(lblSummary);
            form.Show(owner);
            return form;
        }
    }
}
