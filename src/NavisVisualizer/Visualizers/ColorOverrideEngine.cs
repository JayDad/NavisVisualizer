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
        // Spool / Hydrotest / EIT Tray share the same full-walk index.
        // Equipment uses its own level-targeted index.
        // Cable Pull uses a third index keyed on the "{NodeId}-BOX..." prefix.
        private readonly ModelItemSearcher _tagSearcher;
        private readonly ModelItemSearcher _equipmentSearcher;
        private readonly ModelItemSearcher _cableBoxSearcher;

        private Dictionary<string, ModelItemCollection> _cachedStageCollections
            = new Dictionary<string, ModelItemCollection>();

        // Cable-specific caches (per-node items, per-node stage, hidden, last settings)
        private Dictionary<string, ModelItemCollection> _cableNodeItems
            = new Dictionary<string, ModelItemCollection>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CableStage> _cableNodeStages
            = new Dictionary<string, CableStage>(StringComparer.OrdinalIgnoreCase);
        private ModelItemCollection _cableHiddenItems;
        private Dictionary<CableStage, ColorSetting> _cableLastSettings;
        private bool _cableFilterFocusActive;

        public ColorOverrideEngine(ModelItemSearcher tagSearcher, ModelItemSearcher equipmentSearcher, ModelItemSearcher cableBoxSearcher)
        {
            _tagSearcher = tagSearcher;
            _equipmentSearcher = equipmentSearcher;
            _cableBoxSearcher = cableBoxSearcher;
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
            var searchResult = _tagSearcher.FindBySpoolIds(allPkgIds);

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
            var searchResult = _tagSearcher.FindBySpoolIds(allSpoolIds);

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
            var searchResult = _equipmentSearcher.FindByTagPrefix(allTagNos);

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
            Dictionary<EitStage, ColorSetting> colorSettings)
        {
            var result = new OverrideResult();
            var stageItems = new Dictionary<EitStage, List<ModelItem>>();

            // Model indexes strip leading '/' — match Excel tray numbers accordingly
            var normalizedIds = trays.Select(t => EitTrayData.NormalizeId(t.TrayNumber)).Distinct();
            var searchResult = _tagSearcher.FindBySpoolIds(normalizedIds);

            foreach (var tray in trays)
            {
                string key = EitTrayData.NormalizeId(tray.TrayNumber);
                if (!searchResult.TryGetValue(key, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(tray.TrayNumber);
                    continue;
                }
                result.MatchedCount++;

                var stage = tray.GetStage();
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

        /// <summary>
        /// Cable Pull: matches each Excel Node to a "{NodeId}-BOX..." element and
        /// colors it by aggregate progress stage. Returns matched/unmatched IDs.
        /// Per-node matched items are cached for filter-focus / selection sync.
        /// Unmatched boxes (in model but no Excel data) are NOT hidden here — caller
        /// invokes HideUnmatchedCableBoxes for that.
        /// </summary>
        public OverrideResult ApplyCable(
            Document doc,
            List<CableNodeData> nodes,
            Dictionary<CableStage, ColorSetting> colorSettings)
        {
            var result = new OverrideResult();
            var stageItems = new Dictionary<CableStage, List<ModelItem>>();

            var normalizedIds = nodes.Select(n => CableNodeData.NormalizeId(n.NodeId)).Distinct();
            var searchResult = _cableBoxSearcher.FindBySpoolIds(normalizedIds);

            _cableNodeItems.Clear();
            _cableNodeStages.Clear();

            foreach (var node in nodes)
            {
                string key = CableNodeData.NormalizeId(node.NodeId);
                if (!searchResult.TryGetValue(key, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(node.NodeId);
                    continue;
                }
                result.MatchedCount++;

                var col = new ModelItemCollection();
                col.AddRange(items);
                _cableNodeItems[node.NodeId] = col;

                var stage = node.GetStage();
                _cableNodeStages[node.NodeId] = stage;
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

            _cableLastSettings = colorSettings;
            _cableFilterFocusActive = false;
            return result;
        }

        /// <summary>Hide every indexed cable box that didn't match an Excel Node.</summary>
        public int HideUnmatchedCableBoxes(Document doc, IEnumerable<string> matchedNodeIds)
        {
            if (!_cableBoxSearcher.IsIndexBuilt) return 0;

            var matchedKeys = new HashSet<string>(
                matchedNodeIds.Select(CableNodeData.NormalizeId),
                StringComparer.OrdinalIgnoreCase);

            // Re-walk the index to collect items whose key isn't in the matched set.
            // The Searcher exposes its index only via FindBySpoolIds, so call it with
            // every indexed key — by feeding the union (matched + everything else we
            // can pull from the Excel-mismatched residue is unknown). Simpler: ask the
            // searcher for all unmatched keys via a new helper.
            var unmatchedItems = _cableBoxSearcher.GetItemsExcept(matchedKeys);

            var collection = new ModelItemCollection();
            collection.AddRange(unmatchedItems);
            if (collection.Count > 0)
                doc.Models.SetHidden(collection, true);

            _cableHiddenItems = collection;
            return collection.Count;
        }

        public void RestoreHiddenCableBoxes(Document doc)
        {
            if (_cableHiddenItems != null && _cableHiddenItems.Count > 0)
                doc.Models.SetHidden(_cableHiddenItems, false);
            _cableHiddenItems = null;
        }

        /// <summary>
        /// Toggle filter focus: items NOT in <paramref name="hitNodeIds"/> get a
        /// heavy transparency override (preserving stage color); items in the hit
        /// set are restored to their stage's transparency.
        /// </summary>
        public void SetCableFilterFocus(Document doc, IEnumerable<string> hitNodeIds, double dimTransparency = 0.85)
        {
            if (_cableNodeItems.Count == 0 || _cableLastSettings == null) return;

            var hits = new HashSet<string>(hitNodeIds, StringComparer.OrdinalIgnoreCase);

            // Bucket hit items by stage so we can restore each stage's transparency.
            var hitByStage = new Dictionary<CableStage, ModelItemCollection>();
            var dimItems = new ModelItemCollection();
            foreach (var kv in _cableNodeItems)
            {
                if (hits.Contains(kv.Key))
                {
                    if (_cableNodeStages.TryGetValue(kv.Key, out var stage))
                    {
                        if (!hitByStage.TryGetValue(stage, out var bucket))
                        {
                            bucket = new ModelItemCollection();
                            hitByStage[stage] = bucket;
                        }
                        foreach (ModelItem mi in kv.Value) bucket.Add(mi);
                    }
                }
                else
                {
                    foreach (ModelItem mi in kv.Value) dimItems.Add(mi);
                }
            }

            if (dimItems.Count > 0)
                doc.Models.OverridePermanentTransparency(dimItems, dimTransparency);

            foreach (var kv in hitByStage)
            {
                if (_cableLastSettings.TryGetValue(kv.Key, out var setting))
                    doc.Models.OverridePermanentTransparency(kv.Value, setting.Transparency);
            }

            _cableFilterFocusActive = true;
        }

        public void ClearCableFilterFocus(Document doc)
        {
            if (!_cableFilterFocusActive || _cableLastSettings == null) return;
            // Re-apply each stage's setting to its cached collection — this resets
            // transparency back to stage defaults across all matched boxes.
            foreach (var kv in _cableLastSettings)
            {
                if (_cachedStageCollections.TryGetValue(kv.Key.ToString(), out var collection))
                    ApplyOverride(doc, collection, kv.Value);
            }
            _cableFilterFocusActive = false;
        }

        public ModelItemCollection GetCableNodeItems(string nodeId) =>
            _cableNodeItems.TryGetValue(nodeId, out var col) ? col : null;

        public IEnumerable<string> GetMatchedCableNodeIds() => _cableNodeItems.Keys;

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
            RestoreHiddenCableBoxes(doc);
            _cachedStageCollections.Clear();
            _cableNodeItems.Clear();
            _cableNodeStages.Clear();
            _cableLastSettings = null;
            _cableFilterFocusActive = false;
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
            // Always call — if a previous apply set transparency > 0 and the user dials it
            // back to 0, skipping here would leave the old transparency override in place.
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
