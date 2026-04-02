using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ExcelDataReader;
using NavisVisualizer.Models;

namespace NavisVisualizer.Loaders
{
    public static class ExcelLoader
    {
        public static List<TestPackageData> LoadHydrotest(string filePath)
        {
            var packages = new Dictionary<string, TestPackageData>(StringComparer.OrdinalIgnoreCase);

            using (var wb = new XLWorkbook(filePath))
            {
                var statusSheet = wb.Worksheet("Status")
                    ?? throw new Exception("'Status' 시트를 찾을 수 없습니다.");
                var statusCols = GetHeaderMap(statusSheet);

                foreach (var row in statusSheet.RangeUsed().RowsUsed().Skip(1))
                {
                    string pkgId = GetCell(row, statusCols, "TestPkgId").Trim();
                    if (string.IsNullOrEmpty(pkgId)) continue;

                    packages[pkgId] = new TestPackageData
                    {
                        TestPkgId = pkgId,
                        Status = ParseHydrotestStatus(GetCell(row, statusCols, "Status")),
                        PlannedDate = ParseDate(GetCell(row, statusCols, "PlannedDate")),
                        ActualDate = ParseDate(GetCell(row, statusCols, "ActualDate")),
                        System = GetCell(row, statusCols, "System"),
                        Remarks = GetCell(row, statusCols, "Remarks"),
                    };
                }

                var mappingSheet = wb.Worksheet("Mapping")
                    ?? throw new Exception("'Mapping' 시트를 찾을 수 없습니다.");
                var mappingCols = GetHeaderMap(mappingSheet);

                foreach (var row in mappingSheet.RangeUsed().RowsUsed().Skip(1))
                {
                    string pkgId = GetCell(row, mappingCols, "TestPkgId").Trim();
                    string spoolId = GetCell(row, mappingCols, "SpoolId").Trim();

                    if (packages.TryGetValue(pkgId, out var pkg) && !string.IsNullOrEmpty(spoolId))
                        pkg.SpoolIds.Add(spoolId);
                }
            }

            return packages.Values.ToList();
        }

        /// <summary>
        /// Loads spool data from Excel files (.xlsx, .xls, .xlsb).
        /// Uses ExcelDataReader for broad format support.
        /// </summary>
        public static List<SpoolData> LoadSpool(string filePath)
        {
            var spools = new List<SpoolData>();
            var spoolHeaderNames = new[] { "Spool Number", "SpoolId", "Spool No", "SpoolNumber" };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet();

                // Find sheet and header row
                System.Data.DataTable table = null;
                int headerRowIdx = -1;

                // Prefer "Spool" named sheet
                var namedTable = dataSet.Tables.Cast<System.Data.DataTable>()
                    .FirstOrDefault(t => t.TableName.Equals("Spool", StringComparison.OrdinalIgnoreCase));
                if (namedTable != null)
                {
                    headerRowIdx = FindHeaderRowInTable(namedTable, spoolHeaderNames);
                    if (headerRowIdx >= 0) table = namedTable;
                }

                // Otherwise scan all sheets
                if (table == null)
                {
                    foreach (System.Data.DataTable dt in dataSet.Tables)
                    {
                        int row = FindHeaderRowInTable(dt, spoolHeaderNames);
                        if (row >= 0)
                        {
                            table = dt;
                            headerRowIdx = row;
                            break;
                        }
                    }
                }

                if (table == null || headerRowIdx < 0)
                    throw new Exception("'Spool Number' 헤더를 포함한 시트를 찾을 수 없습니다.");

                // Build column map from header row
                var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var headerRow = table.Rows[headerRowIdx];
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string val = headerRow[c]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val))
                        cols[val] = c;
                }

                // Stage columns
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

                // Read data rows
                for (int r = headerRowIdx + 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string spoolId = row[spoolCol]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(spoolId)) continue;

                    string isoNo = isoCol >= 0 ? row[isoCol]?.ToString()?.Trim() : string.Empty;

                    var spool = new SpoolData
                    {
                        SpoolId = spoolId,
                        IsoNo = isoNo ?? string.Empty,
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

        #region Hydrotest helpers (ClosedXML)

        private static Dictionary<string, int> GetHeaderMap(IXLWorksheet sheet)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headerRow = sheet.Row(1);
            for (int i = 1; i <= headerRow.LastCellUsed().Address.ColumnNumber; i++)
            {
                string header = headerRow.Cell(i).GetString().Trim();
                if (!string.IsNullOrEmpty(header))
                    map[header] = i;
            }
            return map;
        }

        private static string GetCell(IXLRangeRow row, Dictionary<string, int> cols, string header)
        {
            if (!cols.TryGetValue(header, out int col)) return string.Empty;
            return row.WorksheetRow().Cell(col).GetString() ?? string.Empty;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, out var dt) ? dt : (DateTime?)null;
        }

        private static HydrotestStatus ParseHydrotestStatus(string value)
        {
            switch (value?.Trim().ToUpperInvariant())
            {
                case "C": case "완료": case "COMPLETED": return HydrotestStatus.Completed;
                case "R": case "복구": case "RECOVERY":  return HydrotestStatus.Recovery;
                default:                                  return HydrotestStatus.NotStarted;
            }
        }

        #endregion
    }
}
