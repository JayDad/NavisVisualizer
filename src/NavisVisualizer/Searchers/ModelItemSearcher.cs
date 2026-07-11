using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Searchers
{
    public class ModelItemSearcher
    {
        private Dictionary<string, List<ModelItem>> _index;
        private bool _isBuilt = false;
        private string _lastDocumentId;

        public bool IsIndexBuilt => _isBuilt;
        public int IndexedCount => _index?.Count ?? 0;

        /// <summary>마지막 빌드의 스코프 결과 설명 — 진단 CSV/Tools 출력용 (예: "스코프 MEQ: 04-02_..._MEQ.nwd").</summary>
        public string LastScopeNote { get; private set; }

        /// <summary>스코프로 대상 모델을 못 찾았거나 스코프 인덱스가 0건이라 전체 모델로 fallback했는가.</summary>
        public bool LastScopeFellBack { get; private set; }

        /// <summary>마지막 ResolveScopeRoots가 전체보다 실제로 좁은 루트 집합을 반환했는가
        /// (0건 fallback 재시도 여부 판단용 — federated에선 모델 수 비교로는 알 수 없음).</summary>
        private bool _lastScopeNarrowed;

        public bool NeedsRebuild(Document doc)
        {
            if (!_isBuilt) return true;
            return GetDocumentId(doc) != _lastDocumentId;
        }

        /// <summary>
        /// General BuildIndex — recursive walk, stops when children have no tags.
        /// Used by Spool/Hydrotest/EIT/Sub-system.
        /// scope가 있으면 파일명 키워드에 맞는 모델(또는 federated 트리의 파일 노드)만 walk하고,
        /// 대상이 없거나 결과가 0건이면 전체 모델로 자동 fallback한다.
        /// </summary>
        /// <summary>
        /// hardScope=true면 스코프 파일을 못 찾거나 인덱스가 0건이어도 전체 모델로 넓히지
        /// 않는다 (EIT Tray처럼 특정 nwd에서만 돌아야 하는 탭용 — federated 매칭 실패 시
        /// 전체 트리 walk로 인한 지연 방지). 기본값 false = 기존 3중 fallback 유지.
        /// </summary>
        public void BuildIndex(Document doc, NwdScope scope = null, Action<int, int> onProgress = null, bool hardScope = false)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            var roots = ResolveScopeRoots(doc, scope, hardScope);
            foreach (var root in roots)
                WalkAndIndex(root);

            // 스코프 모델은 찾았지만 인덱스가 비면 (파일은 규약대로인데 내용이 예상과 다른 경우)
            // 규약 오판 가능성 — 전체 모델로 재시도. (하드 스코프는 이 fallback도 안 함.)
            if (!hardScope && scope != null && !LastScopeFellBack && _lastScopeNarrowed && _index.Count == 0)
            {
                _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                foreach (var model in doc.Models)
                    WalkAndIndex(model.RootItem);
                LastScopeFellBack = true;
                LastScopeNote = $"스코프 {scope.Label}: 인덱스 0건 → 전체 모델 fallback";
            }

            _isBuilt = true;
        }

        /// <summary>
        /// 스코프에 맞는 walk 시작점을 결정한다.
        /// 우선순위 체인(scope → scope.Fallback → …)을 앞에서부터 시도해 처음으로 대상 모델이
        /// 잡히는 스코프만 쓴다 — 예: Spool은 SPL 파일이 있으면 SPL만, 없으면 HYDROPKG만.
        /// 각 스코프의 매칭은 2단계:
        /// ① Model.FileName / RootItem.DisplayName 키워드 매칭 (개별 공종 nwd·append 구성),
        /// ② federated NWD(전체 묶음 파일을 연 경우 하위 파일이 트리 안 파일 노드로 들어옴)는
        ///    파일 노드만 얕게 따라 내려가며 매칭 — geometry 트리는 건드리지 않는다.
        /// 체인 전부에서 한 건도 못 찾으면 전체 모델 루트 반환 + fallback 표시.
        /// </summary>
        private List<ModelItem> ResolveScopeRoots(Document doc, NwdScope scope, bool hardScope = false)
        {
            var roots = new List<ModelItem>();
            LastScopeFellBack = false;
            _lastScopeNarrowed = false;

            if (scope == null)
            {
                foreach (var model in doc.Models)
                    roots.Add(model.RootItem);
                LastScopeNote = "전체 모델 (스코프 없음)";
                return roots;
            }

            var chainNotes = new List<string>();
            for (var tier = scope; tier != null; tier = tier.Fallback)
            {
                var names = new List<string>();
                if (TryCollectScopeRoots(doc, tier, roots, names))
                {
                    chainNotes.Add($"{tier.Label}: {string.Join(", ", names)}");
                    LastScopeNote = "스코프 " + string.Join(" → ", chainNotes);
                    return roots;
                }
                chainNotes.Add($"{tier.Label} 없음");
            }

            // 하드 스코프: 스코프 파일을 못 찾아도 전체 모델로 넓히지 않는다 (빈 루트 반환).
            // EIT Tray처럼 "그 nwd에서만 돌아야" 하는 탭이 federated 매칭 실패 시 전체 트리를
            // 훑어 느려지는 것을 막는다 — 인덱스 0건 + 진단 노트로 규약 불일치를 드러낸다.
            if (hardScope)
            {
                LastScopeNote = $"스코프 {string.Join(" → ", chainNotes)} → 하드 스코프: 전체 fallback 안 함 (인덱스 0건 — 파일명 규약 확인)";
                return roots; // empty
            }

            foreach (var model in doc.Models)
                roots.Add(model.RootItem);
            LastScopeFellBack = true;
            _lastScopeNarrowed = false;
            LastScopeNote = $"스코프 {string.Join(" → ", chainNotes)} → 전체 모델 fallback";
            return roots;
        }

        /// <summary>단일 스코프(체인의 한 단계)로 대상 루트를 수집. 한 건도 없으면 false.</summary>
        private bool TryCollectScopeRoots(Document doc, NwdScope tier, List<ModelItem> roots, List<string> names)
        {
            bool narrowed = false;
            foreach (var model in doc.Models)
            {
                string fileName = null;
                try { fileName = model.FileName; } catch { /* 일부 모델은 FileName 조회 실패 가능 */ }
                string rootName = model.RootItem?.DisplayName;

                if (tier.MatchesFileName(fileName) || tier.MatchesFileName(rootName))
                {
                    roots.Add(model.RootItem);
                    names.Add(NwdScope.StripDirectory(fileName ?? rootName ?? "?"));
                    continue;
                }

                // 모델 단위 매칭 실패 — 하위 파일 노드 일부만 들어가든 통째로 빠지든
                // 결과는 전체보다 좁으므로 0건 fallback 재시도 대상이 된다.
                narrowed = true;
                CollectFileNodeRoots(model.RootItem, tier, 0, roots, names);
            }

            if (roots.Count == 0) return false;
            _lastScopeNarrowed = narrowed;
            return true;
        }

        /// <summary>
        /// federated 트리에서 파일 노드만 따라 내려가며 스코프 매칭 루트를 수집.
        /// 매칭된 파일 노드는 그 서브트리 전체가 스코프에 들어가므로 더 내려가지 않는다.
        /// 파일 중첩은 얕으므로 depth 3 제한 (geometry 노드는 파일 확장자가 없어 애초에 안 내려감).
        /// </summary>
        private static void CollectFileNodeRoots(
            ModelItem item, NwdScope scope, int depth, List<ModelItem> roots, List<string> names)
        {
            if (item == null || depth > 3) return;
            foreach (var child in item.Children)
            {
                string dn = child.DisplayName?.Trim();
                if (!NwdScope.LooksLikeFileNode(dn)) continue;
                if (scope.MatchesFileName(dn))
                {
                    roots.Add(child);
                    names.Add(dn);
                }
                else
                {
                    CollectFileNodeRoots(child, scope, depth + 1, roots, names);
                }
            }
        }

        /// <summary>
        /// Level-targeted BuildIndex — finds the tree level where known tags exist,
        /// then indexes ONLY that level. Much faster for Equipment models.
        /// scope가 있으면 대상 모델(파일)만에서 depth 탐지·인덱싱하고,
        /// 스코프 안에서 태그를 한 건도 못 찾으면 전체 모델로 자동 fallback한다.
        /// hardScope=true면 스코프 파일 미발견/태그 미발견 시에도 전체 모델로 넓히지 않는다
        /// (그 nwd에서만 — federated 매칭 실패 시 전 트리 스캔으로 인한 지연 방지).
        /// </summary>
        public void BuildIndexForTags(Document doc, HashSet<string> knownTags, NwdScope scope = null, bool hardScope = false)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            // Normalize tags for comparison
            var normalizedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in knownTags)
            {
                string t = tag.Trim().TrimStart('/').ToUpperInvariant();
                if (!string.IsNullOrEmpty(t))
                    normalizedTags.Add(t);
            }

            var roots = ResolveScopeRoots(doc, scope, hardScope);

            // Step 1: Find the depth where first tag match occurs
            // (depth는 각 루트 기준 상대값 — 탐지와 인덱싱이 같은 roots를 쓰므로 일관됨)
            int targetDepth = FindTagDepthInRoots(roots, normalizedTags);

            // 스코프 모델 안에서 태그를 못 찾으면 스코프 오판 가능성 — 전체 모델로 재탐지 (하드 스코프 제외)
            if (targetDepth < 0 && !hardScope && scope != null && !LastScopeFellBack && _lastScopeNarrowed)
            {
                roots = new List<ModelItem>();
                foreach (var model in doc.Models)
                    roots.Add(model.RootItem);
                LastScopeFellBack = true;
                LastScopeNote = $"스코프 {scope.Label}: 태그 미발견 → 전체 모델 fallback";
                targetDepth = FindTagDepthInRoots(roots, normalizedTags);
            }

            if (targetDepth < 0)
            {
                // No tags found — fallback to general index
                foreach (var root in roots)
                    WalkAndIndex(root);
            }
            else
            {
                // Step 2: Index only at the target depth
                foreach (var root in roots)
                    IndexAtDepth(root, 0, targetDepth);
            }

            _isBuilt = true;
        }

        private int FindTagDepthInRoots(List<ModelItem> roots, HashSet<string> normalizedTags)
        {
            foreach (var root in roots)
            {
                int found = FindTagDepth(root, normalizedTags, 0);
                if (found >= 0) return found;
            }
            return -1;
        }

        /// <summary>
        /// Walk tree to find the depth where a known tag first appears.
        /// </summary>
        private int FindTagDepth(ModelItem item, HashSet<string> tags, int depth)
        {
            string name = item.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                string key = name.TrimStart('/').Trim().ToUpperInvariant();
                // Check exact match or prefix match (tag/VENSKID → tag)
                if (tags.Contains(key))
                    return depth;
                int slash = key.IndexOf('/');
                if (slash > 0 && tags.Contains(key.Substring(0, slash)))
                    return depth;
            }

            // Recurse into children (but limit depth to avoid going too deep)
            if (depth > 20) return -1;

            foreach (var child in item.Children)
            {
                int found = FindTagDepth(child, tags, depth + 1);
                if (found >= 0) return found;
            }

            return -1;
        }

        /// <summary>
        /// Index all nodes at the target depth only.
        /// </summary>
        private void IndexAtDepth(ModelItem item, int currentDepth, int targetDepth)
        {
            if (currentDepth == targetDepth)
            {
                // Index this node
                string name = item.DisplayName?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    string key = name.TrimStart('/').Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        key = key.ToUpperInvariant();
                        AddToIndex(key, item);

                        int slash = key.IndexOf('/');
                        if (slash > 0)
                            AddToIndex(key.Substring(0, slash), item);
                    }
                }
                return; // Don't go deeper
            }

            // Haven't reached target depth yet — keep going
            foreach (var child in item.Children)
                IndexAtDepth(child, currentDepth + 1, targetDepth);
        }

        private void WalkAndIndex(ModelItem item)
        {
            string name = item.DisplayName?.Trim();
            bool isTagLike = !string.IsNullOrEmpty(name) && ContainsDigit(name);

            if (isTagLike)
            {
                string key = name.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    key = key.ToUpperInvariant();
                    AddToIndex(key, item);

                    int slash = key.IndexOf('/');
                    if (slash > 0)
                        AddToIndex(key.Substring(0, slash), item);
                }
            }

            // Decide whether to keep descending — for ALL nodes, tag-like or not.
            // Stopping as soon as no immediate child has a digit breaks federated trees
            // where a digit-bearing file node (e.g. "MEBTray1.nwc") sits above non-digit
            // category nodes ("/SM/MEB/ELEC" -> "/.../PCVTRAY") that still contain deeper
            // tags. So also descend into structural containers (a child with no geometry
            // of its own but with children); only stop once children are geometry.
            //
            // 비태그 노드에도 같은 게이트를 적용하는 이유(§2 "과다 방문" 해소): digit 없는
            // 범주 노드(/CM/PDA/ELEC/PCVTRAY-STW) 바로 아래 geometry가 직접 붙은 경우, 종전엔
            // 무조건 하강해 geometry 서브트리 전체를 COM으로 순회했다 — EIT처럼 이런 구조가
            // 많은 스코프에서 인덱스 빌드(= 첫 가시화 적용)가 가장 느렸던 원인. 인덱스가
            // 필요로 하는 태그는 컴포지트 노드 이름이고 "태그는 geometry 인스턴스 아래에
            // 없다"는 가정은 태그 노드 정지 규칙이 이미 쓰던 것과 동일하다.
            bool descend = false;
            foreach (var child in item.Children)
            {
                string childName = child.DisplayName?.Trim();
                bool childTagLike = !string.IsNullOrEmpty(childName) && ContainsDigit(childName);
                if (childTagLike || (!child.HasGeometry && child.Children.Any()))
                {
                    descend = true;
                    break;
                }
            }

            if (!descend)
                return;

            foreach (var child in item.Children)
                WalkAndIndex(child);
        }

        private void AddToIndex(string key, ModelItem item)
        {
            if (!_index.TryGetValue(key, out var list))
            {
                list = new List<ModelItem>();
                _index[key] = list;
            }
            list.Add(item);
        }

        /// <summary>
        /// Cable-box index: walks the tree and indexes any item whose DisplayName
        /// contains "-BOX". The index key is the prefix BEFORE "-BOX", e.g.
        /// "101780-EMCT-52101_A-ND-BOX001" → key "101780-EMCT-52101_A-ND".
        /// Excel Node IDs are looked up against this same key.
        /// scope가 있으면 대상 모델만 walk — 단, node box가 담길 nwd의 파일명 규약이
        /// 미확정이므로(별도 추출 예정) 스코프 인덱스가 0건이면 전체 모델로 자동 fallback.
        /// </summary>
        public void BuildIndexForBoxes(Document doc, NwdScope scope = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            var roots = ResolveScopeRoots(doc, scope);
            foreach (var root in roots)
                WalkBoxIndex(root);

            if (scope != null && !LastScopeFellBack && _lastScopeNarrowed && _index.Count == 0)
            {
                _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                foreach (var model in doc.Models)
                    WalkBoxIndex(model.RootItem);
                LastScopeFellBack = true;
                LastScopeNote = $"스코프 {scope.Label}: 박스 0건 → 전체 모델 fallback";
            }

            _isBuilt = true;
        }

        private void WalkBoxIndex(ModelItem item)
        {
            string name = item.DisplayName?.Trim() ?? "";
            int idx = name.IndexOf("-BOX", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                string key = name.Substring(0, idx).TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(key))
                    AddToIndex(key.ToUpperInvariant(), item);
                // Box leaves usually have geometry below; no need to recurse for indexing.
                return;
            }

            foreach (var child in item.Children)
                WalkBoxIndex(child);
        }

        public Dictionary<string, List<ModelItem>> FindBySpoolIds(IEnumerable<string> spoolIds)
        {
            if (!_isBuilt)
                throw new InvalidOperationException("인덱스가 빌드되지 않았습니다.");

            var result = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in spoolIds)
            {
                result[id] = _index.TryGetValue(id, out var items)
                    ? items
                    : new List<ModelItem>();
            }
            return result;
        }

        public Dictionary<string, List<ModelItem>> FindByTagPrefix(IEnumerable<string> tagNos)
        {
            return FindBySpoolIds(tagNos);
        }

        /// <summary>Return every indexed item whose key is NOT in <paramref name="excluded"/>.</summary>
        public List<ModelItem> GetItemsExcept(HashSet<string> excluded)
        {
            var result = new List<ModelItem>();
            if (!_isBuilt || _index == null) return result;
            foreach (var kv in _index)
            {
                if (excluded.Contains(kv.Key)) continue;
                result.AddRange(kv.Value);
            }
            return result;
        }

        public IEnumerable<string> GetIndexedKeys() =>
            _index?.Keys ?? Enumerable.Empty<string>();

        /// <summary>
        /// Return index entries whose key maps to more than one item. For the cable-box
        /// index this surfaces nodes that have multiple "-BOX" elements — usually a sign
        /// the box-generation macro produced duplicates for a single node.
        /// </summary>
        public List<KeyValuePair<string, List<ModelItem>>> GetEntriesWithMultipleItems()
        {
            var result = new List<KeyValuePair<string, List<ModelItem>>>();
            if (!_isBuilt || _index == null) return result;
            foreach (var kv in _index)
                if (kv.Value != null && kv.Value.Count > 1)
                    result.Add(kv);
            return result;
        }

        public void Reset()
        {
            _isBuilt = false;
            _lastDocumentId = null;
        }

        private static bool ContainsDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return true;
            return false;
        }

        private string GetDocumentId(Document doc)
        {
            try
            {
                string path = doc.FileName ?? "";
                int modelCount = doc.Models.Count;
                return $"{path}|{modelCount}";
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
