using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using NavisVisualizer.Models;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// OASIS(사내 SQL Server, [Navis] 스키마)에서 실적 데이터를 읽는다.
    ///
    /// ExcelLoader와 달리 헤더 후보 추측을 하지 않는다 — DB 스키마는 고정 계약이므로
    /// 테이블별 SELECT에 컬럼을 명시하고 프로퍼티에 직접 매핑한다. 컬럼명이 틀리면
    /// SQL Server가 즉시 오류를 내므로 "조용히 빈 값" 문제가 원천 차단된다.
    ///
    /// 반환 타입은 ExcelLoader와 동일 — 탭/서처/색상 엔진은 데이터 출처를 구분하지 않는다.
    /// </summary>
    public static class SqlLoader
    {
        // ------------------------------------------------------------
        // Spool  ←  [Navis].[Piping_Spool]
        // ------------------------------------------------------------

        public static List<SpoolData> LoadSpool(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [SPOOL NO],[ISO NO],
       [B/V],[F/up],[W/D],[NDE],[PWHT],[S/out],
       [G-후공정인계],[Galv2],[Pnt1],[Pnt2],[Stock],[H/O일자],
       [Setting],[FIT-UP],[Welding]
FROM [Navis].[Piping_Spool]";

            var spools = new List<SpoolData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PRJTNO", r =>
            {
                string spoolId = GetString(r, "SPOOL NO");
                if (string.IsNullOrEmpty(spoolId) || !seen.Add(spoolId)) return;

                var spool = new SpoolData
                {
                    SpoolId = spoolId,
                    IsoNo = GetString(r, "ISO NO"),
                };
                spool.StageDates[SpoolStage.BV]          = GetDate(r, "B/V");
                spool.StageDates[SpoolStage.FitUp]       = GetDate(r, "F/up");
                spool.StageDates[SpoolStage.WeldDone]    = GetDate(r, "W/D");
                spool.StageDates[SpoolStage.NDE]         = GetDate(r, "NDE");
                spool.StageDates[SpoolStage.PWHT]        = GetDate(r, "PWHT");
                spool.StageDates[SpoolStage.ShipOut]     = GetDate(r, "S/out");
                spool.StageDates[SpoolStage.PostProcess] = GetDate(r, "G-후공정인계");
                spool.StageDates[SpoolStage.Galvanizing] = GetDate(r, "Galv2");
                spool.StageDates[SpoolStage.Paint1]      = GetDate(r, "Pnt1");
                spool.StageDates[SpoolStage.Paint2]      = GetDate(r, "Pnt2");
                spool.StageDates[SpoolStage.Stock]       = GetDate(r, "Stock");
                spool.StageDates[SpoolStage.HandOver]    = GetDate(r, "H/O일자");
                spool.StageDates[SpoolStage.Setting]      = GetDate(r, "Setting");
                // 설치 fit-up (제작 F/up과 별개). Welding 단계는 라벨상 "Install"(Welding+Flange).
                spool.StageDates[SpoolStage.FitUpInstall] = GetDate(r, "FIT-UP");
                spool.StageDates[SpoolStage.Welding]      = GetDate(r, "Welding");
                spools.Add(spool);
            });

            return spools;
        }

        // ------------------------------------------------------------
        // Hydrotest  ←  [Navis].[Piping_HydrotestPKG]
        // ------------------------------------------------------------

        public static List<TestPackageData> LoadHydrotest(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [PKGNO],[LINESVC],[Sub-System],
       [Review],[Line inspection],[Flushing],[Hydrotest],[Drying],[Reinstatement]
FROM [Navis].[Piping_HydrotestPKG]";

            var packages = new List<TestPackageData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PRJTNO", r =>
            {
                string pkgId = GetString(r, "PKGNO");
                if (string.IsNullOrEmpty(pkgId) || !seen.Add(pkgId)) return;

                var pkg = new TestPackageData
                {
                    TestPkgId = pkgId,
                    // Excel 경로는 "System"(0201)을 썼지만 DB에서는 더 세밀한
                    // Sub-System(0201-00)을 표시/검색용으로 채택.
                    SystemNo = GetString(r, "Sub-System"),
                    LineService = GetString(r, "LINESVC"),
                };
                pkg.StageDates[HydrotestStage.Review]         = GetDate(r, "Review");
                pkg.StageDates[HydrotestStage.LineInspection] = GetDate(r, "Line inspection");
                pkg.StageDates[HydrotestStage.Flushing]       = GetDate(r, "Flushing");
                pkg.StageDates[HydrotestStage.Hydrotest]      = GetDate(r, "Hydrotest");
                pkg.StageDates[HydrotestStage.Drying]         = GetDate(r, "Drying");
                pkg.StageDates[HydrotestStage.Reinstatement]  = GetDate(r, "Reinstatement");
                packages.Add(pkg);
            });

            return packages;
        }

        // ------------------------------------------------------------
        // Equipment  ←  [Navis].[Mech_EQ]
        // ------------------------------------------------------------

        /// <summary>
        /// Mech_EQ 단독 로드. All_EQ는 사용자 결정으로 제외(2026-07) —
        /// Mech_EQ가 stage 실적을 보유한 기계 장비 마스터. 컬럼은 실 스키마 대조 완료.
        /// </summary>
        public static List<EquipmentData> LoadEquipment(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [RFQ NO],[SUB-SYSTEM],[TAG NO],[TAG DESCRIPTION],
       [Delivered],[Confirmed ETA],[Loading],[Setting],[Inspection]
FROM [Navis].[Mech_EQ]";

            var items = new List<EquipmentData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PJTNO", r =>
            {
                // 모델 인덱스 키는 선행 '/' 제거 형태 — 방어적으로 동일 정규화 유지.
                string tagNo = GetString(r, "TAG NO").TrimStart('/').Trim();
                if (string.IsNullOrEmpty(tagNo) || !seen.Add(tagNo)) return;

                DateTime? delivered = GetDate(r, "Delivered");

                var equip = new EquipmentData
                {
                    TagNo = tagNo,
                    RfqNo = GetString(r, "RFQ NO"),
                    SubSystem = GetString(r, "SUB-SYSTEM"),
                    Description = GetString(r, "TAG DESCRIPTION"),
                    // Excel 경로의 상태 텍스트("Delivered") 관례를 유지 — 속성 쓰기 등
                    // 기존 소비처가 이 문자열을 그대로 출력한다.
                    DeliveryStatus = delivered.HasValue ? "Delivered" : "",
                    ConfirmedEta = GetDate(r, "Confirmed ETA"),
                };
                // Excel 경로와 달리 DB의 Delivered는 날짜이므로 stage에 직접 매핑.
                equip.StageDates[EquipmentStage.Delivery]   = delivered;
                equip.StageDates[EquipmentStage.Loading]    = GetDate(r, "Loading");
                equip.StageDates[EquipmentStage.Setting]    = GetDate(r, "Setting");
                equip.StageDates[EquipmentStage.Inspection] = GetDate(r, "Inspection");
                items.Add(equip);
            });

            return items;
        }

        // ------------------------------------------------------------
        // Sub-system  ←  Mech_EQ (Equipment) + Piping_HydrotestPKG (Piping)
        // ------------------------------------------------------------

        /// <summary>
        /// Sub-system 탭용 통합 요소 로드. 새 SQL 없이 기존 검증된 쿼리
        /// (LoadEquipment / LoadHydrotest)를 재사용하고 Sub-system 축으로 감싼다.
        /// Sub-system 값이 없는 행은 그룹핑이 불가능해 제외하며, 조용한 누락이 되지
        /// 않도록 그 수를 noSubSystemCount로 보고한다.
        /// </summary>
        public static List<SubSystemElement> LoadSubSystemElements(
            SqlConnectionSettings settings, out int noSubSystemCount)
        {
            var elements = new List<SubSystemElement>();
            noSubSystemCount = 0;

            foreach (var eq in LoadEquipment(settings))
            {
                if (string.IsNullOrWhiteSpace(eq.SubSystem)) { noSubSystemCount++; continue; }
                elements.Add(SubSystemElement.FromEquipment(eq));
            }

            foreach (var pkg in LoadHydrotest(settings))
            {
                if (string.IsNullOrWhiteSpace(pkg.SystemNo)) { noSubSystemCount++; continue; }
                elements.Add(SubSystemElement.FromPackage(pkg));
            }

            return elements;
        }

        /// <summary>
        /// Sub-system 마스터 로드 ([Navis].[SubSystem_Master] — 계약은 CLAUDE.md 11번).
        /// 마일스톤 날짜(Walkdown/Partial MCC/MCC/PCC — 별도 RFCC 없음, MCC 계열이
        /// Ready for Commissioning 의미) + A/B/C-ITR·A/B Punch 수치(각 Total+완료/종결).
        /// 테이블이 아직 없으면 SQL Server가 예외를 던진다 — 호출부(SubSystemTab)가
        /// 잡아서 "마스터 미구성" fallback(요소 파생 목록)으로 전환한다.
        /// </summary>
        public static List<SubSystemMasterData> LoadSubSystemMaster(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [SUB-SYSTEM],[DESCRIPTION],
       [MCC Plan],[Walkdown],[Partial MCC],[MCC],[PCC],
       [A-ITR TOTAL],[A-ITR DONE],[B-ITR TOTAL],[B-ITR DONE],[C-ITR TOTAL],[C-ITR DONE],
       [PUNCH A TOTAL],[PUNCH A CLOSED],[PUNCH B TOTAL],[PUNCH B CLOSED]
FROM [Navis].[SubSystem_Master]";

            var masters = new List<SubSystemMasterData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PJTNO", r =>
            {
                string no = GetString(r, "SUB-SYSTEM");
                if (string.IsNullOrEmpty(no) || !seen.Add(no)) return;

                var m = new SubSystemMasterData
                {
                    SubSystemNo = no,
                    Description = GetString(r, "DESCRIPTION"),
                    MccPlan      = GetDate(r, "MCC Plan"),
                    ItrATotal    = GetInt(r, "A-ITR TOTAL"),
                    ItrADone     = GetInt(r, "A-ITR DONE"),
                    ItrBTotal    = GetInt(r, "B-ITR TOTAL"),
                    ItrBDone     = GetInt(r, "B-ITR DONE"),
                    ItrCTotal    = GetInt(r, "C-ITR TOTAL"),
                    ItrCDone     = GetInt(r, "C-ITR DONE"),
                    PunchATotal  = GetInt(r, "PUNCH A TOTAL"),
                    PunchAClosed = GetInt(r, "PUNCH A CLOSED"),
                    PunchBTotal  = GetInt(r, "PUNCH B TOTAL"),
                    PunchBClosed = GetInt(r, "PUNCH B CLOSED"),
                };
                m.StageDates[SubSystemStage.Walkdown]   = GetDate(r, "Walkdown");
                m.StageDates[SubSystemStage.PartialMcc] = GetDate(r, "Partial MCC");
                m.StageDates[SubSystemStage.Mcc]        = GetDate(r, "MCC");
                m.StageDates[SubSystemStage.Pcc]        = GetDate(r, "PCC");
                masters.Add(m);
            });

            return masters;
        }

        #region Shared helpers

        /// <summary>
        /// baseSql에 프로젝트 필터(WHERE [projectColumn] = @prj)를 조건부로 붙여 실행하고
        /// 행마다 rowHandler를 호출한다. projectColumn은 테이블마다 다르다
        /// (EQ 계열 PJTNO / Piping 계열 PRJTNO).
        /// </summary>
        private static void ExecuteReader(SqlConnectionSettings settings, string baseSql,
            string projectColumn, Action<IDataRecord> rowHandler)
        {
            string sql = baseSql;
            bool filter = !string.IsNullOrWhiteSpace(settings.ProjectNo);
            if (filter)
                sql += $"\nWHERE [{projectColumn}] = @prj";

            using (var conn = new SqlConnection(settings.BuildConnectionString()))
            using (var cmd = new SqlCommand(sql, conn))
            {
                if (filter)
                    cmd.Parameters.AddWithValue("@prj", settings.ProjectNo.Trim());
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        rowHandler(reader);
                }
            }
        }

        private static string GetString(IDataRecord r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return "";
            return v.ToString().Trim();
        }

        /// <summary>정수 컬럼. typed 숫자는 그대로 변환, varchar는 InvariantCulture 파싱. 실패 시 null.</summary>
        private static int? GetInt(IDataRecord r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return null;
            if (v is int i) return i;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch
            {
                return int.TryParse(v.ToString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed : (int?)null;
            }
        }

        /// <summary>
        /// DATE/DATETIME 컬럼은 DateTime으로 그대로, varchar 날짜("2025-06-20")는
        /// InvariantCulture 우선으로 파싱한다(현지 문화권 fallback). 실패 시 null.
        /// </summary>
        private static DateTime? GetDate(IDataRecord r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return null;
            if (v is DateTime dt) return dt;

            string s = v.ToString().Trim();
            if (string.IsNullOrEmpty(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
            if (DateTime.TryParse(s, out parsed))
                return parsed;
            return null;
        }

        #endregion
    }
}
