using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// Aggregation scope for the match list/stats. Judged per matched node
    /// (list-row scale, thousands), never per geometry leaf.
    /// </summary>
    public enum MatchScope
    {
        /// <summary>전체 모델 — no filtering (default, current behaviour).</summary>
        FullModel,
        /// <summary>숨김 제외 — drop keys whose every item is (ancestor-)hidden.</summary>
        ExcludeHidden,
        /// <summary>Clipping 영역 — not hidden AND inside every active clip plane.</summary>
        ClippingVolume,
        /// <summary>선택 항목 — item, its ancestor or its descendant is in the 3D selection.</summary>
        SelectedItems,
    }

    public static class MatchScopeInfo
    {
        public static readonly MatchScope[] Ordered =
        {
            MatchScope.FullModel, MatchScope.ExcludeHidden,
            MatchScope.ClippingVolume, MatchScope.SelectedItems,
        };

        public static string Label(MatchScope scope)
        {
            switch (scope)
            {
                case MatchScope.ExcludeHidden:  return "숨김 제외";
                case MatchScope.ClippingVolume: return "Clipping 영역";
                case MatchScope.SelectedItems:  return "선택 항목";
                default:                        return "전체 모델";
            }
        }
    }

    /// <summary>
    /// Judges which keys of a matched-key → ModelItems map fall inside a scope.
    /// One instance per tab (each tab owns its matched set).
    ///
    /// Recompute policy: no section/hide/selection change events are hooked, so state
    /// is re-read only when the user presses the scope [적용] button. Re-applying the
    /// SAME scope recomputes (that press IS the explicit refresh), while switching to a
    /// previously computed scope returns the cached set for free. Callers must
    /// Invalidate() whenever the matched set changes (가시화 적용, data reload).
    /// </summary>
    public class ScopeFilter
    {
        private readonly SectionService _sectionSvc;
        private readonly Dictionary<MatchScope, HashSet<string>> _cache
            = new Dictionary<MatchScope, HashSet<string>>();

        public MatchScope CurrentScope { get; private set; } = MatchScope.FullModel;
        public string Diagnostics { get; private set; } = "";

        public ScopeFilter(SectionService sectionSvc)
        {
            _sectionSvc = sectionSvc;
        }

        /// <summary>
        /// Returns the keys of <paramref name="itemsByKey"/> that are inside
        /// <paramref name="scope"/>, or null for FullModel (= no filtering).
        /// A key with no items (not found in the model) is never in scope.
        /// </summary>
        public HashSet<string> Apply(Document doc, MatchScope scope,
            Dictionary<string, List<ModelItem>> itemsByKey)
        {
            if (scope == MatchScope.FullModel)
            {
                CurrentScope = scope;
                Diagnostics = "";
                return null;
            }

            // Switching to a previously computed scope → cached. Same-scope re-apply
            // → recompute against the current section/hide/selection state.
            if (scope != CurrentScope && _cache.TryGetValue(scope, out var cached))
            {
                CurrentScope = scope;
                return cached;
            }

            var result = Compute(doc, scope, itemsByKey);
            _cache[scope] = result;
            CurrentScope = scope;
            return result;
        }

        /// <summary>Drop all cached verdicts. Call when the matched set changes.</summary>
        public void Invalidate() => _cache.Clear();

        /// <summary>Back to FullModel without recomputing; cached scopes stay usable.</summary>
        public void SetFullModel()
        {
            CurrentScope = MatchScope.FullModel;
            Diagnostics = "";
        }

        /// <summary>Full reset (cache + scope). Call on data reload.</summary>
        public void Reset()
        {
            _cache.Clear();
            SetFullModel();
        }

        private HashSet<string> Compute(Document doc, MatchScope scope,
            Dictionary<string, List<ModelItem>> itemsByKey)
        {
            var inScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || itemsByKey == null || itemsByKey.Count == 0)
            {
                Diagnostics = "범위 진단: 대상 없음";
                return inScope;
            }

            List<ClipPlane> planes = null;
            if (scope == MatchScope.ClippingVolume)
                planes = _sectionSvc.GetActiveClipPlanes(doc);

            // For SelectedItems: a matched item is in scope when the selection contains
            // the item itself, one of its ancestors (selection above), or one of its
            // descendants (selection below — covered by adding the selection's ancestor
            // chains to the lookup set). Selection counts are small, so this stays cheap.
            HashSet<ModelItem> selected = null, selectedAndAncestors = null;
            if (scope == MatchScope.SelectedItems)
            {
                selected = new HashSet<ModelItem>();
                selectedAndAncestors = new HashSet<ModelItem>();
                foreach (ModelItem sel in doc.CurrentSelection.SelectedItems)
                {
                    selected.Add(sel);
                    for (var cur = sel; cur != null; cur = cur.Parent)
                        selectedAndAncestors.Add(cur);
                }
            }

            int noItem = 0, hiddenOut = 0, clippedOut = 0, otherOut = 0;
            foreach (var kv in itemsByKey)
            {
                var items = kv.Value;
                if (items == null || items.Count == 0) { noItem++; continue; }

                bool pass = false, anyNotHidden = false;
                foreach (var item in items)
                {
                    if (item == null) continue;
                    switch (scope)
                    {
                        case MatchScope.ExcludeHidden:
                            if (!SectionService.IsEffectivelyHidden(item))
                                pass = true;
                            break;

                        case MatchScope.ClippingVolume:
                            // Same semantics as the Cable "보이는 것만" judgement:
                            // not hidden AND center inside every active plane.
                            if (SectionService.IsEffectivelyHidden(item)) break;
                            anyNotHidden = true;
                            BoundingBox3D bbox;
                            try { bbox = item.BoundingBox(); }
                            catch { break; }
                            if (bbox == null) break;
                            if (_sectionSvc.IsPointVisible(bbox.Center, planes))
                                pass = true;
                            break;

                        case MatchScope.SelectedItems:
                            if (selectedAndAncestors.Contains(item)) { pass = true; break; }
                            for (var cur = item.Parent; cur != null; cur = cur.Parent)
                            {
                                if (selected.Contains(cur)) { pass = true; break; }
                            }
                            break;
                    }
                    if (pass) break;
                }

                if (pass) inScope.Add(kv.Key);
                else if (scope == MatchScope.ClippingVolume) { if (anyNotHidden) clippedOut++; else hiddenOut++; }
                else if (scope == MatchScope.ExcludeHidden) hiddenOut++;
                else otherOut++;
            }

            switch (scope)
            {
                case MatchScope.ExcludeHidden:
                    Diagnostics = $"범위 진단({MatchScopeInfo.Label(scope)}): 대상 {itemsByKey.Count}, 모델미존재 {noItem}, 숨김제외 {hiddenOut}, 포함 {inScope.Count}";
                    break;
                case MatchScope.ClippingVolume:
                    Diagnostics = $"범위 진단({MatchScopeInfo.Label(scope)}): 대상 {itemsByKey.Count}, 활성평면 {planes.Count}, 모델미존재 {noItem}, 숨김제외 {hiddenOut}, 단면제외 {clippedOut}, 포함 {inScope.Count}";
                    break;
                case MatchScope.SelectedItems:
                    Diagnostics = $"범위 진단({MatchScopeInfo.Label(scope)}): 대상 {itemsByKey.Count}, 모델미존재 {noItem}, 선택외 {otherOut}, 포함 {inScope.Count}";
                    break;
            }
            return inScope;
        }
    }
}
