using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;

namespace NavisVisualizer.Tests
{
    [TestClass]
    public class ExcelLoaderTests
    {
        private static string TestDataDir =>
            Path.Combine(Path.GetDirectoryName(typeof(ExcelLoaderTests).Assembly.Location),
                         "TestData");

        [TestMethod]
        public void LoadHydrotest_ParsesStatusSheet()
        {
            var path = Path.Combine(TestDataDir, "hydrotest_sample.xlsx");
            var packages = ExcelLoader.LoadHydrotest(path);

            Assert.AreEqual(3, packages.Count);
            Assert.AreEqual(HydrotestStatus.Completed,  packages.First(p => p.TestPkgId == "HTP-001").Status);
            Assert.AreEqual(HydrotestStatus.NotStarted, packages.First(p => p.TestPkgId == "HTP-002").Status);
            Assert.AreEqual(HydrotestStatus.Recovery,   packages.First(p => p.TestPkgId == "HTP-003").Status);
        }

        [TestMethod]
        public void LoadHydrotest_ParsesMappingSheet()
        {
            var path = Path.Combine(TestDataDir, "hydrotest_sample.xlsx");
            var packages = ExcelLoader.LoadHydrotest(path);

            var htp001 = packages.First(p => p.TestPkgId == "HTP-001");
            Assert.AreEqual(2, htp001.SpoolIds.Count);
            CollectionAssert.Contains(htp001.SpoolIds, "SP-001-A");
            CollectionAssert.Contains(htp001.SpoolIds, "SP-001-B");
        }

        [TestMethod]
        public void LoadSpool_ParsesStageDates()
        {
            var path = CreateSpoolTestFile();
            try
            {
                var spools = ExcelLoader.LoadSpool(path);

                Assert.AreEqual(3, spools.Count);

                var sp1 = spools.First(s => s.SpoolId == "CD11-OF-29248-101");
                Assert.AreEqual("CD11-OF-29248-1", sp1.IsoNo);
                Assert.AreEqual(new DateTime(2025, 5, 8), sp1.StageDates[SpoolStage.BV]);
                Assert.AreEqual(new DateTime(2025, 5, 28), sp1.StageDates[SpoolStage.FitUp]);
                Assert.AreEqual(new DateTime(2025, 7, 1), sp1.StageDates[SpoolStage.HandOver]);
                Assert.AreEqual(new DateTime(2025, 7, 10), sp1.StageDates[SpoolStage.Setting]);

                // Stage computation at different dates
                Assert.AreEqual(SpoolStage.NotStarted, sp1.GetStageAtDate(new DateTime(2025, 5, 1)));
                Assert.AreEqual(SpoolStage.BV, sp1.GetStageAtDate(new DateTime(2025, 5, 8)));
                Assert.AreEqual(SpoolStage.HandOver, sp1.GetStageAtDate(new DateTime(2025, 7, 5)));
                Assert.AreEqual(SpoolStage.Welding, sp1.GetStageAtDate(new DateTime(2025, 9, 1)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void LoadSpool_HandlesEmptyDates()
        {
            var path = CreateSpoolTestFileWithGaps();
            try
            {
                var spools = ExcelLoader.LoadSpool(path);
                var sp = spools.First();

                Assert.IsTrue(sp.StageDates[SpoolStage.BV].HasValue);
                Assert.IsFalse(sp.StageDates.ContainsKey(SpoolStage.FitUp) && sp.StageDates[SpoolStage.FitUp].HasValue);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateSpoolTestFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"spool_test_{Guid.NewGuid()}.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Spool");
                // Headers
                ws.Cell(1, 1).Value = "Spool Number";
                ws.Cell(1, 2).Value = "ISO No";
                ws.Cell(1, 3).Value = "B/V";
                ws.Cell(1, 4).Value = "F/up";
                ws.Cell(1, 5).Value = "W/D";
                ws.Cell(1, 6).Value = "NDE";
                ws.Cell(1, 7).Value = "PWHT";
                ws.Cell(1, 8).Value = "S/out";
                ws.Cell(1, 9).Value = "G-후공정인계";
                ws.Cell(1, 10).Value = "Galv2";
                ws.Cell(1, 11).Value = "Pnt1";
                ws.Cell(1, 12).Value = "Pnt2";
                ws.Cell(1, 13).Value = "Stock";
                ws.Cell(1, 14).Value = "H/O일자";
                ws.Cell(1, 15).Value = "Setting";
                ws.Cell(1, 16).Value = "Welding";

                // Row 1: CD11-OF-29248-101
                ws.Cell(2, 1).Value = "CD11-OF-29248-101";
                ws.Cell(2, 2).Value = "CD11-OF-29248-1";
                ws.Cell(2, 3).Value = new DateTime(2025, 5, 8);
                ws.Cell(2, 4).Value = new DateTime(2025, 5, 28);
                ws.Cell(2, 5).Value = new DateTime(2025, 5, 29);
                ws.Cell(2, 6).Value = new DateTime(2025, 6, 17);
                ws.Cell(2, 7).Value = new DateTime(2025, 6, 19);
                ws.Cell(2, 8).Value = new DateTime(2025, 6, 19);
                ws.Cell(2, 9).Value = new DateTime(2025, 6, 20);
                ws.Cell(2, 10).Value = new DateTime(2025, 6, 20);
                ws.Cell(2, 11).Value = new DateTime(2025, 6, 23);
                ws.Cell(2, 12).Value = new DateTime(2025, 6, 23);
                ws.Cell(2, 13).Value = new DateTime(2025, 6, 28);
                ws.Cell(2, 14).Value = new DateTime(2025, 7, 1);
                ws.Cell(2, 15).Value = new DateTime(2025, 7, 10);
                ws.Cell(2, 16).Value = new DateTime(2025, 8, 23);

                // Row 2: CD11-OF-29248-102
                ws.Cell(3, 1).Value = "CD11-OF-29248-102";
                ws.Cell(3, 2).Value = "CD11-OF-29248-1";
                ws.Cell(3, 3).Value = new DateTime(2025, 5, 8);
                ws.Cell(3, 4).Value = new DateTime(2025, 5, 14);
                ws.Cell(3, 5).Value = new DateTime(2025, 5, 15);
                ws.Cell(3, 6).Value = new DateTime(2025, 5, 23);
                ws.Cell(3, 7).Value = new DateTime(2025, 5, 23);
                ws.Cell(3, 8).Value = new DateTime(2025, 5, 23);
                ws.Cell(3, 9).Value = new DateTime(2025, 5, 27);
                ws.Cell(3, 10).Value = new DateTime(2025, 5, 27);
                ws.Cell(3, 11).Value = new DateTime(2025, 6, 5);
                ws.Cell(3, 12).Value = new DateTime(2025, 6, 5);
                ws.Cell(3, 13).Value = new DateTime(2025, 6, 5);
                ws.Cell(3, 14).Value = new DateTime(2025, 6, 24);
                ws.Cell(3, 15).Value = new DateTime(2025, 7, 3);
                ws.Cell(3, 16).Value = new DateTime(2025, 8, 13);

                // Row 3: CD11-OF-29248-103 (partial - no Setting/Welding)
                ws.Cell(4, 1).Value = "CD11-OF-29248-103";
                ws.Cell(4, 2).Value = "CD11-OF-29248-1";
                ws.Cell(4, 3).Value = new DateTime(2025, 5, 8);
                ws.Cell(4, 4).Value = new DateTime(2025, 5, 14);
                ws.Cell(4, 5).Value = new DateTime(2025, 5, 15);
                ws.Cell(4, 6).Value = new DateTime(2025, 5, 23);
                ws.Cell(4, 7).Value = new DateTime(2025, 5, 23);
                ws.Cell(4, 8).Value = new DateTime(2025, 5, 23);
                ws.Cell(4, 9).Value = new DateTime(2025, 5, 27);
                ws.Cell(4, 10).Value = new DateTime(2025, 5, 27);
                ws.Cell(4, 11).Value = new DateTime(2025, 6, 5);
                ws.Cell(4, 12).Value = new DateTime(2025, 6, 5);
                ws.Cell(4, 13).Value = new DateTime(2025, 6, 5);
                ws.Cell(4, 14).Value = new DateTime(2025, 6, 24);
                // Setting and Welding left empty

                wb.SaveAs(path);
            }
            return path;
        }

        [TestMethod]
        public void LoadSpool_SkipsDuplicateIds_FirstRowWins()
        {
            var path = CreateSpoolTestFileWithDuplicates();
            try
            {
                var spools = ExcelLoader.LoadSpool(path, out int duplicatesSkipped);

                Assert.AreEqual(1, spools.Count);
                Assert.AreEqual(1, duplicatesSkipped);
                // 첫 행 유지 정책 (OASIS 로더와 동일) — 두 번째 행의 ISO는 무시된다.
                Assert.AreEqual("ISO-FIRST", spools[0].IsoNo);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateSpoolTestFileWithDuplicates()
        {
            var path = Path.Combine(Path.GetTempPath(), $"spool_dup_{Guid.NewGuid()}.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Spool");
                ws.Cell(1, 1).Value = "Spool Number";
                ws.Cell(1, 2).Value = "ISO No";

                ws.Cell(2, 1).Value = "DUP-001";
                ws.Cell(2, 2).Value = "ISO-FIRST";
                ws.Cell(3, 1).Value = "DUP-001";
                ws.Cell(3, 2).Value = "ISO-SECOND";

                wb.SaveAs(path);
            }
            return path;
        }

        private static string CreateSpoolTestFileWithGaps()
        {
            var path = Path.Combine(Path.GetTempPath(), $"spool_gaps_{Guid.NewGuid()}.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Spool");
                ws.Cell(1, 1).Value = "Spool Number";
                ws.Cell(1, 2).Value = "ISO No";
                ws.Cell(1, 3).Value = "B/V";
                ws.Cell(1, 4).Value = "F/up";
                ws.Cell(1, 5).Value = "W/D";

                ws.Cell(2, 1).Value = "TEST-001";
                ws.Cell(2, 2).Value = "TEST-ISO-1";
                ws.Cell(2, 3).Value = new DateTime(2025, 5, 1);
                // F/up empty
                ws.Cell(2, 5).Value = new DateTime(2025, 5, 20);

                wb.SaveAs(path);
            }
            return path;
        }
    }
}
