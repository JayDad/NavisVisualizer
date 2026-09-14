using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// 공사(프로젝트) 하나. Code = DB의 PJTNO/PRJTNO 값, Name = 사용자에게 보이는 공사명.
    /// 공사명은 DB에 없다(BASE TABLE 10개 중 프로젝트 마스터 테이블 없음 —
    /// docs/SQL_DB_CONNECTION_ANALYSIS.md 부록) → 이름은 코드 기본값 + oasis.config가 원천.
    /// </summary>
    public class ProjectInfo
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>true면 DB에서 코드만 발견됐고 이름 매핑이 없는 공사.</summary>
        public bool NameUnknown => string.IsNullOrWhiteSpace(Name);

        /// <summary>드롭다운 표기: "Trion (Q557)". 이름을 모르면 "Q999 (이름 미등록)".</summary>
        public string Display =>
            NameUnknown ? $"{Code} (이름 미등록)" : $"{Name} ({Code})";

        public override string ToString() => Display;
    }

    /// <summary>
    /// 공사코드 ↔ 공사명 목록. 세 겹으로 합쳐진다 (뒤가 앞을 덮어씀):
    ///   ① 코드 내장 기본값 (Q557=Trion, Q558=Ruya) — 배포 직후·config 없이도 동작
    ///   ② oasis.config의 `project.<코드>=<이름>` 줄 — 공사 추가 시 재빌드 없이 현장에서 확장
    ///   ③ DB에서 DISTINCT로 발견된 코드 — 이름이 없으면 "(이름 미등록)"으로만 추가
    ///
    /// ③은 이름을 채우지 못한다(DB에 공사명이 없음) — 새 공사가 생겼을 때 목록에서
    /// 사라지지 않게 하는 용도. 이름을 붙이려면 ①/②에 등록해야 한다.
    ///
    /// Autodesk 비의존 — 리눅스에서 컴파일·테스트 검증 가능.
    /// </summary>
    public static class ProjectCatalog
    {
        /// <summary>ProjectNo가 빈 문자열 = WHERE 절 생략 = 전체 프로젝트 로드.</summary>
        public const string AllProjectsCode = "";

        /// <summary>
        /// 코드 내장 기본 공사 목록. 공사가 추가되면 여기에 넣거나(재빌드)
        /// oasis.config에 `project.<코드>=<이름>`을 적는다(재빌드 불필요).
        /// </summary>
        private static readonly Dictionary<string, string> BuiltIn =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Q557", "Trion" },
                { "Q558", "Ruya" },
            };

        /// <summary>
        /// 기본값 + config 이름을 병합한 목록을 만든다. 정렬은 코드 오름차순
        /// (Q557 → Q558 → …) — 공사번호가 곧 시간 순서라 사용자 기대와 일치.
        /// </summary>
        public static List<ProjectInfo> Build(IDictionary<string, string> configNames)
        {
            var merged = new Dictionary<string, string>(BuiltIn, StringComparer.OrdinalIgnoreCase);

            if (configNames != null)
            {
                foreach (var kv in configNames)
                {
                    string code = (kv.Key ?? "").Trim();
                    if (code.Length == 0) continue;
                    merged[code] = (kv.Value ?? "").Trim();
                }
            }

            return merged
                .Select(kv => new ProjectInfo { Code = kv.Key, Name = kv.Value })
                .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// DB에서 발견한 코드를 목록에 병합한다. 이미 있는 코드는 이름을 보존하고
        /// (DB엔 이름이 없으므로 덮어쓰면 이름이 지워진다), 처음 보는 코드만 추가한다.
        /// 반환값 = 새로 추가된 코드 수 (사용자 안내용).
        /// </summary>
        public static int MergeDiscovered(List<ProjectInfo> catalog, IEnumerable<string> discoveredCodes)
        {
            if (catalog == null || discoveredCodes == null) return 0;

            var known = new HashSet<string>(catalog.Select(p => p.Code), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var raw in discoveredCodes)
            {
                string code = (raw ?? "").Trim();
                if (code.Length == 0 || !known.Add(code)) continue;
                catalog.Add(new ProjectInfo { Code = code, Name = "" });
                added++;
            }

            if (added > 0)
                catalog.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Code, b.Code));

            return added;
        }

        /// <summary>
        /// 코드 → 표시 문자열. 목록에 없는 코드도 코드 그대로 보여준다(조용히 비우지 않음).
        /// 빈 코드는 "전체" — WHERE 절 없이 전 공사를 읽는 상태.
        /// </summary>
        public static string DisplayFor(IEnumerable<ProjectInfo> catalog, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "전체";
            if (catalog != null)
            {
                foreach (var p in catalog)
                {
                    if (string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))
                        return p.Display;
                }
            }
            return code;
        }
    }
}
