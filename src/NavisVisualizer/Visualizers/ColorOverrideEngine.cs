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
                    ApplyOverride(doc, pkgItems, setting);
                    foreach (var item in pkgItems)
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

                if (colorSettings.TryGetValue(spool.Stage, out var setting))
                {
                    ApplyOverride(doc, items, setting);
                    foreach (var item in items)
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

        private NwColor ToNwColor(System.Drawing.Color c) =>
            new NwColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
    }

    public class OverrideResult
    {
        public int MatchedCount { get; set; }
        public List<string> UnmatchedIds { get; set; } = new List<string>();
        public int UnmatchedCount => UnmatchedIds.Count;
    }
}
