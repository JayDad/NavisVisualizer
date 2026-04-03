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

        #endregion
    }
}
