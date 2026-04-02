using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
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

        public static List<SpoolData> LoadSpool(string filePath)
        {
            var spools = new List<SpoolData>();

            using (var wb = new XLWorkbook(filePath))
            {
                var sheet = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Spool", StringComparison.OrdinalIgnoreCase))
                    ?? wb.Worksheets.First();
                var cols = GetHeaderMap(sheet);

                // Build mapping: column index → SpoolStage (for date columns)
                var stageColumns = new List<(int colIndex, SpoolStage stage)>();
                foreach (var kv in SpoolStageInfo.ColumnMap)
                {
                    if (cols.TryGetValue(kv.Key, out int colIdx))
                        stageColumns.Add((colIdx, kv.Value));
                }

                // Find Spool Number and ISO No columns (try multiple header names)
                int spoolCol = FindColumn(cols, "Spool Number", "SpoolId", "Spool No", "SpoolNumber");
                int isoCol = FindColumn(cols, "ISO No", "IsoNo", "ISO", "ISO Number");

                if (spoolCol < 0)
                    throw new Exception("'Spool Number' 컬럼을 찾을 수 없습니다.");

                foreach (var row in sheet.RangeUsed().RowsUsed().Skip(1))
                {
                    string spoolId = row.WorksheetRow().Cell(spoolCol).GetString()?.Trim();
                    if (string.IsNullOrEmpty(spoolId)) continue;

                    string isoNo = isoCol > 0
                        ? row.WorksheetRow().Cell(isoCol).GetString()?.Trim()
                        : string.Empty;

                    var spool = new SpoolData
                    {
                        SpoolId = spoolId,
                        IsoNo = isoNo,
                    };

                    foreach (var (colIndex, stage) in stageColumns)
                    {
                        var cell = row.WorksheetRow().Cell(colIndex);
                        spool.StageDates[stage] = ParseCellDate(cell);
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

        private static DateTime? ParseCellDate(IXLCell cell)
        {
            if (cell.IsEmpty()) return null;

            // Try reading as DateTime directly (Excel stores dates as numbers)
            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime();

            // Try parsing string value
            string val = cell.GetString()?.Trim();
            if (string.IsNullOrEmpty(val)) return null;
            return DateTime.TryParse(val, out var dt) ? dt : (DateTime?)null;
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
    }
}
