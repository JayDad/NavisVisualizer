using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using System.IO;
using System.Linq;

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
        public void LoadSpool_ParsesAllStages()
        {
            var path = Path.Combine(TestDataDir, "spool_sample.xlsx");
            var spools = ExcelLoader.LoadSpool(path);

            Assert.AreEqual(4, spools.Count);
            Assert.AreEqual(SpoolStage.Installed,   spools.First(s => s.SpoolId == "SP-001-A").Stage);
            Assert.AreEqual(SpoolStage.HandOver,    spools.First(s => s.SpoolId == "SP-001-B").Stage);
            Assert.AreEqual(SpoolStage.Fabricating, spools.First(s => s.SpoolId == "SP-002-A").Stage);
            Assert.AreEqual(SpoolStage.NotStarted,  spools.First(s => s.SpoolId == "SP-003-A").Stage);
        }
    }
}
