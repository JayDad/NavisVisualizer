using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Services;

namespace NavisVisualizer.Searchers
{
    public class ModelItemSearcher
    {
        private Dictionary<string, List<ModelItem>> _index;
        private bool _isBuilt = false;
        private string _lastDocumentId;

        public bool IsIndexBuilt => _isBuilt;
        public int IndexedCount => _index?.Count ?? 0;

        /// <summary>마지막 빌드의 스코프 결과 설명 — 진단 CSV/Tools 출력용 (예: "스코프 MEQ[MEQ]: 04-02_..._MEQ.nwd").</summary>
        public string LastScopeNote { get; private set; }

        /// <summary>마지막 빌드에서 스코프 파일이 미지정(별칭 미매칭·지정 파일 미발견)이라 인덱스가 0건이었는가.
        /// 전체 모델 자동 fallback은 폐지됐으므로(2026-09) 이 값이 true면 사용자가 Overview 매핑에서
        /// 파일을 지정(또는 전체 모델을 명시 선택)해야 매칭이 나온다.</summary>
        public bool LastScopeUnmapped { get; private set; }

        /// <summary>walk에서 건너뛸 하위 파일 노드 이름 (직접 지정 매핑에서 미선택 하위 파일 — 예: SPOOL 선택 + SUP 미선택).</summary>
        private HashSet<string> _excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>마지막 빌드에서 실제로 인덱싱한 스코프 루트(공종 nwd 파일 노드). Sub-system
        /// isolate가 "데이터가 있는 공종 파일 전체"를 숨김 스코프로 잡는 데 쓴다 — 선택 항목이
        /// 없는 파일(예: 선택 sub-system에 기계가 없어도 MEQ 파일)도 스코프에 포함되게 한다.
        /// 미지정이면 빈 리스트. 빌드 전엔 빈 리스트.</summary>
        private List<ModelItem> _scopeRoots = new List<ModelItem>();
        public IReadOnlyList<ModelItem> ScopeRoots => _scopeRoots;

        public bool NeedsRebuild(Document doc)
        {
            if (!_isBuilt) return true;
            return GetDocumentId(doc) != _lastDocumentId;
        }

        /// <summary>
        /// General BuildIndex — recursive walk, stops when children have no tags.
        /// Used by Hydrotest (digit 보유 DisplayName 전부 인덱싱).
        /// scope가 있으면 ScopeMappingService가 해석한 파일 노드(프로파일 별칭 자동 인식 또는 사용자
        /// 직접 지정)만 walk한다. **미지정이면 walk 없이 인덱스 0건** — 전체 모델 자동 fallback은
        /// 폐지됨(2026-09 사용자 결정). 적용 전 ScopeGate가 파일 지정을 묻는다.
        /// </summary>
        public void BuildIndex(Document doc, NwdScope scope = null, Action<int, int> onProgress = null)
        {
            using (var perf = PerfLog.Time("인덱스 빌드(general walk)"))
            {
                _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                _isBuilt = false;
                _lastDocumentId = GetDocumentId(doc);

                var roots = ResolveScopeRoots(doc, scope);
                _scopeRoots = roots;   // isolate 스코프용 — 공종 파일 루트 노출
                foreach (var root in roots)
                    WalkAndIndex(root);

                if (scope != null && !LastScopeUnmapped && _index.Count == 0)
                    LastScopeNote += " · 인덱스 0건 (파일 안에 digit 보유 노드 없음 — 매핑 확인)";

                _isBuilt = true;
                perf.Items = _index.Count;
                perf.Note = LastScopeNote;
            }
        }

        /// <summary>
        /// 스코프에 맞는 walk 시작점 — ScopeMappingService.Resolve에 위임 (문서별 직접 지정 →
        /// 프로파일 별칭 자동 인식 순, 체인 Fallback 포함). 미지정이면 빈 리스트 + LastScopeUnmapped.
        /// 직접 지정에서 미선택 하위 파일은 _excludedFiles로 받아 walk에서 건너뛴다.
        /// </summary>
        private List<ModelItem> ResolveScopeRoots(Document doc, NwdScope scope)
        {
            var res = ScopeMappingService.Resolve(doc, scope);
            LastScopeNote = res.Note;
            LastScopeUnmapped = scope != null && !res.IsMapped;
            _excludedFiles = res.ExcludedFiles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return res.Roots;
        }

        /// <summary>직접 지정 매핑에서 제외된 하위 파일 노드인가 (walk 게이트 공통).</summary>
        private bool IsExcludedFileNode(string childName) =>
            _excludedFiles.Count > 0 && NwdScope.LooksLikeFileNode(childName) && _excludedFiles.Contains(childName);

        /// <summary>
        /// Level-targeted BuildIndex — known tag만 인덱싱하는 다중 깊이 게이트 walk.
        /// scope가 있으면 ScopeMappingService가 해석한 파일 노드만 walk한다. 스코프 미지정이거나
        /// 스코프 안에서 태그를 못 찾아도 **전체 모델로 넓히지 않는다** (2026-09 — 구 hardScope가
        /// 전 탭의 기본 동작이 됨): 인덱스 0건 + 진단 노트, 파일 지정은 ScopeGate/Overview 매핑에서.
        /// </summary>
        public void BuildIndexForTags(Document doc, HashSet<string> knownTags, NwdScope scope = null)
        {
            using (var perf = PerfLog.Time("인덱스 빌드(레벨 타겟)"))
            {
                _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
                _isBuilt = false;
                _lastDocumentId = GetDocumentId(doc);
                perf.Rows = knownTags.Count;

                // Normalize tags for comparison
                var normalizedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tag in knownTags)
                {
                    string t = tag.Trim().TrimStart('/').ToUpperInvariant();
                    if (!string.IsNullOrEmpty(t))
                        normalizedTags.Add(t);
                }

                var roots = ResolveScopeRoots(doc, scope);
                _scopeRoots = roots;   // isolate 스코프용 — 공종 파일 루트 노출 (미지정이면 빈 리스트)

                // 게이트 walk로 known tag를 만나면 인덱싱 후 그 서브트리는 정지(태그=컴포지트
                // 이름, 아래는 자기 geometry라 더 펼칠 필요 없음 — 사용자 지적 2026-07). 매칭 안 된
                // 노드만 자식 게이트로 계속 하강 → 여러 깊이에 섞인 태그를 전부 잡되(§2 다중 깊이
                // 해결), 매칭 서브트리·geometry 숲은 안 훑어 빠르고 인덱스도 린(known tag만).
                // (구 IndexRootsAtOwnDepth는 첫 매칭 깊이 하나만 인덱싱 → 다른 깊이 항목 미매칭 §2.)
                var depths = new List<int>();   // 태그가 발견된 깊이들 (진단용, 다중 가능)
                int rootsWithoutTags = IndexRootsKnownTags(roots, normalizedTags, depths);

                if (depths.Count == 0)
                {
                    // 태그 미발견 — general walk로 그 파일 전체를 훑지 않는다 (성능 audit 4-3: 태그 형식이
                    // 규약과 다르면 종전엔 nwd 전체 COM 순회가 일어났다). known-tag walk가 이미 모든 깊이를
                    // 봤으므로 general walk가 더 찾아줄 것도 없다. 인덱스 0건 + 진단 노트로 드러낸다.
                    if (!LastScopeUnmapped)
                        LastScopeNote = (LastScopeNote ?? "") + " → 태그 미발견 (태그 형식/파일 매핑 확인)";
                }
                else
                {
                    // 진단: 태그가 여러 깊이에 걸쳐 있으면(정상 — 다중 깊이 walk가 전부 잡음) 깊이 목록,
                    // 태그 없는 루트 개수를 노트로 드러낸다 (조용한 누락 방지).
                    var distinct = depths.Distinct().OrderBy(d => d).ToList();
                    if (distinct.Count > 1)
                        LastScopeNote = (LastScopeNote ?? "") + $" · 태그 깊이 다중({string.Join(",", distinct)})";
                    if (rootsWithoutTags > 0)
                        LastScopeNote = (LastScopeNote ?? "") + $" · 태그 없는 루트 {rootsWithoutTags}개 제외";
                }

                _isBuilt = true;
                perf.Items = _index.Count;
                perf.Note = $"depth={(depths.Count > 0 ? string.Join(",", depths.Distinct().OrderBy(d => d)) : "-1")} · {LastScopeNote}";
            }
        }

        /// <summary>
        /// 각 루트를 known-tag 게이트 walk로 인덱싱한다(다중 깊이). 태그를 하나도 못 찾은 루트
        /// 개수를 반환하고, 발견된 깊이는 <paramref name="depths"/>에 누적한다(진단용, 다중 가능).
        /// </summary>
        private int IndexRootsKnownTags(List<ModelItem> roots, HashSet<string> normalizedTags, List<int> depths)
        {
            int rootsWithoutTags = 0;
            foreach (var root in roots)
                if (!WalkAndIndexKnownTags(root, normalizedTags, 0, depths))
                    rootsWithoutTags++;
            return rootsWithoutTags;
        }

        /// <summary>
        /// known tag만 인덱싱하는 게이트 walk. 노드 이름이 known tag와 정확 일치(또는 "tag/suffix"
        /// 접두 일치)하면 인덱싱 후 **그 서브트리는 정지** — 태그는 컴포지트 이름이고 그 아래는 자기
        /// geometry라 더 펼칠 필요가 없다(사용자 지적 2026-07). 정확 일치라 federated 파일 노드
        /// (예: MEBTray1.nwc — digit 보유지만 known tag는 아님)는 정지 대상이 아니라 계속 하강한다.
        /// 매칭 안 된 노드는 WalkAndIndex와 동일 게이트(자식이 tag-like 또는 구조 컨테이너일 때만
        /// 하강)로 내려가 여러 깊이에 섞인 태그를 전부 잡는다(§2 다중 깊이 해결). 반환: 이 서브트리에서
        /// 태그를 하나라도 찾았는가. 발견 깊이는 depths에 누적.
        /// </summary>
        private bool WalkAndIndexKnownTags(ModelItem item, HashSet<string> knownTags, int depth, List<int> depths)
        {
            string name = item.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                string key = name.TrimStart('/').Trim().ToUpperInvariant();
                bool matched = false;
                if (!string.IsNullOrEmpty(key))
                {
                    if (knownTags.Contains(key)) { AddToIndex(key, item); matched = true; }
                    int slash = key.IndexOf('/');
                    if (slash > 0 && knownTags.Contains(key.Substring(0, slash)))
                    { AddToIndex(key.Substring(0, slash), item); matched = true; }
                }
                if (matched)
                {
                    if (!depths.Contains(depth)) depths.Add(depth);
                    return true;   // 정확 매칭 → 아래는 자기 geometry, 더 안 펼침
                }
            }

            // 매칭 안 됨 → 자식에 tag-like 또는 구조 컨테이너가 있을 때만 하강 (WalkAndIndex와 동일 게이트).
            var children = new List<ModelItem>();
            bool descend = false;
            foreach (var child in item.Children)
            {
                string cn = child.DisplayName?.Trim();
                if (IsExcludedFileNode(cn)) continue;   // 직접 지정에서 뺀 하위 파일(예: SUP) 건너뜀
                children.Add(child);
                if (descend) continue;
                bool childTagLike = !string.IsNullOrEmpty(cn) && ContainsDigit(cn);
                if (childTagLike || (!child.HasGeometry && child.Children.Any()))
                    descend = true;
            }
            if (!descend) return false;

            bool found = false;
            foreach (var child in children)
                found |= WalkAndIndexKnownTags(child, knownTags, depth + 1, depths);
            return found;
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
            // 자식은 한 번만 열거한다 (성능 audit 4-4) — Navisworks ModelItem.Children 열거는
            // 관리형 리스트보다 비싸서, 종전의 "판단 루프 + 하강 루프" 이중 열거가 walk 비용을
            // 키웠다. descend가 확정된 뒤에는 나머지 자식의 HasGeometry/Children.Any() 검사도 생략.
            var children = new List<ModelItem>();
            bool descend = false;
            foreach (var child in item.Children)
            {
                string childName = child.DisplayName?.Trim();
                if (IsExcludedFileNode(childName)) continue;   // 직접 지정에서 뺀 하위 파일 건너뜀
                children.Add(child);
                if (descend) continue;
                bool childTagLike = !string.IsNullOrEmpty(childName) && ContainsDigit(childName);
                if (childTagLike || (!child.HasGeometry && child.Children.Any()))
                    descend = true;
            }

            if (!descend)
                return;

            foreach (var child in children)
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
        /// scope가 있으면 매핑된 파일만 walk. 미지정이면 0건 (전체 fallback 없음 — Tools 탭이
        /// ScopeGate로 먼저 파일 지정을 묻는다).
        /// </summary>
        public void BuildIndexForBoxes(Document doc, NwdScope scope = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            var roots = ResolveScopeRoots(doc, scope);
            _scopeRoots = roots;
            foreach (var root in roots)
                WalkBoxIndex(root);

            if (scope != null && !LastScopeUnmapped && _index.Count == 0)
                LastScopeNote += " · 박스 0건 (매핑 확인)";

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
            {
                if (IsExcludedFileNode(child.DisplayName?.Trim())) continue;
                WalkBoxIndex(child);
            }
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
            _scopeRoots = new List<ModelItem>();
        }

        private static bool ContainsDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return true;
            return false;
        }

        private string GetDocumentId(Document doc) => DocumentFingerprint(doc);

        /// <summary>
        /// 문서 지문 — 인덱스/형상 캐시 최신성 판정의 공용 기준 (SubSystemTab·CableClashService 공유).
        /// 경로+모델 수만으로는 "같은 개수로 모델 교체"를 구분 못 해(2차 audit P1) 모델별 파일명까지
        /// 포함한다. 단, **같은 파일 재로드**는 지문이 같아 여기로는 못 잡는다 — 그 경우는
        /// MainDockablePanel의 문서 이벤트(ActiveDocumentChanged/FileNameChanged) 무효화가 담당.
        /// </summary>
        public static string DocumentFingerprint(Document doc)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(doc.FileName ?? "").Append('|').Append(doc.Models.Count);
                foreach (var model in doc.Models)
                {
                    string fn = null;
                    try { fn = model.FileName; } catch { /* 일부 모델은 FileName 조회 실패 가능 */ }
                    sb.Append('|').Append(fn ?? model.RootItem?.DisplayName ?? "?");
                }
                return sb.ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
