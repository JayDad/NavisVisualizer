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
                var spoolHeaderNames = new[] { "Spool Number", "SpoolId", "Spool No", "SpoolNumber" };

                // Find the right sheet: prefer "Spool" name, otherwise scan all sheets for the header
                IXLWorksheet sheet = null;
                int headerRowNum = -1;

                var namedSheet = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Spool", StringComparison.OrdinalIgnoreCase));
                if (namedSheet != null)
                {
                    headerRowNum = FindHeaderRow(namedSheet, spoolHeaderNames);
                    if (headerRowNum >= 0) sheet = namedSheet;
                }

                if (sheet == null)
                {
                    foreach (var ws in wb.Worksheets)
                    {
                        int row = FindHeaderRow(ws, spoolHeaderNames);
                        if (row >= 0)
                        {
                            sheet = ws;
                            headerRowNum = row;
                            break;
                        }
                    }
                }

                if (sheet == null || headerRowNum < 0)
                    throw new Exception("'Spool Number' 헤더를 포함한 시트를 찾을 수 없습니다.");

                var cols = GetHeaderMapAt(sheet, headerRowNum);

                // Build mapping: column index → SpoolStage (for date columns)
                var stageColumns = new List<(int colIndex, SpoolStage stage)>();
                foreach (var kv in SpoolStageInfo.ColumnMap)
                {
                    if (cols.TryGetValue(kv.Key, out int colIdx))
                        stageColumns.Add((colIdx, kv.Value));
                }

                int spoolCol = FindColumn(cols, "Spool Number", "SpoolId", "Spool No", "SpoolNumber");
                int isoCol = FindColumn(cols, "ISO No", "IsoNo", "ISO", "ISO Number");

                // Read data rows starting after the header row
                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRowNum;
                for (int r = headerRowNum + 1; r <= lastRow; r++)
                {
                    var wsRow = sheet.Row(r);
                    string spoolId = wsRow.Cell(spoolCol).GetString()?.Trim();
                    if (string.IsNullOrEmpty(spoolId)) continue;

                    string isoNo = isoCol > 0
                        ? wsRow.Cell(isoCol).GetString()?.Trim()
                        : string.Empty;

                    var spool = new SpoolData
                    {
                        SpoolId = spoolId,
                        IsoNo = isoNo,
                    };

                    foreach (var (colIndex, stage) in stageColumns)
                    {
                        var cell = wsRow.Cell(colIndex);
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

        /// <summary>
        /// Scans rows top-down to find the row containing a header cell matching one of the candidates.
        /// Skips merged cells and category rows (e.g. "Spool Fab").
        /// </summary>
        private static int FindHeaderRow(IXLWorksheet sheet, params string[] candidates)
        {
            var candidateSet = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
            int maxScanRows = Math.Min(20, sheet.LastRowUsed()?.RowNumber() ?? 1);
            int maxCols = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;

            for (int r = 1; r <= maxScanRows; r++)
            {
                var row = sheet.Row(r);
                for (int c = 1; c <= maxCols; c++)
                {
                    string val = row.Cell(c).GetString()?.Trim();
                    if (!string.IsNullOrEmpty(val) && candidateSet.Contains(val))
                        return r;
                }
            }
            return -1;
        }

        /// <summary>
        /// Builds header→column map from a specific row number.
        /// </summary>
        private static Dictionary<string, int> GetHeaderMapAt(IXLWorksheet sheet, int rowNumber)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var row = sheet.Row(rowNumber);
            int maxCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
            for (int i = 1; i <= maxCol; i++)
            {
                string header = row.Cell(i).GetString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                    map[header] = i;
            }
            return map;
        }

        private static Dictionary<string, int> GetHeaderMap(IXLWorksheet sheet)
        {
            return GetHeaderMapAt(sheet, 1);
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
