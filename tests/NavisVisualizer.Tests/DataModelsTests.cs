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
        public void ColorSetting_HydrotestDefaults_HasAllStages()
        {
            var defaults = ColorSetting.HydrotestDefaults;
            Assert.AreEqual(7, defaults.Count);
            foreach (HydrotestStage stage in Enum.GetValues(typeof(HydrotestStage)))
                Assert.IsTrue(defaults.ContainsKey(stage), $"Missing color for {stage}");
        }

        [TestMethod]
        public void ColorSetting_SpoolDefaults_HasAllStages()
        {
            var defaults = ColorSetting.SpoolDefaults;
            Assert.AreEqual(16, defaults.Count); // NotStarted + 15 stages (설치 FitUpInstall 포함)
            foreach (SpoolStage stage in Enum.GetValues(typeof(SpoolStage)))
                Assert.IsTrue(defaults.ContainsKey(stage), $"Missing color for {stage}");
        }

        [TestMethod]
        public void ColorSetting_Clone_IsDeepCopy()
        {
            var original = new ColorSetting { DisplayColor = Color.Red, Transparency = 0.5 };
            var clone = original.Clone();
            clone.Transparency = 0.9;
            Assert.AreEqual(0.5, original.Transparency);
        }

        [TestMethod]
        public void TestPackageData_GetStageAtDate_ReturnsNotStarted_WhenNoDates()
        {
            var pkg = new TestPackageData { TestPkgId = "TEST-001" };
            Assert.AreEqual(HydrotestStage.NotStarted, pkg.GetStageAtDate(DateTime.Today));
        }

        [TestMethod]
        public void TestPackageData_GetStageAtDate_ReturnsLatestCompletedStage()
        {
            var pkg = new TestPackageData
            {
                TestPkgId = "TEST-001",
                StageDates = new Dictionary<HydrotestStage, DateTime?>
                {
                    [HydrotestStage.Review]         = new DateTime(2026, 1, 14),
                    [HydrotestStage.LineInspection] = new DateTime(2026, 2, 2),
                    [HydrotestStage.Flushing]       = new DateTime(2026, 3, 21),
                    [HydrotestStage.Hydrotest]      = new DateTime(2026, 3, 24),
                }
            };

            Assert.AreEqual(HydrotestStage.NotStarted, pkg.GetStageAtDate(new DateTime(2026, 1, 1)));
            Assert.AreEqual(HydrotestStage.Review, pkg.GetStageAtDate(new DateTime(2026, 1, 20)));
            Assert.AreEqual(HydrotestStage.Flushing, pkg.GetStageAtDate(new DateTime(2026, 3, 22)));
            Assert.AreEqual(HydrotestStage.Hydrotest, pkg.GetStageAtDate(new DateTime(2026, 4, 1)));
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
                }
            };

            Assert.AreEqual(SpoolStage.NotStarted, spool.GetStageAtDate(new DateTime(2025, 4, 30)));
            Assert.AreEqual(SpoolStage.BV, spool.GetStageAtDate(new DateTime(2025, 5, 5)));
            Assert.AreEqual(SpoolStage.WeldDone, spool.GetStageAtDate(new DateTime(2025, 6, 1)));
        }

        [TestMethod]
        public void HydrotestStageInfo_ColumnMap_MapsAllHeaders()
        {
            Assert.AreEqual(HydrotestStage.Review, HydrotestStageInfo.ColumnMap["Review"]);
            Assert.AreEqual(HydrotestStage.LineInspection, HydrotestStageInfo.ColumnMap["Line inspection"]);
            Assert.AreEqual(HydrotestStage.Reinstatement, HydrotestStageInfo.ColumnMap["Reinstatement"]);
        }

        [TestMethod]
        public void SubSystemElement_EitDisciplines_NormalizeStatus()
        {
            var refDate = new DateTime(2026, 7, 1);

            // EIT EQ — INSTALL DTE 단일 단계
            var eqDone = SubSystemElement.FromEitEquipment("101180-AT-10051", "PT", "0104-00", new DateTime(2026, 6, 1));
            var eqNot  = SubSystemElement.FromEitEquipment("/101180-AT-10052", "PT", "0104-00", null);
            Assert.AreEqual(ProgressStatus.Completed, eqDone.StatusAt(refDate));
            Assert.AreEqual(ProgressStatus.NotStarted, eqNot.StatusAt(refDate));
            Assert.AreEqual("101180-AT-10052", eqNot.ElementId); // 선행 '/' 방어 정규화

            // EIT Tray — % 기반 현재상태 (기준일 무시)
            var tray = SubSystemElement.FromTray(
                new EitTrayData { TrayNumber = "/101890-HVT-61003/B1.", InstallProgress = 0.5 }, "0104-00");
            Assert.AreEqual(ProgressStatus.InProgress, tray.StatusAt(refDate));
            Assert.AreEqual("101890-HVT-61003/B1", tray.ElementId); // 선행 '/'·후행 '.' 정규화

            // Cable — 날짜 기반: 결선완료(Terminated)만 완료
            var cable = new CableLineData { CableNo = "101440-CLV-92440-001", SubSystem = "0104-00" };
            cable.StageDates[CableLineStage.Pulling] = new DateTime(2026, 6, 10);
            var cableEl = SubSystemElement.FromCable(cable);
            Assert.AreEqual(ProgressStatus.InProgress, cableEl.StatusAt(refDate));
            Assert.AreEqual(ProgressStatus.NotStarted, cableEl.StatusAt(new DateTime(2026, 6, 1)));
        }

        [TestMethod]
        public void EitTrayData_NormalizeId_StripsLeadingSlashAndTrailingDot()
        {
            // 모델 DisplayName 인덱스 키와 동일 규약: 선행 '/' 제거.
            Assert.AreEqual("101890-INT-25018-CM-PDA-CV/B1",
                EitTrayData.NormalizeId("/101890-INT-25018-CM-PDA-CV/B1"));
            // OASIS EIT_Tray의 BRANCH NO. 끝 '.' 장식(실측 2026-07) — 제거해야 매칭됨.
            Assert.AreEqual("101890-INT-25018-CM-PDA-CV/B1",
                EitTrayData.NormalizeId("101890-INT-25018-CM-PDA-CV/B1."));
            Assert.AreEqual("101890-INT-25018", EitTrayData.NormalizeId(" /101890-INT-25018. "));
            Assert.AreEqual("", EitTrayData.NormalizeId(null));
            Assert.AreEqual("", EitTrayData.NormalizeId("  "));
        }
    }
}
