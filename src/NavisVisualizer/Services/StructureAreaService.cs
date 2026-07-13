using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Services
{
    /// <summary>구조(Str) 파일의 레벨1 영역 노드 하나 — 이름 기준으로 복수 Str 파일의 동명 노드를 병합.</summary>
    public class StructureArea
    {
        public string Name;
        public List<ModelItem> Items = new List<ModelItem>();
    }

    /// <summary>
    /// Structure 탭용 영역 열거 — 열린 문서에서 Str 스코프(NwdScope.Structure) 파일 노드를
    /// ScopePreflight와 동일한 2단계 매칭(① Model.FileName/RootItem DisplayName,
    /// ② federated 트리의 파일 노드만 depth≤3 얕은 하강)으로 찾고, 그 레벨1 자식(영역
    /// 노드: /QR/LG/STRU/HHI …)만 반환한다. geometry 트리는 내려가지 않아 인덱스 빌드
    /// 없이 즉시 수준. 하드 스코프 성격: Str 파일 미발견 시 전체 모델을 훑지 않고
    /// 빈 목록 + 진단 노트만 남긴다 (파일명 규약 불일치를 드러내는 쪽이 안전).
    /// ModelItemSearcher를 안 쓰는 이유: 태그 매칭이 아니라 노드 열거라 인덱스가 불필요하고,
    /// searcher의 LastScopeNote 등 다른 탭 진단 상태를 건드리면 안 되기 때문 (ScopePreflight와 동일 취지).
    /// </summary>
    public static class StructureAreaService
    {
        public class Result
        {
            public List<StructureArea> Areas = new List<StructureArea>();
            /// <summary>발견 파일/미발견 사유 — 상태 라벨·Overview 노출용.</summary>
            public string ScopeNote = "-";
            public bool Found => Areas.Count > 0;
        }

        public static Result Probe(Document doc)
        {
            var result = new Result();
            if (doc == null || doc.Models.Count == 0)
            {
                result.ScopeNote = "모델 미열림";
                return result;
            }

            var roots = new List<ModelItem>();
            var files = new List<string>();
            foreach (Model model in doc.Models)
            {
                string fileName = null;
                try { fileName = model.FileName; } catch { /* 일부 모델은 FileName 조회 실패 가능 */ }
                string rootName = model.RootItem?.DisplayName;

                if (NwdScope.Structure.MatchesFileName(fileName) || NwdScope.Structure.MatchesFileName(rootName))
                {
                    roots.Add(model.RootItem);
                    files.Add(NwdScope.StripDirectory(fileName ?? rootName ?? "?"));
                    continue;
                }
                CollectFileNodeRoots(model.RootItem, 0, roots, files);
            }

            if (roots.Count == 0)
            {
                result.ScopeNote = "스코프 STR: 대상 파일 미발견 — 파일명 규약 확인 (전체 모델 fallback 안 함)";
                return result;
            }

            // 이름 기준 병합 — 복수 Str 파일(granular 분할)에 같은 영역명이 있으면 한 행으로.
            var byName = new Dictionary<string, StructureArea>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var top = UnwrapSingleFileChild(root);
                int unnamed = 0;
                foreach (ModelItem child in top.Children)
                {
                    string name = child.DisplayName?.Trim();
                    if (string.IsNullOrEmpty(name))
                        name = $"(이름 없음 {++unnamed})";   // 무명 노드도 숨김/투명 대상에서 빠지지 않게 포함
                    if (!byName.TryGetValue(name, out var area))
                    {
                        area = new StructureArea { Name = name };
                        byName[name] = area;
                        result.Areas.Add(area);
                    }
                    area.Items.Add(child);
                }
            }

            result.ScopeNote = result.Areas.Count > 0
                ? $"스코프 STR: {string.Join(", ", files.Distinct())} · 영역 {result.Areas.Count}개"
                : $"스코프 STR: {string.Join(", ", files.Distinct())} · 레벨1 자식 없음";
            return result;
        }

        /// <summary>federated 트리에서 파일 노드만 얕게 따라가며 Str 매칭 루트 수집 (ResolveScopeRoots ② 미러).</summary>
        private static void CollectFileNodeRoots(ModelItem item, int depth, List<ModelItem> roots, List<string> files)
        {
            if (item == null || depth > 3) return;
            foreach (ModelItem child in item.Children)
            {
                string dn = child.DisplayName?.Trim();
                if (!NwdScope.LooksLikeFileNode(dn)) continue;
                if (NwdScope.Structure.MatchesFileName(dn))
                {
                    roots.Add(child);
                    files.Add(dn);   // 매칭 파일의 하위는 더 볼 필요 없음
                }
                else
                {
                    CollectFileNodeRoots(child, depth + 1, roots, files);
                }
            }
        }

        /// <summary>
        /// nwd→nwc 중첩처럼 매칭 루트가 단일 파일 노드(또는 무명 노드) 하나만 감싸고 있으면
        /// 그 안으로 내려가 실제 영역 레벨을 레벨1로 만든다. 영역 노드가 여럿이면 그대로 반환.
        /// (federated 구성별 래핑 깊이 차이 대비 — Windows 실측 확인 필요.)
        /// </summary>
        private static ModelItem UnwrapSingleFileChild(ModelItem root)
        {
            var current = root;
            for (int i = 0; i < 3; i++)
            {
                ModelItem only = null;
                int count = 0;
                foreach (ModelItem child in current.Children)
                {
                    only = child;
                    if (++count > 1) break;
                }
                if (count != 1) return current;

                string dn = only.DisplayName?.Trim();
                if (NwdScope.LooksLikeFileNode(dn) || string.IsNullOrEmpty(dn))
                    current = only;
                else
                    return current;
            }
            return current;
        }
    }
}
