using System;
using System.Collections.Generic;

namespace NavisVisualizer.Searchers
{
    /// <summary>
    /// 태그(스풀/PKG/장비/트레이/케이블 번호) 인덱스 키 정규화 — 모델 DisplayName과 실적 데이터 ID가
    /// 장식 차이로 어긋나는 것을 한 곳에서 흡수한다 (2026-09, Ruya Hydrotest 실측 대응).
    ///
    /// 실측된 장식:
    ///   Trion: "/TAG" (선행 슬래시), "TAG/suffix" (태그 뒤 접미)
    ///   Ruya : "/GPSET/U001-DO-102-K" (PDMS 경로 접두 — 태그는 **마지막 세그먼트**),
    ///          "(C01B-DO-030254-1)" (괄호 감쌈)
    ///
    /// 규칙: 인덱스는 <see cref="Candidates"/>가 주는 후보 키 전부로 등록하고, 조회는 <see cref="Normalize"/>
    /// 한 값으로 한다. 실적 파일에 "/GPSET/…"를 붙여도, 안 붙여도 같은 노드를 찾는다.
    /// Autodesk 비의존 — 단위 테스트(TagKeyTests) 대상.
    /// </summary>
    public static class TagKey
    {
        /// <summary>조회용 정규화: 공백/선행 '/' 제거, 바깥 괄호 제거, 대문자.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().TrimStart('/').Trim();
            s = StripParens(s);
            return s.ToUpperInvariant();
        }

        /// <summary>
        /// 모델 노드 이름에서 인덱스에 등록할 후보 키들 (중복 제거, 빈 값 제외):
        ///   ① 전체 정규화 ("GPSET/U001-DO-102-K")
        ///   ② 첫 '/' 앞 접두 ("TAG/suffix" 규약 — Trion)
        ///   ③ 마지막 '/' 뒤 세그먼트 ("/GPSET/TAG" 규약 — Ruya PDMS 경로)
        /// 슬래시가 없으면 ①만. 각 후보는 바깥 괄호를 벗긴다.
        /// </summary>
        public static List<string> Candidates(string displayName)
        {
            var list = new List<string>(3);
            string full = Normalize(displayName);
            if (full.Length == 0) return list;
            Add(list, full);

            int first = full.IndexOf('/');
            if (first > 0)
                Add(list, StripParens(full.Substring(0, first).Trim()));

            int last = full.LastIndexOf('/');
            if (last >= 0 && last < full.Length - 1)
                Add(list, StripParens(full.Substring(last + 1).Trim()));

            return list;
        }

        private static void Add(List<string> list, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            foreach (var k in list)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(key);
        }

        /// <summary>"(X)" → "X" (중첩 괄호도 반복 제거). 안쪽 괄호("A(1)")는 건드리지 않음.</summary>
        private static string StripParens(string s)
        {
            while (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')')
                s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }
    }
}
