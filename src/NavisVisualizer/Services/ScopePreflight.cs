using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// NWD 스코프 사전 점검 (Overview 탭용) — 열린 문서에서 각 공종 스코프의 대상 파일이
    /// 발견되는지 인덱스 빌드 없이 읽기 전용으로 판정한다. ModelItemSearcher의
    /// ResolveScopeRoots와 동일한 2단계 매칭(① Model.FileName/RootItem DisplayName,
    /// ② federated 트리의 파일 노드만 depth≤3 얕은 하강)을 미러링하되, searcher의
    /// LastScopeNote 등 상태를 건드리지 않는다 — 점검이 진단값을 덮어쓰면 안 되므로 별도 구현.
    /// 파일 노드만 따라가므로 geometry 트리 walk가 없어 대형 모델에서도 즉시 수준.
    /// </summary>
    public static class ScopePreflight
    {
        public class Result
        {
            /// <summary>점검한 스코프 체인의 대표 라벨 (예: "SPL→HYDROPKG").</summary>
            public string ChainLabel;
            /// <summary>실제로 매칭된 체인 단계 라벨 (예: SPL 부재 시 "HYDROPKG"). 미발견이면 null.</summary>
            public string MatchedTier;
            /// <summary>매칭된 파일명들 (디렉터리 제거).</summary>
            public List<string> Files = new List<string>();
            public bool Found => Files.Count > 0;
        }

        /// <summary>스코프 체인을 앞에서부터 시도해 처음 매칭되는 단계의 파일들을 반환.</summary>
        public static Result Probe(Document doc, NwdScope scope)
        {
            var result = new Result { ChainLabel = ChainLabel(scope) };
            if (doc == null || doc.Models.Count == 0) return result;

            for (var tier = scope; tier != null; tier = tier.Fallback)
            {
                CollectTier(doc, tier, result.Files);
                if (result.Files.Count > 0)
                {
                    result.MatchedTier = tier.Label;
                    return result;
                }
            }
            return result;
        }

        private static string ChainLabel(NwdScope scope)
        {
            var parts = new List<string>();
            for (var tier = scope; tier != null; tier = tier.Fallback)
                parts.Add(tier.Label);
            return string.Join("→", parts);
        }

        private static void CollectTier(Document doc, NwdScope tier, List<string> files)
        {
            foreach (Model model in doc.Models)
            {
                string fileName = null;
                try { fileName = model.FileName; } catch { /* 일부 모델은 FileName 조회 실패 가능 */ }
                string rootName = model.RootItem?.DisplayName;

                if (tier.MatchesFileName(fileName) || tier.MatchesFileName(rootName))
                {
                    files.Add(NwdScope.StripDirectory(fileName ?? rootName ?? "?"));
                    continue;
                }
                CollectFileNodes(model.RootItem, tier, 0, files);
            }
        }

        private static void CollectFileNodes(ModelItem item, NwdScope tier, int depth, List<string> files)
        {
            if (item == null || depth > 3) return;
            foreach (ModelItem child in item.Children)
            {
                string dn = child.DisplayName?.Trim();
                if (!NwdScope.LooksLikeFileNode(dn)) continue;
                if (tier.MatchesFileName(dn))
                    files.Add(dn);           // 매칭 파일의 하위는 더 볼 필요 없음
                else
                    CollectFileNodes(child, tier, depth + 1, files);
            }
        }
    }
}
