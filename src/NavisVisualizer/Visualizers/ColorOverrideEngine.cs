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
            DateTime referenceDate)
        {
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

            // Reset previous overrides, then apply only matched
            doc.Models.ResetAllPermanentMaterials();

            foreach (var kv in stageItems)
            {
                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverrideWithDescendants(doc, kv.Value, setting);
            }

            return result;
        }

        public OverrideResult ApplySpool(
            Document doc,
            List<SpoolData> spools,
            Dictionary<SpoolStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
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

            // Reset previous overrides, then apply only matched
            doc.Models.ResetAllPermanentMaterials();

            foreach (var kv in stageItems)
            {
                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverrideWithDescendants(doc, kv.Value, setting);
            }

            return result;
        }

        public void Reset(Document doc)
        {
            doc.Models.ResetAllPermanentMaterials();
        }

        private void ApplyOverrideWithDescendants(Document doc, List<ModelItem> items, ColorSetting setting)
        {
            if (items.Count == 0) return;
            var collection = new ModelItemCollection();
            foreach (var item in items)
            {
                foreach (var desc in item.DescendantsAndSelf)
                    collection.Add(desc);
            }
            var nwColor = ToNwColor(setting.DisplayColor);
            doc.Models.OverridePermanentColor(collection, nwColor);
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
    }
}
