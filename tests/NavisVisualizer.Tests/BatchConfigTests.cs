using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Services;

namespace NavisVisualizer.Tests
{
    /// <summary>
    /// batch.config 파싱/직렬화/출력 경로 규칙 (CLAUDE.md §19). Autodesk 비의존.
    /// 사용자가 손으로 고치는 파일이라 느슨한 표기(대소문자·별칭·공백)를 받아주는지와,
    /// "마지막 선택 기억"이 라운드트립으로 보존되는지를 고정한다.
    /// </summary>
    [TestClass]
    public class BatchConfigTests
    {
        private static readonly string[] Sample =
        {
            "# 주석",
            "[settings]",
            @"outputFolder=D:\발행",
            "fileNamePattern={name}_{date}_{time}",
            "",
            "[job:Trion]",
            @"model=D:\Models\00-02_Trion_Topsides_Subsystem.nwd",
            "project=Q557",
            "enabled=true",
            "disciplines=Spool, hydro, EIT",
            "",
            "[job:Project B]",
            @"model=E:\B\B_Subsystem.nwd",
            "enabled=0",
            "disciplines=Equipment,Cable,Unknown",
        };

        [TestMethod]
        public void Parse_ReadsSettingsAndJobs()
        {
            var cfg = BatchConfig.Parse(Sample);

            Assert.AreEqual(@"D:\발행", cfg.OutputFolder);
            Assert.AreEqual("{name}_{date}_{time}", cfg.FileNamePattern);
            Assert.AreEqual(2, cfg.Jobs.Count);

            var a = cfg.Jobs[0];
            Assert.AreEqual("Trion", a.Name);
            Assert.AreEqual(@"D:\Models\00-02_Trion_Topsides_Subsystem.nwd", a.ModelPath);
            Assert.AreEqual("Q557", a.ProjectNo);
            Assert.IsTrue(a.Enabled);

            var b = cfg.Jobs[1];
            Assert.AreEqual("Project B", b.Name);
            Assert.AreEqual("", b.ProjectNo);
            Assert.IsFalse(b.Enabled);
        }

        [TestMethod]
        public void Parse_DisciplineAliases_CaseInsensitive_UnknownIgnored()
        {
            var cfg = BatchConfig.Parse(Sample);

            CollectionAssert.AreEquivalent(
                new[] { BatchDiscipline.Spool, BatchDiscipline.Hydrotest, BatchDiscipline.EitTray },
                cfg.Jobs[0].Disciplines.ToArray());
            CollectionAssert.AreEquivalent(
                new[] { BatchDiscipline.Equipment, BatchDiscipline.Cable },
                cfg.Jobs[1].Disciplines.ToArray());
        }

        [TestMethod]
        public void DisciplineParse_Aliases()
        {
            Assert.AreEqual(BatchDiscipline.Spool, BatchDisciplineInfo.Parse("SPL"));
            Assert.AreEqual(BatchDiscipline.Hydrotest, BatchDisciplineInfo.Parse("HydroPKG"));
            Assert.AreEqual(BatchDiscipline.Equipment, BatchDisciplineInfo.Parse("meq"));
            Assert.AreEqual(BatchDiscipline.EitTray, BatchDisciplineInfo.Parse("EIT Tray"));
            Assert.AreEqual(BatchDiscipline.EitTray, BatchDisciplineInfo.Parse("eit_tray"));
            Assert.AreEqual(BatchDiscipline.Cable, BatchDisciplineInfo.Parse(" cable "));
            Assert.IsNull(BatchDisciplineInfo.Parse("SubSystem"));
            Assert.IsNull(BatchDisciplineInfo.Parse(""));
        }

        [TestMethod]
        public void Serialize_RoundTrip_PreservesSelection()
        {
            var cfg = BatchConfig.Parse(Sample);
            // "마지막 선택 기억": 실행 창이 바꾼 상태가 저장→재로드 후 그대로여야 한다
            cfg.Jobs[1].Enabled = true;
            cfg.Jobs[1].Disciplines = new HashSet<BatchDiscipline> { BatchDiscipline.Cable };

            var again = BatchConfig.Parse(cfg.Serialize().Split('\n'));

            Assert.AreEqual(cfg.OutputFolder, again.OutputFolder);
            Assert.AreEqual(cfg.FileNamePattern, again.FileNamePattern);
            Assert.AreEqual(2, again.Jobs.Count);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(cfg.Jobs[i].Name, again.Jobs[i].Name);
                Assert.AreEqual(cfg.Jobs[i].ModelPath, again.Jobs[i].ModelPath);
                Assert.AreEqual(cfg.Jobs[i].ProjectNo, again.Jobs[i].ProjectNo);
                Assert.AreEqual(cfg.Jobs[i].Enabled, again.Jobs[i].Enabled);
                CollectionAssert.AreEquivalent(cfg.Jobs[i].Disciplines.ToArray(), again.Jobs[i].Disciplines.ToArray());
            }
        }

        [TestMethod]
        public void BuildOutputPath_ReplacesTokens_AndAppendsExtension()
        {
            var cfg = BatchConfig.Parse(Sample);
            var at = new DateTime(2026, 9, 14, 7, 5, 0);

            string path = cfg.BuildOutputPath(cfg.Jobs[0], at);

            Assert.AreEqual(Path.Combine(@"D:\발행", "Trion_20260914_0705.nwd"), path);
        }

        [TestMethod]
        public void BuildOutputPath_DefaultPattern_UsesModelName_NextToModelWhenFolderEmpty()
        {
            var cfg = BatchConfig.Parse(new[]
            {
                "[job:X]",
                @"model=C:\M\00-02_Trion_Topsides_Subsystem.nwd",
            });
            Assert.AreEqual(BatchConfig.DefaultFileNamePattern, cfg.FileNamePattern);
            Assert.AreEqual("", cfg.OutputFolder);

            string path = cfg.BuildOutputPath(cfg.Jobs[0], new DateTime(2026, 9, 14));

            Assert.AreEqual(Path.Combine(@"C:\M", "00-02_Trion_Topsides_Subsystem_20260914.nwd"), path);
        }

        [TestMethod]
        public void BuildOutputPath_SanitizesInvalidFileNameChars()
        {
            var cfg = BatchConfig.Parse(new[]
            {
                "[settings]",
                "fileNamePattern={name}_{date}",
                "[job:A/B:C]",
                @"model=C:\M\x.nwd",
            });

            string name = Path.GetFileName(cfg.BuildOutputPath(cfg.Jobs[0], new DateTime(2026, 1, 2)));

            Assert.IsFalse(name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0, name);
            Assert.IsTrue(name.EndsWith(".nwd"));
        }

        [TestMethod]
        public void DefaultTemplate_ParsesToTwoPlaceholderJobs()
        {
            var cfg = BatchConfig.Parse(BatchConfig.DefaultTemplate.Split('\n'));

            Assert.AreEqual(2, cfg.Jobs.Count);
            Assert.IsTrue(cfg.Jobs[0].Enabled);
            Assert.IsFalse(cfg.Jobs[1].Enabled);
            Assert.IsTrue(cfg.Jobs.All(j => j.ModelPath.Contains("CHANGE_ME")),
                "placeholder 경로는 눈에 띄게 CHANGE_ME를 포함해야 한다");
        }

        [TestMethod]
        public void Parse_UnknownSection_KeysIgnored()
        {
            var cfg = BatchConfig.Parse(new[]
            {
                "[settings]",
                @"outputFolder=D:\a",
                "[future]",
                @"outputFolder=D:\b",      // 모르는 섹션의 키는 settings를 덮어쓰면 안 됨
                "[job:J]",
                @"model=C:\x.nwd",
            });

            Assert.AreEqual(@"D:\a", cfg.OutputFolder);
            Assert.AreEqual(1, cfg.Jobs.Count);
        }
    }
}
