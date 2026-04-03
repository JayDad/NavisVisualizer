using System;
using System.Collections.Generic;
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

        public ColorOverrideEngine(ModelItemSearcher searcher)
        {
            _searcher = searcher;
        }

        public OverrideResult ApplyHydrotest(
            Document doc,
            List<TestPackageData> packages,
            Dictionary<HydrotestStage, ColorSetting> colorSettings,
            DateTime referenceDate,
            bool hideUnmatched = true)
        {
            var result = new OverrideResult();
            var allMatchedItems = new HashSet<ModelItem>();

            // Group items by stage for batch API calls
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

                foreach (var item in items)
                {
                    list.Add(item);
                    foreach (var desc in item.DescendantsAndSelf)
                        allMatchedItems.Add(desc);
                    AddAncestors(allMatchedItems, item);
                }
            }

            // Batch: one API call per stage
            foreach (var kv in stageItems)
            {
                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    var expanded = ExpandWithDescendants(kv.Value);
                    ApplyOverride(doc, expanded, setting);
                }
            }

            if (hideUnmatched)
                ApplyUnmatchedOverride(doc, allMatchedItems);

            return result;
        }

        public OverrideResult ApplySpool(
            Document doc,
            List<SpoolData> spools,
            Dictionary<SpoolStage, ColorSetting> colorSettings,
            DateTime referenceDate,
            bool hideUnmatched = true)
        {
            var result = new OverrideResult();
            var allMatchedItems = new HashSet<ModelItem>();

            // Group items by stage for batch API calls
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

                foreach (var item in items)
                {
                    list.Add(item);
                    foreach (var desc in item.DescendantsAndSelf)
                        allMatchedItems.Add(desc);
                    AddAncestors(allMatchedItems, item);
                }
            }

            // Batch: one API call per stage
            foreach (var kv in stageItems)
            {
                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    var expanded = ExpandWithDescendants(kv.Value);
                    ApplyOverride(doc, expanded, setting);
                }
            }

            if (hideUnmatched)
                ApplyUnmatchedOverride(doc, allMatchedItems);

            return result;
        }

        public void Reset(Document doc)
        {
            doc.Models.ResetAllPermanentMaterials();
        }

        private void ApplyOverride(Document doc, List<ModelItem> items, ColorSetting setting)
        {
            if (items.Count == 0) return;
            var collection = new ModelItemCollection();
            collection.AddRange(items);
            var nwColor = ToNwColor(setting.DisplayColor);
            doc.Models.OverridePermanentColor(collection, nwColor);
            doc.Models.OverridePermanentTransparency(collection, setting.Transparency);
        }

        private void ApplyUnmatchedOverride(Document doc, HashSet<ModelItem> matchedItems)
        {
            var unmatched = new List<ModelItem>();
            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                if (!matchedItems.Contains(item))
                    unmatched.Add(item);
            }

            if (unmatched.Count == 0) return;
            ApplyOverride(doc, unmatched, ColorSetting.Unmatched);
        }

        private void AddAncestors(HashSet<ModelItem> matchedItems, ModelItem item)
        {
            var parent = item.Parent;
            while (parent != null)
            {
                if (!matchedItems.Add(parent)) break;
                parent = parent.Parent;
            }
        }

        private List<ModelItem> ExpandWithDescendants(List<ModelItem> items)
        {
            var expanded = new List<ModelItem>();
            foreach (var item in items)
            {
                foreach (var desc in item.DescendantsAndSelf)
                    expanded.Add(desc);
            }
            return expanded;
        }

        private NwColor ToNwColor(System.Drawing.Color c) =>
            NwColor.FromByteRGB(c.R, c.G, c.B);
    }

    public class OverrideResult
    {
        public int MatchedCount { get; set; }
        public List<string> UnmatchedIds { get; set; } = new List<string>();
        public int UnmatchedCount => UnmatchedIds.Count;
    }
}
