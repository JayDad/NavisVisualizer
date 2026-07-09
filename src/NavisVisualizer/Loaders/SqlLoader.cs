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
        /// <summary>
        /// Sub-system 요소 로드 — 5공종: Equipment(Mech_EQ)·Piping(Piping_HydrotestPKG)은
        /// 기존 검증 쿼리 재사용, EIT 3공종(EIT_EQ/EIT_Tray/EIT_Cable)은 편입(2026-07 사용자 요청).
        /// EIT 계열은 공종별 try/catch — 컬럼 미구성(특히 EIT_Tray의 [SUB-SYSTEM]은 실측 미확인)
        /// 이어도 나머지 공종은 정상 로드하고 disciplineNotes로 사유를 보고한다.
        /// Sub-System 미지정 행은 제외: Equipment/Piping은 noSubSystemCount(기존 유지),
        /// EIT 계열은 모수가 커서(케이블 수만 건) 공종별 "지정 M/전체 N" note로 분리 보고.
        /// </summary>
        public static List<SubSystemElement> LoadSubSystemElements(
            SqlConnectionSettings settings, out int noSubSystemCount, out List<string> disciplineNotes)
        {
            var elements = new List<SubSystemElement>();
            noSubSystemCount = 0;
            disciplineNotes = new List<string>();

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

            // EIT EQ — [Navis].[EIT_EQ]: TAG NO + INSTALL DTE(설치 실적일, 구 WRKDTE) + SUB-SYSTEM.
            // 프로젝트 컬럼 없음(§9) → WHERE 생략.
            try
            {
                int total = 0, taken = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                const string eitEqSql = @"
SELECT [TAG NO],[TAG DESCRIPTION],[INSTALL DTE],[SUB-SYSTEM]
FROM [Navis].[EIT_EQ]";
                ExecuteReader(settings, eitEqSql, null, r =>
                {
                    total++;
                    string tag = GetString(r, "TAG NO");
                    string sub = GetString(r, "SUB-SYSTEM");
                    if (string.IsNullOrEmpty(tag) || string.IsNullOrWhiteSpace(sub)) return;
                    if (!seen.Add(EitTrayData.NormalizeId(tag))) return;
                    taken++;
                    elements.Add(SubSystemElement.FromEitEquipment(
                        tag, GetString(r, "TAG DESCRIPTION"), sub, GetDate(r, "INSTALL DTE")));
                });
                disciplineNotes.Add($"EIT EQ {taken:N0}/{total:N0}건 편입");
            }
            catch (Exception ex)
            {
                disciplineNotes.Add($"EIT EQ 제외({FirstLine(ex.Message)})");
            }

            // EIT Tray — [Navis].[EIT_Tray]: [SUB-SYSTEM] 컬럼은 실측 미확인(문서상 BRANCH NO./
            // Install %/PJTNO만 확정 — §9). 없으면 이 블록만 실패하고 note로 드러난다.
            try
            {
                int total = 0, taken = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                const string traySql = @"
SELECT [BRANCH NO.],[TRAY Install %],[SUB-SYSTEM]
FROM [Navis].[EIT_Tray]";
                ExecuteReader(settings, traySql, "PJTNO", r =>
                {
                    total++;
                    string trayNo = GetString(r, "BRANCH NO.");
                    string sub = GetString(r, "SUB-SYSTEM");
                    if (string.IsNullOrEmpty(trayNo) || string.IsNullOrWhiteSpace(sub)) return;
                    if (!seen.Add(EitTrayData.NormalizeId(trayNo))) return;
                    taken++;
                    var tray = new EitTrayData
                    {
                        TrayNumber = trayNo,
                        InstallProgress = GetPercentage(r, "TRAY Install %"),
                    };
                    elements.Add(SubSystemElement.FromTray(tray, sub));
                });
                disciplineNotes.Add($"EIT Tray {taken:N0}/{total:N0}건 편입");
            }
            catch (Exception ex)
            {
                disciplineNotes.Add($"EIT Tray 제외({FirstLine(ex.Message)})");
            }

            // Cable — LoadCable(철자 실측 확정) 재사용. SUB-SYSTEM 미지정 케이블(샘플상 다수)은 제외.
            try
            {
                var cables = LoadCable(settings);
                int taken = 0;
                foreach (var c in cables)
                {
                    if (string.IsNullOrWhiteSpace(c.SubSystem)) continue;
                    taken++;
                    elements.Add(SubSystemElement.FromCable(c));
                }
                disciplineNotes.Add($"Cable {taken:N0}/{cables.Count:N0}건 편입");
            }
            catch (Exception ex)
            {
                disciplineNotes.Add($"Cable 제외({FirstLine(ex.Message)})");
            }

            return elements;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int idx = s.IndexOfAny(new[] { '\r', '\n' });
            return idx >= 0 ? s.Substring(0, idx) : s;
        }

        /// <summary>
        /// Sub-system 마스터 로드 ([Navis].[System_Summary] — 실측 스키마 2026-07 확정,
        /// 구 SubSystem_Master 계약 대체). 마일스톤 실적일(WD/Partial MCC/MCC/PCC Actual —
        /// 별도 RFCC 없음, MCC 계열이 Ready for Commissioning 의미) + MCC Plan(지연 판정 기준)
        /// + A/B/C-ITR·A/B Punch 수치(각 Total+Complete/Closed).
        /// 미사용 컬럼: Area/System/System Des(그룹핑 확장 후보), PCC Plan/MCC Fcst(계획·예측),
        /// %계열(Total/Complete 수치로 충분). 테이블이 아직 없으면 SQL Server가 예외를 던진다 —
        /// 호출부(SubSystemTab)가 잡아서 "마스터 미구성" fallback(요소 파생 목록)으로 전환한다.
        /// </summary>
        public static List<SubSystemMasterData> LoadSubSystemMaster(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [Sub-System],[Sub-System Des],
       [MCC Plan],[WD Actual],[Partial MCC Actual],[MCC Actual],[PCC Actual],
       [A-ITR Total],[A-ITR Complete],[B-ITR Total],[B-ITR Complete],[C-ITR Total],[C-ITR Complete],
       [A Punch Total],[A Punch Closed],[B Punch Total],[B Punch Closed]
FROM [Navis].[System_Summary]";

            var masters = new List<SubSystemMasterData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PJTNO", r =>
            {
                string no = GetString(r, "Sub-System");
                if (string.IsNullOrEmpty(no) || !seen.Add(no)) return;

                var m = new SubSystemMasterData
                {
                    SubSystemNo = no,
                    Description = GetString(r, "Sub-System Des"),
                    MccPlan      = GetDate(r, "MCC Plan"),
                    ItrATotal    = GetInt(r, "A-ITR Total"),
                    ItrADone     = GetInt(r, "A-ITR Complete"),
                    ItrBTotal    = GetInt(r, "B-ITR Total"),
                    ItrBDone     = GetInt(r, "B-ITR Complete"),
                    ItrCTotal    = GetInt(r, "C-ITR Total"),
                    ItrCDone     = GetInt(r, "C-ITR Complete"),
                    PunchATotal  = GetInt(r, "A Punch Total"),
                    PunchAClosed = GetInt(r, "A Punch Closed"),
                    PunchBTotal  = GetInt(r, "B Punch Total"),
                    PunchBClosed = GetInt(r, "B Punch Closed"),
                };
                m.StageDates[SubSystemStage.Walkdown]   = GetDate(r, "WD Actual");
                m.StageDates[SubSystemStage.PartialMcc] = GetDate(r, "Partial MCC Actual");
                m.StageDates[SubSystemStage.Mcc]        = GetDate(r, "MCC Actual");
                m.StageDates[SubSystemStage.Pcc]        = GetDate(r, "PCC Actual");
                masters.Add(m);
            });

            return masters;
        }

        // ------------------------------------------------------------
        // EIT Tray  ←  [Navis].[EIT_Tray]
        // ------------------------------------------------------------

        /// <summary>
        /// EIT Tray 진척 로드 ([Navis].[EIT_Tray] — BRANCH NO./TRAY Install %/PJTNO).
        /// 이 테이블엔 날짜 컬럼이 없어(§9) 기준일 필터 불가 — % 기반 현재상태 판정
        /// (EitTrayData.GetStage가 InstallProgress만 씀). BRANCH NO.의 선행 '/'·후행 '.'은
        /// 매칭 시 NormalizeId가 제거하므로 원시 그대로 보관하되, 중복 제거는 정규화 키로
        /// 한다 ("X"와 "X."은 같은 트레이 — 실측상 후행 '.' 장식 행 존재). 프로젝트 컬럼 = PJTNO.
        /// </summary>
        public static List<EitTrayData> LoadEitTray(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [BRANCH NO.],[TRAY Install %]
FROM [Navis].[EIT_Tray]";

            var trays = new List<EitTrayData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, "PJTNO", r =>
            {
                string trayNo = GetString(r, "BRANCH NO.");
                if (string.IsNullOrEmpty(trayNo) || !seen.Add(EitTrayData.NormalizeId(trayNo))) return;

                trays.Add(new EitTrayData
                {
                    TrayNumber = trayNo,
                    InstallProgress = GetPercentage(r, "TRAY Install %"),
                });
            });

            return trays;
        }

        // ------------------------------------------------------------
        // Cable(형상)  ←  [Navis].[EIT_Cable]   (컬럼 철자 실측 확정 — 2026-07 사용자 제공)
        // ------------------------------------------------------------

        /// <summary>
        /// Cable(형상) 탭용 OASIS 로드. 컬럼 철자는 실측 스키마로 확정(2026-07) — 날짜 4개는
        /// 전부 ` DATE` 접미사(`PULLING START DATE` 등). EIT_Cable엔 프로젝트 컬럼이 없어(§9)
        /// projectColumn=null → WHERE 생략. `PULLING LTH`는 실측 샘플상 포설 실적 길이로 보이나
        /// (0/189=0%, 37/37=100%) 데이터 오너 확정 전까지 길이·%는 표시 전용, stage 색엔 안 씀
        /// (§13-6). stage는 날짜만: Pulling=PULLING START DATE / Pulled=PULLING END DATE /
        /// Terminated=FROM·TO CONN DATE 둘 다(AND 게이트, ComputeTerminated).
        /// 미사용 컬럼(확장 후보): INSTALL_MODULE, SYSTEM DES, SUB-SYSTEM(+DES — sub-system 편입 시).
        /// </summary>
        public static List<CableLineData> LoadCable(SqlConnectionSettings settings)
        {
            const string baseSql = @"
SELECT [CABLE NO],[DESIGN LTH],[PULLING LTH],[Pulling %],
       [PULLING START DATE],[PULLING END DATE],[FROM CONN DATE],[TO CONN DATE],
       [FROM MODULE],[FROM EQUIP],[TO MODULE],[TO EQUIP],
       [CABLE_TYPE],[CABLE_CORE],[CABLE_SIZE],[OUT DIA],[TRAY SYS],[SYSTEM],[SUB-SYSTEM]
FROM [Navis].[EIT_Cable]";

            var cables = new List<CableLineData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExecuteReader(settings, baseSql, null, r =>
            {
                string cableNo = GetString(r, "CABLE NO");
                if (string.IsNullOrEmpty(cableNo) || !seen.Add(cableNo)) return;

                var c = new CableLineData
                {
                    CableNo = cableNo,
                    FromConnDate = GetDate(r, "FROM CONN DATE"),
                    ToConnDate   = GetDate(r, "TO CONN DATE"),
                    DesignLth    = GetDouble(r, "DESIGN LTH"),
                    PulledLth    = GetDouble(r, "PULLING LTH"),        // 표시 전용 (§13-6)
                    PullingProgress = GetPercentage(r, "Pulling %"),   // 표시 전용
                    FromModule = GetString(r, "FROM MODULE"),
                    FromEquip  = GetString(r, "FROM EQUIP"),
                    ToModule   = GetString(r, "TO MODULE"),
                    ToEquip    = GetString(r, "TO EQUIP"),
                    Type       = GetString(r, "CABLE_TYPE"),
                    Core       = GetString(r, "CABLE_CORE"),
                    Size       = GetString(r, "CABLE_SIZE"),
                    OutDia     = GetString(r, "OUT DIA"),
                    TraySys    = GetString(r, "TRAY SYS"),
                    System     = GetString(r, "SYSTEM"),
                    SubSystem  = GetString(r, "SUB-SYSTEM"),
                };
                c.StageDates[CableLineStage.Pulling] = GetDate(r, "PULLING START DATE");
                c.StageDates[CableLineStage.Pulled]  = GetDate(r, "PULLING END DATE");
                c.ComputeTerminated();
                cables.Add(c);
            });

            return cables;
        }

        #region Shared helpers

        /// <summary>
        /// baseSql에 프로젝트 필터(WHERE [projectColumn] = @prj)를 조건부로 붙여 실행하고
        /// 행마다 rowHandler를 호출한다. projectColumn은 테이블마다 다르다
        /// (EQ 계열 PJTNO / Piping 계열 PRJTNO). projectColumn이 null/빈 문자열이면
        /// (EIT_Cable처럼 프로젝트 컬럼이 없는 테이블) ProjectNo가 설정돼 있어도 WHERE를 생략한다.
        /// </summary>
        private static void ExecuteReader(SqlConnectionSettings settings, string baseSql,
            string projectColumn, Action<IDataRecord> rowHandler)
        {
            string sql = baseSql;
            bool filter = !string.IsNullOrWhiteSpace(settings.ProjectNo)
                && !string.IsNullOrWhiteSpace(projectColumn);
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

        /// <summary>실수 컬럼. typed 숫자는 그대로, varchar는 InvariantCulture 파싱. 실패 시 null.</summary>
        private static double? GetDouble(IDataRecord r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return null;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is decimal m) return (double)m;
            return double.TryParse(v.ToString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
                ? parsed : (double?)null;
        }

        /// <summary>
        /// 퍼센트 컬럼 → 0.0~1.0. ExcelLoader.ParsePercentage와 동일 휴리스틱:
        /// 숫자 0~100 스케일(85)이면 /100, 0~1 스케일(0.85)이면 그대로, "85%" 문자열도 처리(§2.2).
        /// 스케일 오판 시 전 트레이가 설치완료/미착수로 오도되므로 전 분기를 명시.
        /// </summary>
        private static double? GetPercentage(IDataRecord r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return null;
            if (v is double d) return d > 1.0 ? d / 100.0 : d;
            if (v is float f) return f > 1.0f ? f / 100.0 : f;
            if (v is int i) return i > 1 ? i / 100.0 : i;
            if (v is long l) return l > 1 ? l / 100.0 : l;
            if (v is decimal m) { double dv = (double)m; return dv > 1.0 ? dv / 100.0 : dv; }
            string s = v.ToString().Trim();
            if (string.IsNullOrEmpty(s)) return null;
            bool hasPct = s.EndsWith("%");
            if (hasPct) s = s.Substring(0, s.Length - 1).Trim();
            if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)) return null;
            return hasPct || parsed > 1.0 ? parsed / 100.0 : parsed;
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
