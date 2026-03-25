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
        public void ColorSetting_DefaultsAreCorrect_ForSpoolFabricating()
        {
            var setting = ColorSetting.SpoolDefaults[SpoolStage.Fabricating];
            Assert.AreEqual(0.2, setting.Transparency, 0.001);
            Assert.AreEqual(255, setting.DisplayColor.R);
            Assert.AreEqual(215, setting.DisplayColor.G);
            Assert.AreEqual(0, setting.DisplayColor.B);
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
    }
}
