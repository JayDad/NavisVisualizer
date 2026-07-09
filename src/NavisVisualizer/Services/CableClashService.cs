using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// "이 단면(clipping 볼륨)을 지나가는 케이블" 판정 (§12/§13). 케이블 형상은 모델당 정적이므로
    /// world 세그먼트를 케이블당 1회 추출·캐시하고(형상만 캐시), 볼륨 술어(Cyrus–Beck)는 매 호출마다
    /// 현재 clip 평면으로 재계산한다(L2 — 라이브 판정 결과는 캐시 금지). 캐시 무효화는 모델 변경
    /// (doc-id) 시에만 — Excel/OASIS 재로드로는 지우지 않는다(형상 불변인데 비싼 COM 추출 낭비).
    ///
    /// 병목은 산술이 아니라 COM 추출이므로 ① AABB pre-cull ② 리스트에 있는(=매칭된) 케이블만 lazy
    /// 추출 ③ 모델당 1회 캐시로 완화. COM은 STA/UI 스레드라 백그라운드 Task 금지 — 호출부가
    /// marquee 진행바로 감싼다.
    /// </summary>
    public class CableClashService
    {
        private class Cached
        {
            public List<double[]> Segments;
            public double[] Aabb; // {minX,minY,minZ,maxX,maxY,maxZ}
        }

        private readonly Dictionary<string, Cached> _cache =
            new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private string _cacheDocId;

        /// <summary>이번 배치에서 새로 COM 추출한 케이블 수 / AABB로 즉시 배제한 수 (진단).</summary>
        public int LastExtracted { get; private set; }
        public int LastCulled { get; private set; }

        /// <summary>모델이 바뀌면 세그먼트 캐시를 버린다. 판정 배치 시작 전에 호출.</summary>
        public void EnsureFresh(Document doc)
        {
            string id = DocId(doc);
            if (id != _cacheDocId)
            {
                _cache.Clear();
                _cacheDocId = id;
            }
        }

        public void ResetBatchCounters() { LastExtracted = 0; LastCulled = 0; }

        /// <summary>
        /// cableNo의 형상(items에서 추출·캐시)이 planes 볼륨을 통과하는가. planes가 비면 true.
        /// AABB pre-cull → 세그먼트별 Cyrus–Beck, 하나라도 통과하면 true. 세그먼트가 0개면
        /// (geometry 추출 실패) false — 조용히 통과로 찍지 않는다.
        /// </summary>
        public bool PassesVolume(string cableNo, IList<ModelItem> items,
            IList<ClipPlane> planes, bool keepPositive)
        {
            if (planes == null || planes.Count == 0) return true;

            var cached = GetOrExtract(cableNo, items);
            if (cached == null || cached.Segments.Count == 0) return false;

            if (ClashMath.AabbOutside(cached.Aabb, planes, keepPositive))
            {
                LastCulled++;
                return false;
            }
            foreach (var seg in cached.Segments)
                if (ClashMath.SegmentInsideVolume(seg, planes, keepPositive))
                    return true;
            return false;
        }

        private Cached GetOrExtract(string cableNo, IList<ModelItem> items)
        {
            string key = cableNo ?? "";
            if (_cache.TryGetValue(key, out var c)) return c;

            var segs = new List<double[]>();
            if (items != null)
                foreach (var item in items)
                    if (item != null)
                        segs.AddRange(GeometryProbe.ExtractWorldSegments(item));

            c = new Cached { Segments = segs, Aabb = ClashMath.SegmentsAabb(segs) };
            _cache[key] = c;
            LastExtracted++;
            return c;
        }

        private static string DocId(Document doc)
        {
            try { return $"{doc?.FileName}|{doc?.Models.Count}"; }
            catch { return Guid.NewGuid().ToString(); }
        }
    }
}
