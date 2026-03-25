using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using NavisVisualizer.Models;

namespace NavisVisualizer.Loaders
{
    public static class ExcelLoader
    {
        public const string SpoolPropertyCategory = "Element";
        public const string SpoolPropertyName = "Id";

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
                var sheet = wb.Worksheet("Spool")
                    ?? throw new Exception("'Spool' 시트를 찾을 수 없습니다.");
                var cols = GetHeaderMap(sheet);

                foreach (var row in sheet.RangeUsed().RowsUsed().Skip(1))
                {
                    string spoolId = GetCell(row, cols, "SpoolId").Trim();
                    if (string.IsNullOrEmpty(spoolId)) continue;

                    spools.Add(new SpoolData
                    {
                        SpoolId = spoolId,
                        Stage = ParseSpoolStage(GetCell(row, cols, "Stage")),
                        PlannedDate = ParseDate(GetCell(row, cols, "PlannedDate")),
                        ActualDate = ParseDate(GetCell(row, cols, "ActualDate")),
                        Remarks = GetCell(row, cols, "Remarks"),
                    });
                }
            }

            return spools;
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

        private static string GetCell(IXLRow row, Dictionary<string, int> cols, string header)
        {
            if (!cols.TryGetValue(header, out int col)) return string.Empty;
            return row.Cell(col).GetString() ?? string.Empty;
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

        private static SpoolStage ParseSpoolStage(string value)
        {
            switch (value?.Trim().ToUpperInvariant())
            {
                case "FAB":  case "제작중":    return SpoolStage.Fabricating;
                case "FABC": case "제작완료":  return SpoolStage.FabCompleted;
                case "HO":   case "HAND-OVER": case "HANDOVER": return SpoolStage.HandOver;
                case "LD":   case "LOADED":    return SpoolStage.Loaded;
                case "INST": case "설치완":    return SpoolStage.Installed;
                default:                       return SpoolStage.NotStarted;
            }
        }
    }
}
