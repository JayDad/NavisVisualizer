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
            Dictionary<HydrotestStatus, ColorSetting> colorSettings,
            bool hideUnmatched = true)
        {
            var result = new OverrideResult();
            var allMatchedItems = new HashSet<ModelItem>();

            var allSpoolIds = packages.SelectMany(p => p.SpoolIds).Distinct();
            var searchResult = _searcher.FindBySpoolIds(allSpoolIds);

            foreach (var pkg in packages)
            {
                var pkgItems = new List<ModelItem>();

                foreach (var spoolId in pkg.SpoolIds)
                {
                    if (searchResult.TryGetValue(spoolId, out var items) && items.Count > 0)
                    {
                        pkgItems.AddRange(items);
                        result.MatchedCount++;
                    }
                    else
                    {
                        result.UnmatchedIds.Add($"{pkg.TestPkgId} → {spoolId}");
                    }
                }

                if (pkgItems.Count > 0 && colorSettings.TryGetValue(pkg.Status, out var setting))
                {
                    var withDescendants = ExpandWithDescendants(pkgItems);
                    ApplyOverride(doc, withDescendants, setting);
                    foreach (var item in withDescendants)
                        allMatchedItems.Add(item);
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
                if (colorSettings.TryGetValue(stage, out var setting))
                {
                    var withDescendants = ExpandWithDescendants(items);
                    ApplyOverride(doc, withDescendants, setting);
                    foreach (var item in withDescendants)
                        allMatchedItems.Add(item);
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
            var unmatched = doc.Models.RootItemDescendantsAndSelf
                .Where(item => !matchedItems.Contains(item))
                .ToList();

            if (unmatched.Count == 0) return;
            ApplyOverride(doc, unmatched, ColorSetting.Unmatched);
        }

        /// <summary>
        /// Expands matched items to include all their descendants,
        /// so child geometry items are also marked as matched.
        /// </summary>
        private List<ModelItem> ExpandWithDescendants(List<ModelItem> items)
        {
            var expanded = new List<ModelItem>();
            foreach (var item in items)
            {
                expanded.Add(item);
                expanded.AddRange(item.DescendantsAndSelf.Skip(1)); // Skip self (already added)
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
