using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Models;
using System.Drawing;

namespace NavisVisualizer.Tests
{
    [TestClass]
    public class DataModelsTests
    {
        [TestMethod]
        public void ColorSetting_DefaultsAreCorrect_ForHydrotestCompleted()
        {
            var setting = ColorSetting.HydrotestDefaults[HydrotestStatus.Completed];
            Assert.AreEqual(0.0, setting.Transparency);
            Assert.AreEqual(34, setting.DisplayColor.R);
            Assert.AreEqual(139, setting.DisplayColor.G);
            Assert.AreEqual(34, setting.DisplayColor.B);
        }

        [TestMethod]
        public void ColorSetting_SpoolDefaults_HasAllStages()
        {
            var defaults = ColorSetting.SpoolDefaults;
            Assert.AreEqual(15, defaults.Count); // NotStarted + 14 stages
            foreach (SpoolStage stage in Enum.GetValues(typeof(SpoolStage)))
            {
                Assert.IsTrue(defaults.ContainsKey(stage), $"Missing color for {stage}");
            }
        }

        [TestMethod]
        public void TestPackageData_DefaultSpoolIdsIsEmptyList()
        {
            var pkg = new TestPackageData();
            Assert.IsNotNull(pkg.SpoolIds);
            Assert.AreEqual(0, pkg.SpoolIds.Count);
        }

        [TestMethod]
        public void ColorSetting_Clone_IsDeepCopy()
        {
            var original = new ColorSetting { DisplayColor = Color.Red, Transparency = 0.5 };
            var clone = original.Clone();
            clone.Transparency = 0.9;
            Assert.AreEqual(0.5, original.Transparency); // original unchanged
        }

        [TestMethod]
        public void SpoolData_GetStageAtDate_ReturnsNotStarted_WhenNoDates()
        {
            var spool = new SpoolData { SpoolId = "TEST-001" };
            Assert.AreEqual(SpoolStage.NotStarted, spool.GetStageAtDate(DateTime.Today));
        }

        [TestMethod]
        public void SpoolData_GetStageAtDate_ReturnsLatestCompletedStage()
        {
            var spool = new SpoolData
            {
                SpoolId = "TEST-001",
                StageDates = new Dictionary<SpoolStage, DateTime?>
                {
                    [SpoolStage.BV]       = new DateTime(2025, 5, 1),
                    [SpoolStage.FitUp]    = new DateTime(2025, 5, 10),
                    [SpoolStage.WeldDone] = new DateTime(2025, 5, 20),
                    [SpoolStage.NDE]      = new DateTime(2025, 6, 1),
                }
            };

            // Before any stage
            Assert.AreEqual(SpoolStage.NotStarted, spool.GetStageAtDate(new DateTime(2025, 4, 30)));
            // After BV but before FitUp
            Assert.AreEqual(SpoolStage.BV, spool.GetStageAtDate(new DateTime(2025, 5, 5)));
            // After WeldDone but before NDE
            Assert.AreEqual(SpoolStage.WeldDone, spool.GetStageAtDate(new DateTime(2025, 5, 25)));
            // After all stages
            Assert.AreEqual(SpoolStage.NDE, spool.GetStageAtDate(new DateTime(2025, 7, 1)));
        }

        [TestMethod]
        public void SpoolData_GetStageAtDate_HandlesGaps()
        {
            // Some stages might not have dates (skipped)
            var spool = new SpoolData
            {
                SpoolId = "TEST-002",
                StageDates = new Dictionary<SpoolStage, DateTime?>
                {
                    [SpoolStage.BV]       = new DateTime(2025, 5, 1),
                    [SpoolStage.WeldDone] = new DateTime(2025, 5, 20), // FitUp skipped
                }
            };

            Assert.AreEqual(SpoolStage.WeldDone, spool.GetStageAtDate(new DateTime(2025, 6, 1)));
        }

        [TestMethod]
        public void SpoolData_GetStageAtDate_SameDayIsCompleted()
        {
            var spool = new SpoolData
            {
                SpoolId = "TEST-003",
                StageDates = new Dictionary<SpoolStage, DateTime?>
                {
                    [SpoolStage.BV] = new DateTime(2025, 5, 1),
                }
            };

            // Same day should count as completed
            Assert.AreEqual(SpoolStage.BV, spool.GetStageAtDate(new DateTime(2025, 5, 1)));
        }

        [TestMethod]
        public void SpoolStageInfo_OrderedStages_Has14Items()
        {
            Assert.AreEqual(14, SpoolStageInfo.OrderedStages.Length);
        }

        [TestMethod]
        public void SpoolStageInfo_ColumnMap_MapsAllStageHeaders()
        {
            var map = SpoolStageInfo.ColumnMap;
            Assert.AreEqual(14, map.Count);
            Assert.AreEqual(SpoolStage.BV, map["B/V"]);
            Assert.AreEqual(SpoolStage.HandOver, map["H/O일자"]);
            Assert.AreEqual(SpoolStage.Welding, map["Welding"]);
        }
    }
}
