using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// [Navis] 스키마 테이블별 프로젝트 필터 컬럼명을 실제 DB 스키마에서 확인한다.
    ///
    /// **왜 조회하는가**: 컬럼명이 테이블마다 다르다 — Piping 2종은 `PRJTNO`,
    /// EQ/Summary 계열은 `PJTNO`. 게다가 컬럼이 없던 테이블(EIT_Cable 등)은 DB단에서
    /// 나중에 추가됐고(2026-07) 어느 철자를 썼는지 코드가 알 수 없다. 하드코딩하면
    /// 철자가 어긋나는 순간 "Invalid column name"으로 그 탭 전체가 죽는다.
    /// INFORMATION_SCHEMA 한 번 조회(DB당 1회 캐시)로 이 추측을 없앤다.
    ///
    /// 조회 자체가 실패하면(권한 없음 등) 알려진 기본 철자로 degrade한다 — 이땐
    /// 철자가 틀리면 SQL 오류로 드러나므로 조용한 오작동은 아니다.
    /// </summary>
    public static class ProjectColumnResolver
    {
        public const string Schema = "Navis";

        /// <summary>후보 철자. 이 둘만 허용 — 해석된 이름을 SQL에 넣으므로 화이트리스트 역할도 한다.</summary>
        private static readonly string[] Candidates = { "PJTNO", "PRJTNO" };

        /// <summary>
        /// INFORMATION_SCHEMA를 못 읽을 때 쓰는 알려진 철자(2026-07 실측).
        /// 실측되지 않은 테이블은 넣지 않는다 — 모르면 "모른다"가 정답이고,
        /// 추측한 철자로 조용히 잘못된 SQL을 만드는 것보다 낫다.
        /// </summary>
        private static readonly Dictionary<string, string> KnownFallback =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Piping_Spool",        "PRJTNO" },
                { "Piping_HydrotestPKG", "PRJTNO" },
                { "Mech_EQ",             "PJTNO"  },
                { "All_EQ",              "PJTNO"  },
                { "System_Summary",      "PJTNO"  },
                { "EIT_Tray",            "PJTNO"  },
                // EIT 계열도 철자가 갈린다 — Tray는 PJTNO, Cable은 PRJTNO(2026-07 사용자 확정).
                // "EIT_*는 PJTNO"처럼 접두사로 추정하면 안 된다는 근거.
                { "EIT_Cable",           "PRJTNO" },
                // EIT_EQ는 철자 미확인 — 넣지 않는다. 스키마 조회가 되면 자동 해석되고,
                // 조회 불가 환경에서만 "전체 선택" 안내와 함께 막힌다(잘못된 철자로
                // 조용히 다른 공사를 읽는 것보다 낫다).
            };

        // DB(서버+데이터베이스)별 캐시. 스키마는 세션 중 안 바뀐다는 전제 —
        // 캐시하는 대상이 "DB 스키마"라 라이브 상태(단면/선택)와 달리 L2 대상이 아니다.
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> Cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 테이블의 프로젝트 컬럼명. 해당 컬럼이 없으면 null.
        /// </summary>
        public static string Resolve(SqlConnectionSettings settings, string table)
        {
            if (settings == null || string.IsNullOrWhiteSpace(table)) return null;

            var map = GetMap(settings);
            if (map != null && map.TryGetValue(table, out string col))
                return col;

            // 스키마 조회가 성공했는데 목록에 없다 = 그 테이블엔 프로젝트 컬럼이 없다.
            if (map != null) return null;

            // 스키마 조회 실패 → 알려진 철자로 degrade (모르는 테이블은 null).
            return KnownFallback.TryGetValue(table, out string known) ? known : null;
        }

        /// <summary>
        /// 프로젝트 컬럼을 가진 테이블 중 코드 목록을 뽑기 좋은 것을 고른다.
        /// System_Summary(sub-system당 1행)가 가장 작아 우선 — 대형 실적 테이블에
        /// DISTINCT를 거는 비용을 피한다.
        /// </summary>
        public static bool TryPickDiscoveryTable(SqlConnectionSettings settings,
            out string table, out string column)
        {
            string[] preferred = { "System_Summary", "Mech_EQ", "EIT_Tray", "Piping_Spool", "Piping_HydrotestPKG" };
            foreach (var t in preferred)
            {
                string col = Resolve(settings, t);
                if (!string.IsNullOrEmpty(col))
                {
                    table = t;
                    column = col;
                    return true;
                }
            }
            table = null;
            column = null;
            return false;
        }

        /// <summary>캐시된 테이블→컬럼 맵. 조회 실패 시 null (fallback 신호).</summary>
        private static Dictionary<string, string> GetMap(SqlConnectionSettings settings)
        {
            string key = settings.Server + "|" + settings.Database;
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            Dictionary<string, string> map = QuerySchema(settings);

            lock (Sync)
            {
                // 조회 실패(null)는 캐시하지 않는다 — 일시적 네트워크 장애 뒤
                // 영구히 fallback에 갇히지 않도록.
                if (map != null) Cache[key] = map;
            }
            return map;
        }

        private static Dictionary<string, string> QuerySchema(SqlConnectionSettings settings)
        {
            const string sql = @"
SELECT TABLE_NAME, COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema AND COLUMN_NAME IN ('PJTNO','PRJTNO')";

            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var conn = new SqlConnection(settings.BuildConnectionString()))
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@schema", Schema);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string table = reader[0]?.ToString()?.Trim();
                            string column = reader[1]?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) continue;
                            if (Array.IndexOf(Candidates, column.ToUpperInvariant()) < 0) continue;
                            // 한 테이블에 둘 다 있으면 먼저 온 것을 쓴다 (실측상 없는 조합).
                            if (!map.ContainsKey(table)) map[table] = column;
                        }
                    }
                }
                // 0건은 "어느 테이블에도 프로젝트 컬럼이 없다"가 아니라 스키마를 못 봤다는
                // 뜻으로 해석한다 — INFORMATION_SCHEMA는 권한 있는 객체만 노출하므로 계정
                // 권한에 따라 조용히 비어 나올 수 있다. 이걸 "컬럼 없음"으로 받으면 멀쩡히
                // 돌던 배포가 전부 로드 실패가 된다. 알려진 철자 fallback으로 넘긴다.
                return map.Count > 0 ? map : null;
            }
            catch
            {
                return null;   // 권한 없음 등 — 호출부가 알려진 철자로 degrade
            }
        }

        /// <summary>테스트/진단용 — 캐시 비우기 (DB 스키마를 바꾼 뒤 재조회).</summary>
        public static void ClearCache()
        {
            lock (Sync) { Cache.Clear(); }
        }
    }
}
