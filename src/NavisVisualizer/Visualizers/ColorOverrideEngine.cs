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

            _cachedStageCollections.Clear();

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                _cachedStageCollections[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
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

            _cachedStageCollections.Clear();

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                _cachedStageCollections[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
            }

            return result;
        }

        public OverrideResult ApplyEquipment(
            Document doc,
            List<EquipmentData> equipments,
            Dictionary<EquipmentStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var result = new OverrideResult();
            var stageItems = new Dictionary<EquipmentStage, List<ModelItem>>();

            var allTagNos = equipments.Select(e => e.TagNo).Distinct();
            var searchResult = _searcher.FindByTagPrefix(allTagNos);

            foreach (var equip in equipments)
            {
                if (!searchResult.TryGetValue(equip.TagNo, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(equip.TagNo);
                    continue;
                }
                result.MatchedCount++;

                var stage = equip.GetStageAtDate(referenceDate);
                if (!colorSettings.ContainsKey(stage)) continue;

                if (!stageItems.TryGetValue(stage, out var list))
                {
                    list = new List<ModelItem>();
                    stageItems[stage] = list;
                }
                list.AddRange(items);
            }

            // Equipment: matched nodes only (same as Spool/Hydrotest)
            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                _cachedStageCollections[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
            }

            return result;
        }

        public OverrideResult ApplyEit(
            Document doc,
            List<EitTrayData> trays,
            Dictionary<EitStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var result = new OverrideResult();
            var stageItems = new Dictionary<EitStage, List<ModelItem>>();

            var allTrayNos = trays.Select(t => t.TrayNumber).Distinct();
            var searchResult = _searcher.FindBySpoolIds(allTrayNos);

            foreach (var tray in trays)
            {
                if (!searchResult.TryGetValue(tray.TrayNumber, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(tray.TrayNumber);
                    continue;
                }
                result.MatchedCount++;

                var stage = tray.GetStageAtDate(referenceDate);
                if (!colorSettings.ContainsKey(stage)) continue;

                if (!stageItems.TryGetValue(stage, out var list))
                {
                    list = new List<ModelItem>();
                    stageItems[stage] = list;
                }
                list.AddRange(items);
            }

            _cachedStageCollections.Clear();

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                _cachedStageCollections[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                    ApplyOverride(doc, collection, setting);
            }

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

        private ModelItemCollection ToCollection(List<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            collection.AddRange(items);
            return collection;
        }

        private void ApplyOverride(Document doc, ModelItemCollection collection, ColorSetting setting)
        {
            if (collection.Count == 0) return;
            doc.Models.OverridePermanentColor(collection, ToNwColor(setting.DisplayColor));
            if (setting.Transparency > 0.001)
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
