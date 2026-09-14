using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Tests
{
    /// <summary>
    /// 프로젝트 프로파일(별칭)·문서별 매핑 설정 검증 (2026-09 — Ruya 대응, 전체 fallback 폐지).
    /// Ruya 실제 파일명 목록으로 내장 Ruya 프로파일의 매칭/비매칭을 고정하고, 별칭 덮어쓰기 파일과
    /// 문서별 설정 파일의 파싱·직렬화 왕복을 검증한다. Autodesk 비의존.
    /// </summary>
    [TestClass]
    public class ScopeMappingTests
    {
        // Ruya federated 트리 (RUYA-progress_260911.nwd) 실제 파일명
        private static readonly string[] RuyaFiles =
        {
            "RUYA-progress_260911.nwd",
            "BJ-RUY-ELE.nwd", "BJ-RUY-HSE.nwd", "BJ-RUY-INS.nwd", "BJ-RUY-TEL.nwd",
            "BJ-RUY-SPOOL.nwd", "BJ-RUY-SUP.nwd", "BJ-RUY-HVA.nwd", "BJ-RUY-ARC.nwd", "BJ-RUY-MIF.nwd",
            "BJ-RUY-MEC.nwd",
            "BJ-RUY-STR.nwd", "BJ-RUY-PVV.nwd",
            "BJ-RUY-COOEC.nwd", "BJ-RUY-CONST-V01.nwd", "BJ-Input_To_Others-RUY.rvm",
        };

        [TestCleanup]
        public void Cleanup() => NwdScope.AliasProvider = null;

        private static void UseProfile(ScopeProfile p) => NwdScope.AliasProvider = key => p.AliasesFor(key);

        private static List<string> Matches(NwdScope scope) =>
            RuyaFiles.Where(scope.MatchesFileName).ToList();

        [TestMethod]
        public void Ruya_Profile_Matches_Only_Obvious_Files()
        {
            UseProfile(ScopeProfiles.Ruya());

            CollectionAssert.AreEqual(new[] { "BJ-RUY-SPOOL.nwd" }, Matches(NwdScope.Spool));
            CollectionAssert.AreEqual(new[] { "BJ-RUY-MEC.nwd" }, Matches(NwdScope.Equipment));
            CollectionAssert.AreEqual(new[] { "BJ-RUY-STR.nwd" }, Matches(NwdScope.Structure));

            // 확정 전 공종은 별칭 없음 = 어떤 파일도 자동 인식하지 않음 (현장 지정)
            Assert.AreEqual(0, Matches(NwdScope.Hydrotest).Count);
            Assert.AreEqual(0, Matches(NwdScope.EitTray).Count);
            Assert.AreEqual(0, Matches(NwdScope.Eit).Count);
            Assert.AreEqual(0, Matches(NwdScope.Cable).Count);
            Assert.AreEqual(0, NwdScope.Hydrotest.Keywords.Count);
            Assert.AreEqual("(없음)", NwdScope.Hydrotest.ChainAliasLabel());
        }

        [TestMethod]
        public void Ruya_Profile_Does_Not_Use_Trion_Keywords()
        {
            UseProfile(ScopeProfiles.Ruya());
            // Trion 파일명은 Ruya 프로파일에선 안 잡힘 (SPL/MEQ/HYDROPKG는 Ruya 별칭이 아님)
            Assert.IsFalse(NwdScope.Spool.MatchesFileName("03-02_Trion_Topsides_SPL.nwd"));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName("04-02_Trion_Topsides_MEQ.nwd"));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName("02-02_Trion_Topsides_HYDROPKG.nwd"));
            // Structure는 양쪽 다 STR
            Assert.IsTrue(NwdScope.Structure.MatchesFileName("01-02_Trion_TopsidesLQ_Str.nwc"));
        }

        [TestMethod]
        public void Trion_Profile_Keeps_Default_Keywords()
        {
            UseProfile(ScopeProfiles.Trion());
            Assert.IsTrue(NwdScope.Spool.MatchesFileName("03-02_Trion_Topsides_SPL.nwd"));
            Assert.IsTrue(NwdScope.Equipment.MatchesFileName("04-02_Trion_Topsides_MEQ.nwd"));
            Assert.IsTrue(NwdScope.EitTray.MatchesFileName("05-02-01_Trion_Topsides_EIT_Tray.nwd"));
            // Trion 프로파일로는 Ruya 파일이 안 잡힘 → Ruya 문서에서 프로파일 감지가 필수임을 고정
            Assert.IsFalse(NwdScope.Spool.MatchesFileName("BJ-RUY-SPOOL.nwd"));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName("BJ-RUY-MEC.nwd"));
        }

        [TestMethod]
        public void Provider_Null_Falls_Back_To_Defaults()
        {
            NwdScope.AliasProvider = null;
            CollectionAssert.AreEqual(new[] { "SPL" }, NwdScope.Spool.Keywords.ToList());
            // 모르는 Key에 null을 주는 프로파일도 기본 키워드로
            UseProfile(new ScopeProfile("Empty"));
            CollectionAssert.AreEqual(new[] { "MEQ" }, NwdScope.Equipment.Keywords.ToList());
        }

        [TestMethod]
        public void Detect_Profile_By_Document_Name()
        {
            var profiles = ScopeProfiles.BuiltIn();
            Assert.AreEqual("Ruya", ScopeProfiles.Detect(@"D:\Models\RUYA-progress_260911.nwd", profiles).Name);
            Assert.AreEqual("Ruya", ScopeProfiles.Detect("BJ-RUY-MEC.nwd", profiles).Name);   // 개별 공종 파일만 연 경우
            Assert.AreEqual("Trion", ScopeProfiles.Detect("00-02_Trion_Topsides_Subsystem.nwd", profiles).Name);
            Assert.AreEqual("Trion", ScopeProfiles.Detect("unknown_project.nwd", profiles).Name);   // 기본
            Assert.AreEqual("Trion", ScopeProfiles.Detect(null, profiles).Name);
        }

        [TestMethod]
        public void Overrides_Replace_Aliases_And_Add_Profiles()
        {
            var errors = new List<string>();
            var profiles = ScopeProfiles.ApplyOverrides(ScopeProfiles.BuiltIn(), new[]
            {
                "# 주석",
                "Ruya.Hydrotest=SPOOL",             // 빈 별칭 → 채움
                "Ruya.Eit=ELE|INS, TEL",            // 구분자 혼용
                "Ruya.Spool=",                      // 별칭 제거 (자동 인식 없음)
                "Kaombo.Equipment=EQP",             // 새 프로파일
                "Kaombo.pattern=KMB",
                "Ruya.NoSuchScope=X",               // 오류
                "garbage line",                     // 오류
            }, errors);

            var ruya = ScopeProfiles.FindByName("ruya", profiles);
            CollectionAssert.AreEqual(new[] { "SPOOL" }, ruya.AliasesFor(NwdScope.Hydrotest.Key).ToList());
            CollectionAssert.AreEqual(new[] { "ELE", "INS", "TEL" }, ruya.AliasesFor(NwdScope.Eit.Key).ToList());
            Assert.AreEqual(0, ruya.AliasesFor(NwdScope.Spool.Key).Count);
            CollectionAssert.AreEqual(new[] { "MEC" }, ruya.AliasesFor(NwdScope.Equipment.Key).ToList()); // 미언급은 유지

            var kaombo = ScopeProfiles.FindByName("Kaombo", profiles);
            Assert.IsNotNull(kaombo);
            CollectionAssert.AreEqual(new[] { "EQP" }, kaombo.AliasesFor(NwdScope.Equipment.Key).ToList());
            Assert.AreEqual("Kaombo", ScopeProfiles.Detect("KMB-topside.nwd", profiles).Name);

            Assert.AreEqual(2, errors.Count);
        }

        [TestMethod]
        public void Override_Lines_RoundTrip()
        {
            var original = ScopeProfiles.ApplyOverrides(ScopeProfiles.BuiltIn(),
                new[] { "Ruya.Eit=ELE|INS|TEL", "Kaombo.Spool=SPL", "Kaombo.pattern=KMB" });
            var lines = ScopeProfiles.ToOverrideLines(original);
            var reparsed = ScopeProfiles.ApplyOverrides(ScopeProfiles.BuiltIn(), lines);

            foreach (var p in original)
            {
                var q = ScopeProfiles.FindByName(p.Name, reparsed);
                Assert.IsNotNull(q, p.Name);
                CollectionAssert.AreEqual(p.DocPatterns, q.DocPatterns, p.Name + " pattern");
                foreach (var scope in NwdScope.All)
                {
                    var a = p.AliasesFor(scope.Key);
                    var b = q.AliasesFor(scope.Key);
                    Assert.AreEqual(a == null, b == null, $"{p.Name}.{scope.Key} null-ness");
                    if (a != null) CollectionAssert.AreEqual(a.ToList(), b.ToList(), $"{p.Name}.{scope.Key}");
                }
            }
        }

        [TestMethod]
        public void MappingConfig_RoundTrip()
        {
            var cfg = new ScopeMappingConfig { ProfileName = "Ruya" };
            cfg.Set(NwdScope.Spool.Key, new ScopeSelection
            {
                Mode = ScopeSelectionMode.Files,
                Files = { "BJ-RUY-SPOOL.nwd", "BJ-RUY-HVA.nwd" },
            });
            cfg.Set(NwdScope.Hydrotest.Key, new ScopeSelection { Mode = ScopeSelectionMode.AllModels });
            cfg.Set(NwdScope.Equipment.Key, new ScopeSelection());   // auto = 기록 안 함

            var lines = cfg.ToLines();
            var back = ScopeMappingConfig.Parse(lines);

            Assert.AreEqual("Ruya", back.ProfileName);
            var spool = back.Get(NwdScope.Spool.Key);
            Assert.AreEqual(ScopeSelectionMode.Files, spool.Mode);
            CollectionAssert.AreEqual(new[] { "BJ-RUY-SPOOL.nwd", "BJ-RUY-HVA.nwd" }, spool.Files);
            Assert.AreEqual(ScopeSelectionMode.AllModels, back.Get(NwdScope.Hydrotest.Key).Mode);
            Assert.AreEqual(ScopeSelectionMode.Auto, back.Get(NwdScope.Equipment.Key).Mode);
            Assert.AreEqual(ScopeSelectionMode.Auto, back.Get("Cable").Mode);   // 미기록 = 자동
            Assert.IsFalse(back.IsEmpty);
        }

        [TestMethod]
        public void MappingConfig_Ignores_Unknown_And_Malformed()
        {
            var cfg = ScopeMappingConfig.Parse(new[]
            {
                "profile=",                  // 빈 값 = 자동 감지
                "Bogus=files:a.nwd",         // 모르는 스코프
                "Spool=weird",               // 모르는 형식
                "no-equals",
                "Cable=files:",              // 파일 없음 (빈 목록) — Files 모드지만 대상 0
            });
            Assert.IsNull(cfg.ProfileName);
            Assert.AreEqual(ScopeSelectionMode.Auto, cfg.Get("Spool").Mode);
            Assert.AreEqual(ScopeSelectionMode.Files, cfg.Get("Cable").Mode);
            Assert.AreEqual(0, cfg.Get("Cable").Files.Count);
        }

        [TestMethod]
        public void ConfigFileName_Is_Sanitized_Per_Document()
        {
            Assert.AreEqual("RUYA-progress_260911.nwd.scope.cfg",
                ScopeMappingConfig.ConfigFileNameFor(@"D:\Models\RUYA-progress_260911.nwd"));
            Assert.AreEqual("a_b_c.nwd.scope.cfg", ScopeMappingConfig.ConfigFileNameFor("a b:c.nwd"));
            Assert.AreEqual("untitled.scope.cfg", ScopeMappingConfig.ConfigFileNameFor(null));
        }

        [TestMethod]
        public void Scope_Keys_Are_Stable_And_Unique()
        {
            var keys = NwdScope.All.Select(s => s.Key).ToList();
            CollectionAssert.AllItemsAreUnique(keys);
            CollectionAssert.AreEquivalent(
                new[] { "Structure", "Spool", "Hydrotest", "Equipment", "EitTray", "Eit", "Cable" }, keys);
            Assert.AreSame(NwdScope.Spool, NwdScope.FindByKey("spool"));
            Assert.IsNull(NwdScope.FindByKey("nope"));
        }
    }
}
