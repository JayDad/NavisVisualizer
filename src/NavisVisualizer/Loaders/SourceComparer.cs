using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// Excel ↔ OASIS 두 소스의 동일 모듈 데이터를 키로 조인해 차이를 CSV로 만든다.
    /// OASIS 이행 검증용 — 차이 나는 행만 출력하고 상단에 요약을 붙인다.
    /// </summary>
    public static class SourceComparer
    {
        /// <summary>비교할 필드 하나: 표시 이름 + 값 추출 함수(문자열화는 호출부 책임).</summary>
        public struct Field<T>
        {
            public string Name;
            public Func<T, string> Get;
            public Field(string name, Func<T, string> get) { Name = name; Get = get; }
        }

        /// <summary>
        /// CSV 라인 목록 생성. 키는 정규화(trim, 선행 '/' 제거, 대문자) 후 조인한다.
        /// </summary>
        public static List<string> BuildCsv<T>(
            string keyHeader,
            IReadOnlyList<T> excelRows,
            IReadOnlyList<T> oasisRows,
            Func<T, string> keySelector,
            IReadOnlyList<Field<T>> fields)
        {
            var excel = IndexByKey(excelRows, keySelector);
            var oasis = IndexByKey(oasisRows, keySelector);

            var onlyExcel = excel.Keys.Where(k => !oasis.ContainsKey(k)).OrderBy(k => k).ToList();
            var onlyOasis = oasis.Keys.Where(k => !excel.ContainsKey(k)).OrderBy(k => k).ToList();

            var diffLines = new List<string>();
            int mismatchKeys = 0, matchKeys = 0;

            foreach (var key in excel.Keys.Where(oasis.ContainsKey).OrderBy(k => k))
            {
                var e = excel[key];
                var o = oasis[key];
                bool any = false;
                foreach (var f in fields)
                {
                    string ev = f.Get(e) ?? "";
                    string ov = f.Get(o) ?? "";
                    if (!string.Equals(ev, ov, StringComparison.OrdinalIgnoreCase))
                    {
                        diffLines.Add(Row("불일치", key, f.Name, ev, ov));
                        any = true;
                    }
                }
                if (any) mismatchKeys++; else matchKeys++;
            }

            var lines = new List<string>
            {
                $"# Excel {excel.Count}건 / OASIS {oasis.Count}건 / " +
                $"일치 {matchKeys} / 불일치 {mismatchKeys} / " +
                $"Excel에만 {onlyExcel.Count} / OASIS에만 {onlyOasis.Count}",
                Row("구분", keyHeader, "항목", "Excel", "OASIS"),
            };
            lines.AddRange(onlyExcel.Select(k => Row("Excel에만", k, "", "", "")));
            lines.AddRange(onlyOasis.Select(k => Row("OASIS에만", k, "", "", "")));
            lines.AddRange(diffLines);
            return lines;
        }

        private static Dictionary<string, T> IndexByKey<T>(IReadOnlyList<T> rows, Func<T, string> keySelector)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string key = NormalizeKey(keySelector(row));
                if (string.IsNullOrEmpty(key)) continue;
                if (!map.ContainsKey(key))
                    map[key] = row; // 중복 키는 첫 행 기준 (로더가 이미 dedupe함)
            }
            return map;
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Trim().TrimStart('/').ToUpperInvariant();
        }

        private static string Row(params string[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append((cells[i] ?? "").Replace("\"", "\"\"")).Append('"');
            }
            return sb.ToString();
        }

        public static string FormatDate(DateTime? d) =>
            d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "";
    }
}
