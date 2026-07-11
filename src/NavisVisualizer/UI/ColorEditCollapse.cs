using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 단계 색상 패널의 편집 컨트롤 접기 (UX audit P1). 일상 업무 흐름(로드 → 확인 →
    /// 가시화)에서 색상 편집은 드물게 쓰이므로, 기본은 체크박스+색 스와치만 보이고
    /// 편집 컨트롤(▼ 색상 버튼, 투명도 콤보)은 토글로 펼친다.
    ///
    /// 구현: 각 탭의 색상 패널 빌더를 고치지 않고, 패널 트리를 걸어 ComboBox와
    /// "▼" 버튼만 Visible 토글 — 탭마다 다른 빌더 구조(_colorRows 저장 여부 등)에
    /// 무관하게 동작한다. TableLayoutPanel 열 폭은 절대값이라 숨겨도 레이아웃이
    /// 흔들리지 않는다 (빈 칸만 남음).
    /// </summary>
    public static class ColorEditCollapse
    {
        /// <summary>
        /// 색상 패널들 아래 배치할 토글 행을 만든다. 기본 접힘 상태로 시작한다.
        /// </summary>
        public static Control BuildToggleRow(params Control[] colorPanels)
        {
            const string collapsedText = "색상·투명도 편집 펼치기 ▾";
            const string expandedText = "색상·투명도 편집 접기 ▴";

            var link = new LinkLabel
            {
                Text = collapsedText,
                Dock = DockStyle.Fill,
                Height = 18,
                AutoSize = false,
                LinkBehavior = LinkBehavior.HoverUnderline,
            };

            bool expanded = false;
            SetEditControlsVisible(colorPanels, false);

            link.LinkClicked += (s, e) =>
            {
                expanded = !expanded;
                SetEditControlsVisible(colorPanels, expanded);
                link.Text = expanded ? expandedText : collapsedText;
            };
            return link;
        }

        private static void SetEditControlsVisible(Control[] roots, bool visible)
        {
            foreach (var root in roots)
                if (root != null) Walk(root, visible);
        }

        private static void Walk(Control parent, bool visible)
        {
            foreach (Control child in parent.Controls)
            {
                bool isColorButton = child is Button b && b.Text == "▼";
                if (child is ComboBox || isColorButton)
                    child.Visible = visible;
                else
                    Walk(child, visible);
            }
        }
    }
}
