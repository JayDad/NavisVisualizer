using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace NavisVisualizer.Visualizers
{
    /// <summary>색상 오버라이드를 소유하는 공종(탭) 식별자 — stage 캐시의 1차 키.</summary>
    public enum VisualModule
    {
        Hydrotest,
        Spool,
        Equipment,
        EitTray,
        CableLine,    // Cable(형상) 탭 (구 노드/박스 탭 Cable은 2026-07 삭제)
        SubSystem,
    }

    public class ColorOverrideEngine
    {
        // Tag 인덱스는 NWD 파일 스코프별로 분리 (MainDockablePanel 참조):
        //   spool (SPL → 없으면 HYDROPKG 체인) / hydro (HYDROPKG) / elec (EIT) /
        //   subSystem (MEQ·SPL·HYDROPKG). 매칭 전략은 전부 digit full-walk로 동일.
        // Equipment uses its own level-targeted index.
        private readonly ModelItemSearcher _spoolTagSearcher;
        private readonly ModelItemSearcher _hydroTagSearcher;
        private readonly ModelItemSearcher _elecTagSearcher;
        private readonly ModelItemSearcher _equipmentSearcher;
        // Cable(형상) 탭 — cable-no를 컴포넌트에 직접 매칭 (레벨 타겟, 스코프 CABLE).
        private readonly ModelItemSearcher _cableLineSearcher;
        // Sub-system 탭의 공종별 매칭 searcher는 탭이 소유한다(엔진 비의존) — ApplySubSystem에
        // 공종→searcher 리졸버를 주입받는다. 공종마다 자기 nwd 하나만 레벨 타겟(§11).

        // Stage 컬렉션 캐시를 모듈별로 격리한다. 단일 캐시에 enum.ToString() 키로
        // 넣으면 "NotStarted"(전 모듈), "Setting"(Spool/Equipment) 등이 충돌해
        // 한 탭의 증분 색 변경이 다른 탭이 칠한 컬렉션을 덧칠하는 간섭이 생긴다.
        private readonly Dictionary<VisualModule, Dictionary<string, ModelItemCollection>> _stageCollectionsByModule
            = new Dictionary<VisualModule, Dictionary<string, ModelItemCollection>>();

        // 모듈이 지금까지 칠한 아이템의 누적 합집합. 재적용 전에 이것만 리셋하면 (최신 캐시가
        // 아니라) 이전 적용들의 잔존까지 정확히 원복된다 — 다른 공종 색은 유지. (CLAUDE.md §10)
        private readonly Dictionary<VisualModule, ModelItemCollection> _paintedByModule
            = new Dictionary<VisualModule, ModelItemCollection>();

        // Cable(형상) 탭 캐시 — cableNo 단위. 그룹 키(stage명 또는 "__highlight")로 색을 묶는다.
        private Dictionary<string, ModelItemCollection> _cableLineItems
            = new Dictionary<string, ModelItemCollection>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _cableLineGroupOfCable
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ColorSetting> _cableLineGroupSettings
            = new Dictionary<string, ColorSetting>();
        private bool _cableLineFilterFocusActive;

        public ColorOverrideEngine(
            ModelItemSearcher spoolTagSearcher,
            ModelItemSearcher hydroTagSearcher,
            ModelItemSearcher elecTagSearcher,
            ModelItemSearcher equipmentSearcher,
            ModelItemSearcher cableLineSearcher)
        {
            _spoolTagSearcher = spoolTagSearcher;
            _hydroTagSearcher = hydroTagSearcher;
            _elecTagSearcher = elecTagSearcher;
            _equipmentSearcher = equipmentSearcher;
            _cableLineSearcher = cableLineSearcher;
        }

        private Dictionary<string, ModelItemCollection> ModuleCache(VisualModule module)
        {
            if (!_stageCollectionsByModule.TryGetValue(module, out var cache))
            {
                cache = new Dictionary<string, ModelItemCollection>();
                _stageCollectionsByModule[module] = cache;
            }
            return cache;
        }

        public OverrideResult ApplyHydrotest(
            Document doc,
            List<TestPackageData> packages,
            Dictionary<HydrotestStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
            var result = new OverrideResult();
            var stageItems = new Dictionary<HydrotestStage, List<ModelItem>>();

            var allPkgIds = packages.Select(p => p.TestPkgId).Distinct();
            var searchResult = _hydroTagSearcher.FindBySpoolIds(allPkgIds);

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

            // 이전 적용 누적을 먼저 리셋(다른 공종 유지) — 재적용 성능 저하·체크해제 잔존 방지.
            ResetModule(doc, VisualModule.Hydrotest);
            var cache = ModuleCache(VisualModule.Hydrotest);

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                cache[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.Hydrotest, collection);
                    paintedItems += collection.Count;
                }
            }

            PerfLog.Record("가시화 적용(Hydrotest)", sw.ElapsedMilliseconds, rows: packages.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}");
            return result;
        }

        public OverrideResult ApplySpool(
            Document doc,
            List<SpoolData> spools,
            Dictionary<SpoolStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
            var result = new OverrideResult();
            var stageItems = new Dictionary<SpoolStage, List<ModelItem>>();

            var allSpoolIds = spools.Select(s => s.SpoolId).Distinct();
            var searchResult = _spoolTagSearcher.FindBySpoolIds(allSpoolIds);

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

            // 이전 적용에서 칠한 누적 집합을 먼저 리셋 — 잔존(체크 해제 단계 등) 제거 +
            // override 누적(투명 재처리)으로 인한 재적용 성능 저하 방지. 다른 공종 색은 유지.
            ResetModule(doc, VisualModule.Spool);
            var cache = ModuleCache(VisualModule.Spool);

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                cache[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.Spool, collection);
                    paintedItems += collection.Count;
                }
            }

            PerfLog.Record("가시화 적용(Spool)", sw.ElapsedMilliseconds, rows: spools.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}");
            return result;
        }

        public OverrideResult ApplyEquipment(
            Document doc,
            List<EquipmentData> equipments,
            Dictionary<EquipmentStage, ColorSetting> colorSettings,
            DateTime referenceDate)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
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
            // 이전 적용 누적을 먼저 리셋(다른 공종 유지) — 재적용 성능 저하·체크해제 잔존 방지.
            ResetModule(doc, VisualModule.Equipment);
            var cache = ModuleCache(VisualModule.Equipment);

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                cache[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.Equipment, collection);
                    paintedItems += collection.Count;
                }
            }

            PerfLog.Record("가시화 적용(Equipment)", sw.ElapsedMilliseconds, rows: equipments.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}");
            return result;
        }

        public OverrideResult ApplyEit(
            Document doc,
            List<EitTrayData> trays,
            Dictionary<EitStage, ColorSetting> colorSettings)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
            var result = new OverrideResult();
            var stageItems = new Dictionary<EitStage, List<ModelItem>>();

            // Model indexes strip leading '/' — match Excel tray numbers accordingly
            var normalizedIds = trays.Select(t => EitTrayData.NormalizeId(t.TrayNumber)).Distinct();
            var searchResult = _elecTagSearcher.FindBySpoolIds(normalizedIds);

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

            // §10: 이전 적용 누적을 먼저 리셋(다른 공종 유지) — 재적용 투명 누적·체크해제 잔존 방지.
            // cache.Clear()도 유지 — IncrementalUpdate가 깨끗한 per-stage 캐시에 의존.
            ResetModule(doc, VisualModule.EitTray);
            var cache = ModuleCache(VisualModule.EitTray);

            foreach (var kv in stageItems)
            {
                string key = kv.Key.ToString();
                var collection = ToCollection(kv.Value);
                cache[key] = collection;

                if (colorSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.EitTray, collection);
                    paintedItems += collection.Count;
                }
            }

            PerfLog.Record("가시화 적용(EIT Tray)", sw.ElapsedMilliseconds, rows: trays.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}");
            return result;
        }

        /// <summary>
        /// Sub-system 탭: 요소를 groupSelector가 주는 키(모드에 따라 sub-system 이름
        /// 또는 ProgressStatus 이름)로 묶어 그룹당 1회 색상을 적용한다. 매칭은
        /// 공종별 스코프가 달라 인덱스를 라우팅한다 — 탭이 <paramref name="searcherFor"/>로
        /// 공종→searcher를 주입한다(각 공종이 자기 nwd 하나만 레벨 타겟: Equipment=MEQ /
        /// Piping=HYDROPKG / EIT EQ=EIT / Cable=CABLE. 인덱스 빌드는 SubSystemTab.BuildIndex 책임).
        /// groupSelector가 null을 반환하거나 groupSettings에 없는 키는 색칠하지
        /// 않는다(체크 해제된 단계). 캐시 키는 그룹 키 그대로라 진행 상태 모드에서는
        /// UpdateStageColor(VisualModule.SubSystem, status명)로 증분 색 변경이 된다.
        /// 색칠 전 ResetModule로 직전 적용 누적분을 원복(§10 — 선택 축소 시 잔존 방지).
        /// </summary>
        public OverrideResult ApplySubSystem(
            Document doc,
            List<SubSystemElement> elements,
            Func<SubSystemElement, string> groupSelector,
            Dictionary<string, ColorSetting> groupSettings,
            Func<SubSystemDiscipline, ModelItemSearcher> searcherFor)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
            var result = new OverrideResult();
            var groupItems = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);

            // 공종(searcher)별로 id를 모아 한 번씩 조회 후, 요소 순회 시 자기 결과에서 찾는다.
            var resultsBySearcher = new Dictionary<ModelItemSearcher, Dictionary<string, List<ModelItem>>>();
            foreach (var group in elements.GroupBy(el => searcherFor(el.Discipline)))
                resultsBySearcher[group.Key] =
                    group.Key.FindBySpoolIds(group.Select(el => el.ElementId).Distinct());

            foreach (var el in elements)
            {
                var searchResult = resultsBySearcher[searcherFor(el.Discipline)];
                if (!searchResult.TryGetValue(el.ElementId, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(el.ElementId);
                    continue;
                }
                result.MatchedCount++;

                string key = groupSelector(el);
                if (key == null || !groupSettings.ContainsKey(key)) continue;

                if (!groupItems.TryGetValue(key, out var list))
                {
                    list = new List<ModelItem>();
                    groupItems[key] = list;
                }
                list.AddRange(items);
            }

            ResetModule(doc, VisualModule.SubSystem);
            var cache = ModuleCache(VisualModule.SubSystem);
            cache.Clear();

            foreach (var kv in groupItems)
            {
                var collection = ToCollection(kv.Value);
                cache[kv.Key] = collection;

                if (groupSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.SubSystem, collection);
                    paintedItems += collection.Count;
                }
            }

            PerfLog.Record("가시화 적용(Sub-system)", sw.ElapsedMilliseconds, rows: elements.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}");
            return result;
        }

        // ============================================================
        // Cable(형상) 탭 — cable-no를 컴포넌트에 직접 매칭·색칠 (VisualModule.CableLine)
        // ============================================================

        /// <summary>하이라이트 우선 모드의 단일 그룹 키 (stage명과 충돌하지 않게 언더스코어 접두).</summary>
        public const string CableLineHighlightGroup = "__highlight";

        /// <summary>
        /// Cable(형상): cable-no를 컴포넌트에 매칭해 stage색(또는 하이라이트 단색)으로 칠한다.
        /// highlightOverride가 null이 아니면(맨 목록·stage 날짜 전무) 모든 매칭 케이블을 그 단색으로
        /// 칠하고, null이면 GetStageAtDate 결과로 그룹핑한다. 체크 해제된 단계(colorSettings에 없음)는
        /// 색칠하지 않지만 _cableLineItems엔 남겨(선택/clash 대상). §10 ResetModule를 처음부터 채택.
        /// </summary>
        public OverrideResult ApplyCableLines(
            Document doc,
            List<CableLineData> cables,
            Dictionary<CableLineStage, ColorSetting> colorSettings,
            DateTime referenceDate,
            ColorSetting highlightOverride)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int paintedItems = 0;
            var result = new OverrideResult();
            var groupItems = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            var groupSettings = new Dictionary<string, ColorSetting>();

            var normalizedIds = cables.Select(c => CableLineData.NormalizeCableNo(c.CableNo)).Distinct();
            var searchResult = _cableLineSearcher.FindBySpoolIds(normalizedIds);

            _cableLineItems.Clear();
            _cableLineGroupOfCable.Clear();

            foreach (var cable in cables)
            {
                string key = CableLineData.NormalizeCableNo(cable.CableNo);
                if (!searchResult.TryGetValue(key, out var items) || items.Count == 0)
                {
                    result.UnmatchedIds.Add(cable.CableNo);
                    continue;
                }
                result.MatchedCount++;

                var col = new ModelItemCollection();
                col.AddRange(items);
                _cableLineItems[cable.CableNo] = col;   // 선택/clash용 — 체크 여부와 무관

                string groupKey;
                ColorSetting setting;
                if (highlightOverride != null)
                {
                    groupKey = CableLineHighlightGroup;
                    setting = highlightOverride;
                }
                else
                {
                    var stage = cable.GetStageAtDate(referenceDate);
                    groupKey = stage.ToString();
                    if (!colorSettings.TryGetValue(stage, out setting))
                        continue;   // 체크 해제된 단계 — 색칠 안 함
                }

                _cableLineGroupOfCable[cable.CableNo] = groupKey;
                groupSettings[groupKey] = setting;
                if (!groupItems.TryGetValue(groupKey, out var list))
                {
                    list = new List<ModelItem>();
                    groupItems[groupKey] = list;
                }
                list.AddRange(items);
            }

            // §10: 재적용 전 이 모듈 누적 리셋(다른 공종 유지). focus는 override라 색칠 전 별도 해제(탭에서).
            ResetModule(doc, VisualModule.CableLine);
            var cache = ModuleCache(VisualModule.CableLine);

            foreach (var kv in groupItems)
            {
                var collection = ToCollection(kv.Value);
                cache[kv.Key] = collection;
                if (groupSettings.TryGetValue(kv.Key, out var setting))
                {
                    ApplyOverride(doc, collection, setting);
                    AccumulatePainted(VisualModule.CableLine, collection);
                    paintedItems += collection.Count;
                }
            }

            _cableLineGroupSettings = groupSettings;
            _cableLineFilterFocusActive = false;
            PerfLog.Record("가시화 적용(Cable)", sw.ElapsedMilliseconds, rows: cables.Count,
                items: paintedItems, note: $"매칭 {result.MatchedCount} · 미매칭 {result.UnmatchedIds.Count}"
                    + (highlightOverride != null ? " · 하이라이트" : ""));
            return result;
        }

        /// <summary>필터 포커스: hit 케이블 외 전부 투명 dim, hit은 그룹 투명도 유지 (검색 히트 강조용).</summary>
        public void SetCableLineFilterFocus(Document doc, IEnumerable<string> hitCableNos, double dimTransparency = 0.85)
        {
            if (_cableLineItems.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hits = new HashSet<string>(hitCableNos, StringComparer.OrdinalIgnoreCase);

            var dimItems = new ModelItemCollection();
            var restoreByGroup = new Dictionary<string, ModelItemCollection>();
            foreach (var kv in _cableLineItems)
            {
                if (hits.Contains(kv.Key) && _cableLineGroupOfCable.TryGetValue(kv.Key, out var g))
                {
                    if (!restoreByGroup.TryGetValue(g, out var bucket))
                    {
                        bucket = new ModelItemCollection();
                        restoreByGroup[g] = bucket;
                    }
                    foreach (ModelItem mi in kv.Value) bucket.Add(mi);
                }
                else
                {
                    foreach (ModelItem mi in kv.Value) dimItems.Add(mi);
                }
            }

            if (dimItems.Count > 0)
                doc.Models.OverridePermanentTransparency(dimItems, dimTransparency);
            foreach (var kv in restoreByGroup)
                if (_cableLineGroupSettings.TryGetValue(kv.Key, out var setting))
                    doc.Models.OverridePermanentTransparency(kv.Value, setting.Transparency);

            _cableLineFilterFocusActive = true;
            PerfLog.Record("Cable 필터 포커스", sw.ElapsedMilliseconds,
                items: dimItems.Count, note: $"hit {hits.Count}");
        }

        public void ClearCableLineFilterFocus(Document doc)
        {
            if (!_cableLineFilterFocusActive) return;
            var cache = ModuleCache(VisualModule.CableLine);
            // 색칠된 그룹은 그룹 색/투명도로 복원.
            foreach (var kv in _cableLineGroupSettings)
                if (cache.TryGetValue(kv.Key, out var collection))
                    ApplyOverride(doc, collection, kv.Value);
            // 체크 해제 단계(색칠 안 된) 케이블은 dim만 걷어낸다 — 케이블 형상은 CableLine만 칠하므로 안전.
            var ungrouped = new ModelItemCollection();
            foreach (var kv in _cableLineItems)
                if (!_cableLineGroupOfCable.ContainsKey(kv.Key))
                    foreach (ModelItem mi in kv.Value) ungrouped.Add(mi);
            if (ungrouped.Count > 0)
                doc.Models.ResetPermanentMaterials(ungrouped);
            _cableLineFilterFocusActive = false;
        }

        public ModelItemCollection GetCableLineItems(string cableNo) =>
            _cableLineItems.TryGetValue(cableNo, out var col) ? col : null;

        public IEnumerable<string> GetMatchedCableLineNos() => _cableLineItems.Keys;

        /// <summary>Cable(형상) 공종 초기화: focus 해제 → 이 모듈 색 리셋 → 캐시 clear. isolate 숨김은 탭이 복원.</summary>
        public void ResetCableLineModule(Document doc)
        {
            ClearCableLineFilterFocus(doc);
            ResetModule(doc, VisualModule.CableLine);
            _cableLineItems.Clear();
            _cableLineGroupOfCable.Clear();
            _cableLineGroupSettings = new Dictionary<string, ColorSetting>();
        }

        /// <summary>모듈 자기 캐시의 stage 컬렉션에만 색/투명도를 재적용한다.</summary>
        public bool UpdateStageColor(Document doc, VisualModule module, string stageKey, ColorSetting setting)
        {
            if (!_stageCollectionsByModule.TryGetValue(module, out var cache))
                return false;
            if (!cache.TryGetValue(stageKey, out var collection))
                return false;
            ApplyOverride(doc, collection, setting);
            return true;
        }

        public bool HasCachedData(VisualModule module) =>
            _stageCollectionsByModule.TryGetValue(module, out var cache) && cache.Count > 0;

        /// <summary>
        /// 한 공종(모듈)이 지금까지 칠한 누적 아이템의 permanent material만 리셋한다.
        /// ResetAllPermanentMaterials와 달리 다른 공종 색은 건드리지 않는다.
        /// 재적용 시작 시 호출 → 이전 적용의 잔존(체크 해제된 단계·기준일 변경으로 빠진 스풀 등)
        /// 까지 정확히 원복하고, 이어서 현재 활성 집합만 다시 칠한다. (CLAUDE.md §10)
        /// </summary>
        public void ResetModule(Document doc, VisualModule module)
        {
            if (_paintedByModule.TryGetValue(module, out var painted) && painted.Count > 0)
                doc.Models.ResetPermanentMaterials(painted);
            _paintedByModule.Remove(module);
            if (_stageCollectionsByModule.TryGetValue(module, out var cache))
                cache.Clear();
        }

        /// <summary>ApplyOverride로 칠한 컬렉션을 모듈 누적 painted 셋에 합친다.</summary>
        private void AccumulatePainted(VisualModule module, ModelItemCollection collection)
        {
            if (collection == null || collection.Count == 0) return;
            if (!_paintedByModule.TryGetValue(module, out var painted))
            {
                painted = new ModelItemCollection();
                _paintedByModule[module] = painted;
            }
            painted.AddRange(collection);
        }

        public void Reset(Document doc)
        {
            doc.Models.ResetAllPermanentMaterials();
            _stageCollectionsByModule.Clear();
            _paintedByModule.Clear();
            _cableLineItems.Clear();
            _cableLineGroupOfCable.Clear();
            _cableLineGroupSettings = new Dictionary<string, ColorSetting>();
            _cableLineFilterFocusActive = false;
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
