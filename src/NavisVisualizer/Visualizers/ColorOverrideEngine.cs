using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace NavisVisualizer.Visualizers
{
    public class ColorOverrideEngine
    {
        private readonly ModelItemSearcher _searcher;

        private Dictionary<string, ModelItemCollection> _cachedStageCollections
            = new Dictionary<string, ModelItemCollection>();

        public ColorOverrideEngine(ModelItemSearcher searcher)
        {
            _searcher = searcher;
        }

        public OverrideResult ApplyHydrotest(
            Document doc,
            List<TestPackageData> packages,
            Dictionary<HydrotestStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var sw = Stopwatch.StartNew();
            var result = new OverrideResult();
            var stageItems = new Dictionary<HydrotestStage, List<ModelItem>>();

            var allPkgIds = packages.Select(p => p.TestPkgId).Distinct();
            var searchResult = _searcher.FindBySpoolIds(allPkgIds);

            foreach (var pkg in packages)
            {
                if (!searchResult.TryGetValue(pkg.TestPkgId, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(pkg.TestPkgId);
                    continue;
                }
                result.MatchedCount++;

                var stage = pkg.GetStageAtDate(referenceDate);
                if (!colorSettings.ContainsKey(stage)) continue;

                if (!stageItems.TryGetValue(stage, out var list))
                {
                    list = new List<ModelItem>();
                    stageItems[stage] = list;
                }
                list.AddRange(items);
            }

            result.TimingMatch = sw.ElapsedMilliseconds;

            _cachedStageCollections.Clear();

            // Hydrotest: apply to package nodes directly without expanding descendants.
            // Navisworks propagates parent color overrides to children visually.
            // If not, we fall back to shallow expansion (direct children only).
            long expandMs = 0;
            foreach (var kv in stageItems)
            {
                var swExpand = Stopwatch.StartNew();
                string key = kv.Key.ToString();
                var collection = ShallowExpandToCollection(kv.Value);
                _cachedStageCollections[key] = collection;
                result.TotalItemsColored += collection.Count;
                expandMs += swExpand.ElapsedMilliseconds;
            }
            result.TimingExpand = expandMs;

            // Apply colors
            var swApply = Stopwatch.StartNew();
            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                if (_cachedStageCollections.TryGetValue(key, out var collection)
                    && colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
            }
            result.TimingApply = swApply.ElapsedMilliseconds;
            result.TimingTotal = sw.ElapsedMilliseconds;

            return result;
        }

        public OverrideResult ApplySpool(
            Document doc,
            List<SpoolData> spools,
            Dictionary<SpoolStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var sw = Stopwatch.StartNew();
            var result = new OverrideResult();
            var stageItems = new Dictionary<SpoolStage, List<ModelItem>>();

            var allSpoolIds = spools.Select(s => s.SpoolId).Distinct();
            var searchResult = _searcher.FindBySpoolIds(allSpoolIds);

            foreach (var spool in spools)
            {
                if (!searchResult.TryGetValue(spool.SpoolId, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(spool.SpoolId);
                    continue;
                }
                result.MatchedCount++;

                var stage = spool.GetStageAtDate(referenceDate);
                if (!colorSettings.ContainsKey(stage)) continue;

                if (!stageItems.TryGetValue(stage, out var list))
                {
                    list = new List<ModelItem>();
                    stageItems[stage] = list;
                }
                list.AddRange(items);
            }

            result.TimingMatch = sw.ElapsedMilliseconds;

            _cachedStageCollections.Clear();

            long expandMs = 0;
            foreach (var kv in stageItems)
            {
                var swExpand = Stopwatch.StartNew();
                string key = kv.Key.ToString();
                var collection = ExpandToCollection(kv.Value);
                _cachedStageCollections[key] = collection;
                result.TotalItemsColored += collection.Count;
                expandMs += swExpand.ElapsedMilliseconds;
            }
            result.TimingExpand = expandMs;

            var swApply = Stopwatch.StartNew();
            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                if (_cachedStageCollections.TryGetValue(key, out var collection)
                    && colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
            }
            result.TimingApply = swApply.ElapsedMilliseconds;
            result.TimingTotal = sw.ElapsedMilliseconds;

            return result;
        }

        public bool UpdateStageColor(Document doc, string stageKey, ColorSetting setting)
        {
            if (!_cachedStageCollections.TryGetValue(stageKey, out var collection))
                return false;
            ApplyOverride(doc, collection, setting);
            return true;
        }

        public bool HasCachedData => _cachedStageCollections.Count > 0;

        public void Reset(Document doc)
        {
            doc.Models.ResetAllPermanentMaterials();
            _cachedStageCollections.Clear();
        }

        /// <summary>
        /// Shallow expansion: adds the item itself and its direct Children only (1 level deep).
        /// Used for Hydrotest packages where deep expansion is too slow.
        /// </summary>
        private ModelItemCollection ShallowExpandToCollection(List<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            foreach (var item in items)
            {
                collection.Add(item);
                foreach (var child in item.Children)
                    collection.Add(child);
            }
            return collection;
        }

        /// <summary>
        /// Deep expansion: adds the item and ALL descendants recursively.
        /// Used for Spool items where subtrees are small.
        /// </summary>
        private ModelItemCollection ExpandToCollection(List<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            foreach (var item in items)
            {
                foreach (var desc in item.DescendantsAndSelf)
                    collection.Add(desc);
            }
            return collection;
        }

        private void ApplyOverride(Document doc, ModelItemCollection collection, ColorSetting setting)
        {
            if (collection.Count == 0) return;
            doc.Models.OverridePermanentColor(collection, ToNwColor(setting.DisplayColor));
            doc.Models.OverridePermanentTransparency(collection, setting.Transparency);
        }

        private NwColor ToNwColor(System.Drawing.Color c) =>
            NwColor.FromByteRGB(c.R, c.G, c.B);
    }

    public class OverrideResult
    {
        public int MatchedCount { get; set; }
        public List<string> UnmatchedIds { get; set; } = new List<string>();
        public int UnmatchedCount => UnmatchedIds.Count;
        public int TotalItemsColored { get; set; }

        // Timing (ms)
        public long TimingMatch { get; set; }
        public long TimingExpand { get; set; }
        public long TimingApply { get; set; }
        public long TimingTotal { get; set; }
    }
}
