using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ExcelDataReader;
using NavisVisualizer.Models;

namespace NavisVisualizer.Loaders
{
    public static class ExcelLoader
    {
        public static List<TestPackageData> LoadHydrotest(string filePath)
        {
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

            return packages;
        }

        public static List<SpoolData> LoadSpool(string filePath)
        {
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

            return spools;
        }

        public static List<EquipmentData> LoadEquipment(string filePath)
        {
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

                int trayNoCol       = FindColumn(cols, trayHeaderNames);
                int trayTypeCol     = FindColumn(cols, "Tray type", "Tray Type", "TrayType");
                int trayMrCol       = FindColumn(cols, "Tray MR", "TrayMR");
                int trayCompMrCol   = FindColumn(cols, "Tray Complete MR", "Tray Comp MR", "TrayCompleteMR");
                int trayProgCol     = FindColumn(cols, "Tray Progress", "TrayProgress");
                int trayDateCol     = FindColumn(cols, "Tray install date", "Tray Install Date", "TrayInstallDate", "Install Date");
                int routeCol        = FindColumn(cols, "Route Number", "RouteNumber", "Route No", "Route");
                int cableAssumeCol  = FindColumn(cols, "Cable Assume lth", "Cable Assume Lth", "Cable Assume", "CableAssumeLth");
                int cablePullCol    = FindColumn(cols, "Cable Pull lth", "Cable Pull Lth", "Cable Pull", "CablePullLth");
                int cableProgCol    = FindColumn(cols, "Cable Progress", "CableProgress");

                if (trayNoCol < 0)
                    throw new Exception("'Tray Number' 컬럼을 찾을 수 없습니다.");

                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string trayNo = row[trayNoCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(trayNo)) continue;

                    if (!byTray.TryGetValue(trayNo, out var tray))
                    {
                        tray = new EitTrayData
                        {
                            TrayNumber = trayNo,
                            TrayType = trayTypeCol >= 0 ? row[trayTypeCol]?.ToString()?.Trim() ?? "" : "",
                            TrayMr = trayMrCol >= 0 ? ParseDouble(row[trayMrCol]) : null,
                            TrayCompleteMr = trayCompMrCol >= 0 ? ParseDouble(row[trayCompMrCol]) : null,
                            TrayProgress = trayProgCol >= 0 ? ParsePercentage(row[trayProgCol]) : null,
                            TrayInstallDate = trayDateCol >= 0 ? ParseCellValue(row[trayDateCol]) : null,
                        };
                        byTray[trayNo] = tray;
                        order.Add(trayNo);
                    }

                    string routeNo = routeCol >= 0 ? row[routeCol]?.ToString()?.Trim() ?? "" : "";
                    double? assume = cableAssumeCol >= 0 ? ParseDouble(row[cableAssumeCol]) : null;
                    double? pull = cablePullCol >= 0 ? ParseDouble(row[cablePullCol]) : null;
                    double? cProg = cableProgCol >= 0 ? ParsePercentage(row[cableProgCol]) : null;

                    if (!string.IsNullOrEmpty(routeNo) || assume.HasValue || pull.HasValue || cProg.HasValue)
                    {
                        tray.Cables.Add(new EitCableRecord
                        {
                            RouteNumber = routeNo,
                            AssumeLength = assume,
                            PullLength = pull,
                            Progress = cProg,
                        });
                    }
                }
            }

            var result = new List<EitTrayData>(order.Count);
            foreach (var k in order) result.Add(byTray[k]);
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
