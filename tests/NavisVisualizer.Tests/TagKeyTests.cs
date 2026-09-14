using Microsoft.VisualStudio.TestTools.UnitTesting;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Tests
{
    /// <summary>
    /// 태그 키 정규화 — Ruya Hydrotest 실측(2026-09) 장식("/GPSET/…" PDMS 경로 접두, "(…)" 괄호)과
    /// Trion 장식("/TAG", "TAG/suffix")이 같은 키로 수렴하는지 고정한다.
    /// </summary>
    [TestClass]
    public class TagKeyTests
    {
        [TestMethod]
        public void Normalize_Strips_Slash_Parens_And_Uppercases()
        {
            Assert.AreEqual("U001-DO-102-K", TagKey.Normalize("u001-do-102-k"));
            Assert.AreEqual("U001-DO-102-K", TagKey.Normalize("  /U001-DO-102-K "));
            Assert.AreEqual("C01B-DO-030254-1", TagKey.Normalize("(C01B-DO-030254-1)"));
            Assert.AreEqual("C01B-DO-030254-1", TagKey.Normalize("((C01B-DO-030254-1))"));
            Assert.AreEqual("GPSET/U001-DO-102-K", TagKey.Normalize("/GPSET/U001-DO-102-K"));
            Assert.AreEqual("", TagKey.Normalize(null));
            Assert.AreEqual("", TagKey.Normalize("  "));
            // 안쪽 괄호는 유지 (태그 자체의 일부일 수 있음)
            Assert.AreEqual("A(1)-B", TagKey.Normalize("a(1)-b"));
        }

        [TestMethod]
        public void Candidates_Ruya_PdmsPath_Yields_LastSegment()
        {
            var c = TagKey.Candidates("/GPSET/U001-DO-102-K");
            CollectionAssert.Contains(c, "GPSET/U001-DO-102-K");   // 전체
            CollectionAssert.Contains(c, "GPSET");                 // 접두 (Trion "TAG/suffix" 규약 유지)
            CollectionAssert.Contains(c, "U001-DO-102-K");         // 마지막 세그먼트 ← Ruya 매칭 키
            Assert.AreEqual(3, c.Count);
        }

        [TestMethod]
        public void Candidates_Parenthesized_Spool()
        {
            var c = TagKey.Candidates("(C01B-DO-030254-1)");
            CollectionAssert.AreEqual(new[] { "C01B-DO-030254-1" }, c);
        }

        [TestMethod]
        public void Candidates_Trion_Prefix_Rule_Unchanged()
        {
            // "TAG/suffix" — 태그가 앞. 전체 + 접두 + 마지막 세그먼트(suffix)
            var c = TagKey.Candidates("/101-PV-001/A");
            CollectionAssert.Contains(c, "101-PV-001/A");
            CollectionAssert.Contains(c, "101-PV-001");
            CollectionAssert.Contains(c, "A");

            // 슬래시 없음 → 전체 하나
            CollectionAssert.AreEqual(new[] { "SP-0001" }, TagKey.Candidates("/SP-0001"));
        }

        [TestMethod]
        public void Candidates_Dedup_And_Empty()
        {
            Assert.AreEqual(0, TagKey.Candidates(null).Count);
            Assert.AreEqual(0, TagKey.Candidates("/").Count);
            // 마지막 세그먼트가 전체와 같으면 중복 등록 안 함
            CollectionAssert.AreEqual(new[] { "X" }, TagKey.Candidates("x"));
        }

        [TestMethod]
        public void Excel_Id_With_Or_Without_Prefix_Reaches_Same_Key()
        {
            // 인덱스: 모델 노드 후보 키 / 조회: 실적 ID 정규화 — 둘이 교차해 만나는지
            var modelKeys = TagKey.Candidates("/GPSET/0301-DO-103-K");
            CollectionAssert.Contains(modelKeys, TagKey.Normalize("0301-DO-103-K"));       // 접두 없이
            CollectionAssert.Contains(modelKeys, TagKey.Normalize("/GPSET/0301-DO-103-K")); // 접두 붙여서
            CollectionAssert.Contains(modelKeys, TagKey.Normalize("GPSET/0301-DO-103-K"));
        }
    }
}
