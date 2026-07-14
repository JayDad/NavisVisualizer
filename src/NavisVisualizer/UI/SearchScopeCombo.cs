using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 검색 범위 드롭다운 — 검색창 우측에 두고 "전체" + 리스트 각 열을 나열한다.
    /// "전체"(SelectedColumn = -1) = 기존처럼 전 필드에서 매칭, 특정 열 선택 시 그 열만 검색/필터.
    /// 실제 판정은 각 탭의 FilterList가 SelectedColumn으로 분기(탭마다 열→값 접근이 달라
    /// 공용 컴포넌트는 UI/선택 상태만 담당). 전 리스트 탭 공통.
    /// </summary>
    public class SearchScopeCombo : ComboBox
    {
        // Items와 평행 — 각 항목이 가리키는 실제 ListView 열 인덱스. "전체"는 -1.
        private readonly List<int> _columnIndices = new List<int>();

        public SearchScopeCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;   // 자유 입력 금지 — 목록에서만 선택
            Width = 96;
        }

        /// <summary>
        /// 리스트 열 헤더로 항목을 채운다. "전체"가 먼저, 이어 각 열(헤더가 <paramref name="skipHeaders"/>에
        /// 있으면 제외 — 보통 자동 행번호 "#"). 열이 추가된 뒤(빌드 마지막)에 호출할 것.
        /// </summary>
        public void Populate(ListView listView, params string[] skipHeaders)
        {
            var skip = new HashSet<string>(skipHeaders ?? new string[0], StringComparer.OrdinalIgnoreCase);
            BeginUpdate();
            Items.Clear();
            _columnIndices.Clear();
            Items.Add("전체");
            _columnIndices.Add(-1);
            for (int i = 0; i < listView.Columns.Count; i++)
            {
                string header = listView.Columns[i].Text ?? "";
                if (skip.Contains(header)) continue;
                Items.Add(header);
                _columnIndices.Add(i);
            }
            SelectedIndex = 0;   // 기본 "전체"
            EndUpdate();
        }

        /// <summary>선택된 ListView 열 인덱스. "전체" 또는 미선택이면 -1.</summary>
        public int SelectedColumn =>
            SelectedIndex >= 0 && SelectedIndex < _columnIndices.Count ? _columnIndices[SelectedIndex] : -1;
    }
}
