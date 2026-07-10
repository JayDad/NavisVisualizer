using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Tests
{
    /// <summary>
    /// NwdScope 파일명 키워드 매칭 검증 — 확정 규약의 실제 파일명 목록으로
    /// 각 공종 스코프의 매칭/비매칭을 고정한다 (규약·키워드 변경 시 여기부터 깨져야 함).
    /// </summary>
    [TestClass]
    public class NwdScopeTests
    {
        // 확정 규약 파일명 (federated 컨테이너 + 하위 공종 파일)
        private const string Container = "00-02_Trion_Topsides_Subsystem.nwd";
        private const string Str = "01-02_Trion_TopsidesLQ_Str.nwc";
        private const string HydroPkg = "02-02_Trion_Topsides_HYDROPKG.nwd";
        private const string Meq = "04-02_Trion_Topsides_MEQ.nwd";
        private const string Eit = "05-02_Trion_Topsides_EIT.nwd";
        private const string Cable = "07_Trion_All_Cable.nwd";
        private const string PipSupport = "09-02_Trion_Topsides_PIPSupport.nwd";
        private const string Spl = "03-02_Trion_Topsides_SPL.nwd"; // 규약상 존재 가능 (스풀 별도 추출 시)

        [TestMethod]
        public void Spool_Matches_Spl_Only_And_Chains_To_Hydrotest()
        {
            // 1순위: SPL 파일만 — HYDROPKG는 직접 매칭 안 됨 (SPL 부재 시에만 체인으로 넘어감)
            Assert.IsTrue(NwdScope.Spool.MatchesFileName(Spl));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(HydroPkg));

            Assert.IsFalse(NwdScope.Spool.MatchesFileName(Container));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(Str));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(Meq));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(Eit));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(Cable));
            // "PIPSupport"에 SPL이 없음을 고정 — 배관 서포트는 스풀 스코프 밖
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(PipSupport));

            // SPL 없으면 HYDROPKG에서 스풀을 찾는 규약 = 체인 fallback으로 고정
            Assert.AreSame(NwdScope.Hydrotest, NwdScope.Spool.Fallback);
            Assert.IsNull(NwdScope.Hydrotest.Fallback);
        }

        [TestMethod]
        public void Hydrotest_Matches_HydroPkg_Only()
        {
            Assert.IsTrue(NwdScope.Hydrotest.MatchesFileName(HydroPkg));

            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Spl));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Container));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Str));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Meq));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Eit));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Cable));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(PipSupport));
        }

        [TestMethod]
        public void Equipment_Matches_Meq_Only()
        {
            Assert.IsTrue(NwdScope.Equipment.MatchesFileName(Meq));

            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(Container));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(Str));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(HydroPkg));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(Eit));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(Cable));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(PipSupport));
        }

        [TestMethod]
        public void EitTray_Matches_Eit_Only()
        {
            Assert.IsTrue(NwdScope.EitTray.MatchesFileName(Eit));

            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(Container));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(Str));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(HydroPkg));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(Meq));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(Cable));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(PipSupport));
        }

        [TestMethod]
        public void Cable_Matches_Cable_Only()
        {
            Assert.IsTrue(NwdScope.Cable.MatchesFileName(Cable));

            Assert.IsFalse(NwdScope.Cable.MatchesFileName(Container));
            Assert.IsFalse(NwdScope.Cable.MatchesFileName(Meq));
            Assert.IsFalse(NwdScope.Cable.MatchesFileName(Eit));
            Assert.IsFalse(NwdScope.Cable.MatchesFileName(PipSupport));
        }

        [TestMethod]
        public void SubSystem_Disciplines_Match_Their_Own_Files()
        {
            // 구 union 스코프(MEQ·SPL·HYDROPKG) 폐기 — Sub-system은 공종별로 자기 nwd만
            // 레벨 타겟한다. 각 공종 스코프가 자기 파일만 매칭하는지 검증.
            Assert.IsTrue(NwdScope.Equipment.MatchesFileName(Meq));
            Assert.IsTrue(NwdScope.Hydrotest.MatchesFileName(HydroPkg));
            Assert.IsTrue(NwdScope.EitTray.MatchesFileName(Eit));
            Assert.IsTrue(NwdScope.Cable.MatchesFileName(Cable));

            // 교차 오탐 없음 — Equipment 스코프가 Piping/EIT/Cable 파일을 잡으면 안 됨
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(HydroPkg));
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(Eit));
            Assert.IsFalse(NwdScope.Hydrotest.MatchesFileName(Meq));
            Assert.IsFalse(NwdScope.EitTray.MatchesFileName(Meq));
        }

        [TestMethod]
        public void Match_Is_CaseInsensitive()
        {
            Assert.IsTrue(NwdScope.Equipment.MatchesFileName("04-02_trion_topsides_meq.nwd"));
            Assert.IsTrue(NwdScope.Hydrotest.MatchesFileName("02-02_TRION_TOPSIDES_hydropkg.NWD"));
        }

        [TestMethod]
        public void DirectoryName_DoesNot_FalsePositive()
        {
            // 경로의 디렉터리에 키워드가 있어도 파일명만으로 판정
            Assert.IsFalse(NwdScope.Equipment.MatchesFileName(@"D:\MEQ\07_Trion_All_Cable.nwd"));
            Assert.IsTrue(NwdScope.Equipment.MatchesFileName(@"D:\Models\04-02_Trion_Topsides_MEQ.nwd"));
            Assert.IsTrue(NwdScope.Cable.MatchesFileName("/srv/models/07_Trion_All_Cable.nwd"));
        }

        [TestMethod]
        public void Match_Handles_Null_And_Empty()
        {
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(null));
            Assert.IsFalse(NwdScope.Spool.MatchesFileName(""));
        }

        [TestMethod]
        public void LooksLikeFileNode_Detects_File_Extensions()
        {
            Assert.IsTrue(NwdScope.LooksLikeFileNode(Meq));
            Assert.IsTrue(NwdScope.LooksLikeFileNode("MEBTray1.nwc"));  // CLAUDE.md 2번 사례
            Assert.IsTrue(NwdScope.LooksLikeFileNode("EQUIP.RVM"));
            Assert.IsTrue(NwdScope.LooksLikeFileNode("plant.NWD"));

            // 일반 태그/범주 노드는 파일 노드가 아님
            Assert.IsFalse(NwdScope.LooksLikeFileNode("PCVTRAY-STW-001"));
            Assert.IsFalse(NwdScope.LooksLikeFileNode("/SM/MEB/ELEC"));
            Assert.IsFalse(NwdScope.LooksLikeFileNode(null));
            Assert.IsFalse(NwdScope.LooksLikeFileNode(""));
        }

        [TestMethod]
        public void StripDirectory_Handles_Both_Separators()
        {
            Assert.AreEqual("a.nwd", NwdScope.StripDirectory(@"C:\x\y\a.nwd"));
            Assert.AreEqual("a.nwd", NwdScope.StripDirectory("/x/y/a.nwd"));
            Assert.AreEqual("a.nwd", NwdScope.StripDirectory("a.nwd"));
        }
    }
}
