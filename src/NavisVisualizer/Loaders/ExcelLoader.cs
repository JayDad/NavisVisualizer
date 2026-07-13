using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ExcelDataReader;
using NavisVisualizer.Models;
using NavisVisualizer.Services;

namespace NavisVisualizer.Loaders
{
    public static class ExcelLoader
    {
        public static List<TestPackageData> LoadHydrotest(string filePath) =>
            LoadHydrotest(filePath, out _);

        /// <summary>중복 Pkg ID는 첫 행만 유지 (OASIS 로더와 동일 정책 — 성능 audit §10:
        /// 중복 행이 stage 그룹·override 컬렉션·통계를 중복 부풀렸다). 제외 건수는 out으로 보고.</summary>
        public static List<TestPackageData> LoadHydrotest(string filePath, out int duplicatesSkipped)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            duplicatesSkipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packages = new List<TestPackageData>();
            var pkgHeaderNames = new[] { "Test Package No.", "Test Package No", "TestPkgId", "Test Pkg No" };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1;

                foreach (System.Data.DataTable dt in dataSet.Tables)
                {
                    int row = FindHeaderRowInTable(dt, pkgHeaderNames);
                    if (row >= 0)
                    {
                        table = dt;
                        headerRowIdx = row;
                        break;
                    }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Test Package No.' 헤더를 포함한 시트를 찾을 수 없습니다.");

                var cols = BuildColumnMap(table, headerRowIdx);

                var stageColumns = new List<(int colIndex, HydrotestStage stage)>();
                foreach (var kv in HydrotestStageInfo.ColumnMap)
                {
                    if (cols.TryGetValue(kv.Key, out int idx))
                        stageColumns.Add((idx, kv.Value));
                }

                int pkgCol = FindColumn(cols, pkgHeaderNames);
                int sysCol = FindColumn(cols, "System No.", "System No", "SystemNo", "System");
                int lineCol = FindColumn(cols, "Line Service", "LineService", "Service");

                if (pkgCol < 0)
                    throw new Exception("'Test Package No.' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string pkgId = row[pkgCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(pkgId)) continue;
                    if (!seen.Add(pkgId)) { duplicatesSkipped++; continue; }

                    var pkg = new TestPackageData
                    {
                        TestPkgId = pkgId,
                        SystemNo = sysCol >= 0 ? row[sysCol]?.ToString()?.Trim() ?? "" : "",
                        LineService = lineCol >= 0 ? row[lineCol]?.ToString()?.Trim() ?? "" : "",
                    };

                    foreach (var (colIndex, stage) in stageColumns)
                    {
                        pkg.StageDates[stage] = ParseCellValue(row[colIndex]);
                    }

                    packages.Add(pkg);
                }
            }

            PerfLog.Record("Excel 로드(Hydrotest)", sw.ElapsedMilliseconds, rows: packages.Count,
                note: duplicatesSkipped > 0 ? $"중복 {duplicatesSkipped}건 제외" : "");
            return packages;
        }

        public static List<SpoolData> LoadSpool(string filePath) =>
            LoadSpool(filePath, out _);

        /// <summary>중복 Spool ID는 첫 행만 유지 (OASIS 로더와 동일 정책 — 성능 audit §10).</summary>
        public static List<SpoolData> LoadSpool(string filePath, out int duplicatesSkipped)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            duplicatesSkipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spools = new List<SpoolData>();
            var spoolHeaderNames = new[] { "Spool Number", "SpoolId", "Spool No", "SpoolNumber" };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1;

                var namedTable = dataSet.Tables.Cast<System.Data.DataTable>()
                    .FirstOrDefault(t => t.TableName.Equals("Spool", StringComparison.OrdinalIgnoreCase));
                if (namedTable != null)
                {
                    headerRowIdx = FindHeaderRowInTable(namedTable, spoolHeaderNames);
                    if (headerRowIdx >= 0) table = namedTable;
                }

                if (table == null)
                {
                    foreach (System.Data.DataTable dt in dataSet.Tables)
                    {
                        int row = FindHeaderRowInTable(dt, spoolHeaderNames);
                        if (row >= 0) { table = dt; headerRowIdx = row; break; }
                    }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Spool Number' 헤더를 포함한 시트를 찾을 수 없습니다.");

                var cols = BuildColumnMap(table, headerRowIdx);

                var stageColumns = new List<(int colIndex, SpoolStage stage)>();
                foreach (var kv in SpoolStageInfo.ColumnMap)
                {
                    if (cols.TryGetValue(kv.Key, out int idx))
                        stageColumns.Add((idx, kv.Value));
                }

                int spoolCol = FindColumn(cols, spoolHeaderNames);
                int isoCol = FindColumn(cols, "ISO No", "IsoNo", "ISO", "ISO Number");

                if (spoolCol < 0)
                    throw new Exception("'Spool Number' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string spoolId = row[spoolCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(spoolId)) continue;
                    if (!seen.Add(spoolId)) { duplicatesSkipped++; continue; }

                    var spool = new SpoolData
                    {
                        SpoolId = spoolId,
                        IsoNo = isoCol >= 0 ? row[isoCol]?.ToString()?.Trim() ?? "" : "",
                    };

                    foreach (var (colIndex, stage) in stageColumns)
                    {
                        spool.StageDates[stage] = ParseCellValue(row[colIndex]);
                    }

                    spools.Add(spool);
                }
            }

            PerfLog.Record("Excel 로드(Spool)", sw.ElapsedMilliseconds, rows: spools.Count,
                note: duplicatesSkipped > 0 ? $"중복 {duplicatesSkipped}건 제외" : "");
            return spools;
        }

        public static List<EquipmentData> LoadEquipment(string filePath) =>
            LoadEquipment(filePath, out _);

        /// <summary>중복 Tag No는 첫 행만 유지 (OASIS 로더와 동일 정책 — 성능 audit §10).</summary>
        public static List<EquipmentData> LoadEquipment(string filePath, out int duplicatesSkipped)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            duplicatesSkipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<EquipmentData>();
            var tagHeaderNames = new[] { "Tag No.", "Tag No", "TagNo", "Tag Number" };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1;

                foreach (System.Data.DataTable dt in dataSet.Tables)
                {
                    int row = FindHeaderRowInTable(dt, tagHeaderNames);
                    if (row >= 0) { table = dt; headerRowIdx = row; break; }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Tag No.' 헤더를 포함한 시트를 찾을 수 없습니다.");

                var cols = BuildColumnMap(table, headerRowIdx);

                // Date stage columns (Loading, Setting, Inspection)
                var stageColumns = new List<(int colIndex, EquipmentStage stage)>();
                foreach (var kv in EquipmentStageInfo.ColumnMap)
                {
                    if (cols.TryGetValue(kv.Key, out int idx))
                        stageColumns.Add((idx, kv.Value));
                }

                int tagCol = FindColumn(cols, tagHeaderNames);
                int rfqCol = FindColumn(cols, "RFQ No.", "RFQ No", "RFQ");
                int subSysCol = FindColumn(cols, "SUB SYSTEM", "Sub System", "SubSystem", "System");
                int descCol = FindColumn(cols, "Equipment Description", "Description", "Desc");
                int deliveryCol = FindColumn(cols, "Delivery");
                int etaCol = FindColumn(cols, "Confirmed ETA", "ETA", "Confirmed\nETA");

                if (tagCol < 0)
                    throw new Exception("'Tag No.' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string tagNo = row[tagCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(tagNo)) continue;
                    if (!seen.Add(tagNo)) { duplicatesSkipped++; continue; }

                    string deliveryStatus = deliveryCol >= 0 ? row[deliveryCol]?.ToString()?.Trim() ?? "" : "";
                    DateTime? eta = etaCol >= 0 ? ParseCellValue(row[etaCol]) : null;

                    var equip = new EquipmentData
                    {
                        TagNo = tagNo,
                        RfqNo = rfqCol >= 0 ? row[rfqCol]?.ToString()?.Trim() ?? "" : "",
                        SubSystem = subSysCol >= 0 ? row[subSysCol]?.ToString()?.Trim() ?? "" : "",
                        Description = descCol >= 0 ? row[descCol]?.ToString()?.Trim() ?? "" : "",
                        DeliveryStatus = deliveryStatus,
                        ConfirmedEta = eta,
                    };

                    // Delivery stage: only set date if status is "Delivered"
                    if (deliveryStatus.Equals("Delivered", StringComparison.OrdinalIgnoreCase) && eta.HasValue)
                        equip.StageDates[EquipmentStage.Delivery] = eta;

                    foreach (var (colIndex, stage) in stageColumns)
                    {
                        equip.StageDates[stage] = ParseCellValue(row[colIndex]);
                    }

                    items.Add(equip);
                }
            }

            PerfLog.Record("Excel 로드(Equipment)", sw.ElapsedMilliseconds, rows: items.Count,
                note: duplicatesSkipped > 0 ? $"중복 {duplicatesSkipped}건 제외" : "");
            return items;
        }

        public static List<EitTrayData> LoadEitTray(string filePath)
        {
            var trayHeaderNames = new[] { "Tray Number", "TrayNumber", "Tray No", "Tray No." };
            var byTray = new Dictionary<string, EitTrayData>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1;

                foreach (System.Data.DataTable dt in dataSet.Tables)
                {
                    int row = FindHeaderRowInTable(dt, trayHeaderNames);
                    if (row >= 0) { table = dt; headerRowIdx = row; break; }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Tray Number' 헤더를 포함한 시트를 찾을 수 없습니다.");

                var cols = BuildColumnMap(table, headerRowIdx);

                int trayNoCol        = FindColumn(cols, trayHeaderNames);
                int trayLthCol       = FindColumn(cols, "Tray Lth", "TrayLth", "Tray Length");
                int trayInstalledCol = FindColumn(cols, "Tray Installed", "TrayInstalled", "Installed");
                int installPctCol    = FindColumn(cols, "Install %", "Install Percent", "InstallPercent", "Install Progress");
                int trayDateCol      = FindColumn(cols, "Tray install date", "Tray Install Date", "TrayInstallDate", "Install Date");

                if (trayNoCol < 0)
                    throw new Exception("'Tray Number' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string trayNo = row[trayNoCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(trayNo)) continue;

                    // 중복 판정은 정규화 키("X"와 "X."은 같은 트레이 — 후행 '.' 장식 실측)
                    string dedupKey = EitTrayData.NormalizeId(trayNo);
                    if (byTray.ContainsKey(dedupKey)) continue;

                    var tray = new EitTrayData
                    {
                        TrayNumber = trayNo,
                        TrayLth = trayLthCol >= 0 ? ParseDouble(row[trayLthCol]) : null,
                        TrayInstalled = trayInstalledCol >= 0 ? ParseDouble(row[trayInstalledCol]) : null,
                        InstallProgress = installPctCol >= 0 ? ParsePercentage(row[installPctCol]) : null,
                        TrayInstallDate = trayDateCol >= 0 ? ParseCellValue(row[trayDateCol]) : null,
                    };
                    byTray[dedupKey] = tray;
                    order.Add(dedupKey);
                }
            }

            var result = new List<EitTrayData>(order.Count);
            foreach (var k in order) result.Add(byTray[k]);
            return result;
        }


        /// <summary>
        /// Cable(형상) 탭용 로드 — 한 행 = 한 케이블 (Cable Pull의 노드 다대다와 별개).
        /// 헤더에 "Cable No"만 있으면 되고 "Node"는 필요 없다. stage 날짜(Pulling Start/End,
        /// From/To Conn)가 있으면 날짜 기반 4단계, 전무하면 탭이 하이라이트 전용 모드로 전환한다.
        /// 길이/%는 표시 전용 (§13-6 — PULLING LTH 의미 미확정이라 색에 안 씀).
        /// </summary>
        public static List<CableLineData> LoadCable(string filePath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cableHeaders = new[] { "Cable No", "Cable No.", "CableNo", "CABLE NO" };
            var byCable = new Dictionary<string, CableLineData>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1;
                foreach (System.Data.DataTable dt in dataSet.Tables)
                {
                    int row = FindHeaderRowInTable(dt, cableHeaders);
                    if (row >= 0) { table = dt; headerRowIdx = row; break; }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Cable No' 헤더를 포함한 시트를 찾을 수 없습니다. (Cable 형상 입력 파일이 맞는지 확인하세요)");

                var cols = BuildColumnMap(table, headerRowIdx);

                int cableNoCol   = FindColumn(cols, cableHeaders);
                int pullStartCol = FindColumn(cols, "Pulling Start", "PULLING START", "Pull Start", "Pulling Start Date");
                int pullEndCol   = FindColumn(cols, "Pulling End", "PULLING END", "Pull End", "Pulling End Date");
                int fromConnCol  = FindColumn(cols, "From Conn", "FROM CONN", "From Connection", "From Conn Date");
                int toConnCol    = FindColumn(cols, "To Conn", "TO CONN", "To Connection", "To Conn Date");
                int designLthCol = FindColumn(cols, "Cable Design Lth", "CableDesignLth", "Design Lth", "DESIGN LTH");
                int pulledLthCol = FindColumn(cols, "Cable Pulled Lth", "CablePulledLth", "Pulling Lth", "PULLING LTH");
                int pullingPctCol = FindColumn(cols, "Pulling %", "Pulling Percent", "PullingPercent", "Pulling Progress");
                int fromModCol   = FindColumn(cols, "From Module", "FromModule", "FROM MODULE");
                int fromEquipCol = FindColumn(cols, "From Equip", "FromEquip", "FROM EQUIP", "From Equipment");
                int toModCol     = FindColumn(cols, "To Module", "ToModule", "TO MODULE");
                int toEquipCol   = FindColumn(cols, "To Equip", "ToEquip", "TO EQUIP", "To Equipment");
                int systemCol    = FindColumn(cols, "System", "SYSTEM", "Route Sys", "RouteSys");
                int typeCol      = FindColumn(cols, "Type", "Cable Type", "CABLE_TYPE", "CableType");
                int coreCol      = FindColumn(cols, "Core", "Cable Core", "CABLE_CORE", "CableCore");
                int sizeCol      = FindColumn(cols, "Size", "Cable Size", "CABLE_SIZE", "CableSize");
                int outDiaCol    = FindColumn(cols, "Out Dia", "OutDia", "OUT DIA", "Outer Dia");
                int traySysCol   = FindColumn(cols, "Tray Sys", "TraySys", "TRAY SYS");
                int routeCol     = FindColumn(cols, "Route", "ROUTE", "Route No");

                if (cableNoCol < 0)
                    throw new Exception("'Cable No' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string cableNo = row[cableNoCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(cableNo)) continue;
                    if (byCable.ContainsKey(cableNo)) continue;

                    var c = new CableLineData
                    {
                        CableNo = cableNo,
                        FromConnDate = fromConnCol >= 0 ? ParseCellValue(row[fromConnCol]) : null,
                        ToConnDate   = toConnCol   >= 0 ? ParseCellValue(row[toConnCol]) : null,
                        DesignLth       = designLthCol  >= 0 ? ParseDouble(row[designLthCol]) : null,
                        PulledLth       = pulledLthCol  >= 0 ? ParseDouble(row[pulledLthCol]) : null,
                        PullingProgress = pullingPctCol >= 0 ? ParsePercentage(row[pullingPctCol]) : null,
                        FromModule = fromModCol   >= 0 ? row[fromModCol]?.ToString()?.Trim() ?? "" : "",
                        FromEquip  = fromEquipCol >= 0 ? row[fromEquipCol]?.ToString()?.Trim() ?? "" : "",
                        ToModule   = toModCol     >= 0 ? row[toModCol]?.ToString()?.Trim() ?? "" : "",
                        ToEquip    = toEquipCol   >= 0 ? row[toEquipCol]?.ToString()?.Trim() ?? "" : "",
                        System     = systemCol    >= 0 ? row[systemCol]?.ToString()?.Trim() ?? "" : "",
                        Type       = typeCol      >= 0 ? row[typeCol]?.ToString()?.Trim() ?? "" : "",
                        Core       = coreCol      >= 0 ? row[coreCol]?.ToString()?.Trim() ?? "" : "",
                        Size       = sizeCol      >= 0 ? row[sizeCol]?.ToString()?.Trim() ?? "" : "",
                        OutDia     = outDiaCol    >= 0 ? row[outDiaCol]?.ToString()?.Trim() ?? "" : "",
                        TraySys    = traySysCol   >= 0 ? row[traySysCol]?.ToString()?.Trim() ?? "" : "",
                        Route      = routeCol     >= 0 ? row[routeCol]?.ToString()?.Trim() ?? "" : "",
                    };
                    c.StageDates[CableLineStage.Pulling] = pullStartCol >= 0 ? ParseCellValue(row[pullStartCol]) : null;
                    c.StageDates[CableLineStage.Pulled]  = pullEndCol   >= 0 ? ParseCellValue(row[pullEndCol]) : null;
                    c.ComputeTerminated();

                    byCable[cableNo] = c;
                    order.Add(cableNo);
                }
            }

            var result = new List<CableLineData>(order.Count);
            foreach (var k in order) result.Add(byCable[k]);
            PerfLog.Record("Excel 로드(Cable)", sw.ElapsedMilliseconds, rows: result.Count);
            return result;
        }

        /// <summary>
        /// Cable(형상) 탭 "리스트 필터"용 케이블 번호 목록 로드 — 진척 데이터가 아니라
        /// 보여줄 케이블의 부분집합만 담은 단순 리스트. "Cable No" 헤더가 있으면 그 컬럼을,
        /// 없으면 첫 시트 첫 컬럼의 비어있지 않은 값을 케이블 번호로 읽는다 (관대한 파싱 —
        /// 현장에서 아무 목록이나 붙여넣은 파일 수용). 중복 제거, 순서 유지.
        /// </summary>
        public static List<string> LoadCableNoList(string filePath)
        {
            var headerNames = new[] { "Cable No", "Cable No.", "CableNo", "CABLE NO", "Cable Number" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                System.Data.DataTable table = null;
                int headerRowIdx = -1, col = 0;

                foreach (System.Data.DataTable dt in dataSet.Tables)
                {
                    int row = FindHeaderRowInTable(dt, headerNames);
                    if (row >= 0)
                    {
                        table = dt;
                        headerRowIdx = row;
                        col = FindColumn(BuildColumnMap(dt, row), headerNames);
                        break;
                    }
                }

                if (table == null)
                {
                    // 헤더 없는 맨 목록 — 첫 시트 첫 컬럼 전체
                    if (dataSet.Tables.Count == 0) return result;
                    table = dataSet.Tables[0];
                    headerRowIdx = -1;
                    col = 0;
                }

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    if (col < 0 || col >= table.Columns.Count) break;
                    string no = table.Rows[r][col]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(no)) continue;
                    if (seen.Add(no)) result.Add(no);
                }
            }

            return result;
        }

        /// <summary>
        /// Sub-system 형상 전용 Excel — 정식 진척이 아니라 "sub-system별 형상만 보여주기 위한" 목록.
        /// 하나의 엑셀에 시트명에 키워드 Hydrotest/MEQ/Cable이 '포함'된 시트로 각 공종
        /// (ID, Sub-system) 표가 들어 있다 (정확 일치 아님 — "01_Hydrotest", "MEQ_List" 등도 매칭):
        ///   Hydrotest → Piping (Test Package No.) / MEQ → Equipment (Tag No.) / Cable → Cable (Cable No)
        /// 각 시트에서 ID·Sub-system 컬럼만 읽어 날짜 없는 SubSystemElement(FromBare)로 만든다.
        /// 시트/컬럼이 없으면 그 공종만 조용히 건너뛰고 <paramref name="notes"/>에 사유를 남긴다.
        /// 한 시트가 여러 키워드를 포함해 중복 소비되지 않도록 이미 쓴 시트는 제외한다.
        /// </summary>
        public static List<SubSystemElement> LoadSubSystemShapes(string filePath, out List<string> notes)
        {
            notes = new List<string>();
            var result = new List<SubSystemElement>();
            var subNames = new[] { "Sub-system", "Sub-System", "SubSystem", "Sub System", "SUB-SYSTEM", "Subsystem" };
            // Keyword = 시트명에 포함되면 매칭할 키워드 (정확 일치 아님).
            var specs = new[]
            {
                new { Keyword = "Hydrotest", Disc = SubSystemDiscipline.Piping,
                      IdNames = new[] { "Test Package No.", "Test Package No", "TestPkgId", "Test Pkg No", "PKGNO" } },
                new { Keyword = "MEQ", Disc = SubSystemDiscipline.Equipment,
                      IdNames = new[] { "Tag No.", "Tag No", "TagNo", "TAG NO", "TAG" } },
                new { Keyword = "Cable", Disc = SubSystemDiscipline.Cable,
                      IdNames = new[] { "Cable No", "Cable No.", "CableNo", "CABLE NO", "Cable Number" } },
            };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();
                var usedSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var spec in specs)
                {
                    // 시트명이 키워드와 정확히 같지 않고 '포함'만 해도 매칭. 이미 다른 키워드로
                    // 소비된 시트는 제외해 한 시트가 두 공종으로 중복 소비되는 것을 막는다.
                    var table = dataSet.Tables.Cast<System.Data.DataTable>()
                        .FirstOrDefault(t => !usedSheets.Contains(t.TableName ?? "")
                            && (t.TableName ?? "").IndexOf(spec.Keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (table == null) { notes.Add($"{spec.Keyword} 시트 없음"); continue; }
                    usedSheets.Add(table.TableName ?? "");

                    int headerRow = FindHeaderRowInTable(table, spec.IdNames);
                    if (headerRow < 0) { notes.Add($"{spec.Keyword}: ID 헤더 없음"); continue; }

                    var cols = BuildColumnMap(table, headerRow);
                    int idCol = FindColumn(cols, spec.IdNames);
                    int subCol = FindColumn(cols, subNames);
                    if (idCol < 0 || subCol < 0) { notes.Add($"{spec.Keyword}: 컬럼 부족"); continue; }

                    int added = 0;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int r = headerRow + 1; r < table.Rows.Count; r++)
                    {
                        string id = table.Rows[r][idCol]?.ToString()?.Trim();
                        string sub = table.Rows[r][subCol]?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sub)) continue;
                        if (!seen.Add(id)) continue;   // 시트 내 중복 ID는 첫 행만
                        result.Add(SubSystemElement.FromBare(id, sub, spec.Disc));
                        added++;
                    }
                    notes.Add($"{table.TableName}({spec.Keyword}) {added}건");
                }
            }
            return result;
        }

        #region Shared helpers

        private static Dictionary<string, int> BuildColumnMap(System.Data.DataTable table, int headerRowIdx)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headerRow = table.Rows[headerRowIdx];
            for (int c = 0; c < table.Columns.Count; c++)
            {
                string val = headerRow[c]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val))
                    cols[val] = c;
            }
            return cols;
        }

        private static int FindColumn(Dictionary<string, int> cols, params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (cols.TryGetValue(name, out int col))
                    return col;
            }
            return -1;
        }

        private static int FindHeaderRowInTable(System.Data.DataTable table, string[] candidates)
        {
            var candidateSet = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
            int maxRows = Math.Min(20, table.Rows.Count);
            for (int r = 0; r < maxRows; r++)
            {
                var row = table.Rows[r];
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string val = row[c]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val) && candidateSet.Contains(val))
                        return r;
                }
            }
            return -1;
        }

        /// <summary>Header row must contain at least one value from EACH candidate group (AND, not OR).</summary>
        private static int FindHeaderRowMatchingAll(System.Data.DataTable table, params string[][] candidateGroups)
        {
            var sets = candidateGroups
                .Select(g => new HashSet<string>(g, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            int maxRows = Math.Min(20, table.Rows.Count);
            for (int r = 0; r < maxRows; r++)
            {
                var row = table.Rows[r];
                bool[] matched = new bool[sets.Length];
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string val = row[c]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(val)) continue;
                    for (int s = 0; s < sets.Length; s++)
                        if (!matched[s] && sets[s].Contains(val)) matched[s] = true;
                }
                if (matched.All(m => m)) return r;
            }
            return -1;
        }

        private static int? ParseInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is decimal m) return (int)m;
            string str = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(str)) return null;
            return int.TryParse(str, out var parsed) ? parsed : (int?)null;
        }

        private static DateTime? ParseCellValue(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is DateTime dt) return dt;
            string str = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(str)) return null;
            return DateTime.TryParse(str, out var parsed) ? parsed : (DateTime?)null;
        }

        private static double? ParseDouble(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is decimal m) return (double)m;
            string str = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(str)) return null;
            return double.TryParse(str, out var parsed) ? parsed : (double?)null;
        }

        /// <summary>Parse percentage cell: "100%" → 1.0, 0.19 → 0.19, "19%" → 0.19.</summary>
        private static double? ParsePercentage(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is double d) return d > 1.0 ? d / 100.0 : d;
            if (value is float f) return f > 1.0f ? f / 100.0 : f;
            if (value is int i) return i > 1 ? i / 100.0 : i;
            if (value is decimal m) { double dv = (double)m; return dv > 1.0 ? dv / 100.0 : dv; }
            string str = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(str)) return null;
            bool hasPct = str.EndsWith("%");
            if (hasPct) str = str.Substring(0, str.Length - 1).Trim();
            if (!double.TryParse(str, out var parsed)) return null;
            return hasPct || parsed > 1.0 ? parsed / 100.0 : parsed;
        }

        #endregion
    }
}
