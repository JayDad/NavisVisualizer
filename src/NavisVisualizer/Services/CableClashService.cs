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
        /// <summary>COM 추출 없이 관리형 BoundingBox만으로 배제한 수 — 이 값이 커야 정상
        /// (2만 케이블 중 볼륨 근처만 추출돼야 함). 작으면 pre-cull이 안 먹는 것.</summary>
        public int LastPreCulled { get; private set; }

        // 이번 배치에서 통과 판정된 케이블의 "첫 통과 세그먼트 중점" — 오탐 진단용.
        // 이 좌표가 볼륨 안이면 정상 관통(케이블이 길어 먼 곳까지 뻗은 것), 볼륨에서 멀면
        // 술어/좌표계 버그 또는 인덱스 키 충돌(남의 아이템이 케이블에 붙음) 의심.
        private readonly Dictionary<string, double[]> _lastHits =
            new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, double[]> LastHits => _lastHits;

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

        public void ResetBatchCounters()
        {
            LastExtracted = 0; LastCulled = 0; LastPreCulled = 0;
            _lastHits.Clear();
        }

        /// <summary>
        /// cableNo의 형상(items에서 추출·캐시)이 planes 볼륨을 통과하는가. planes가 비면 true.
        /// 판정 순서(§12 D — 병목은 COM 추출이므로 추출을 최후로 미룬다):
        /// ① 미캐시 케이블은 관리형 <c>BoundingBox()</c>(Navisworks 사전 계산값, COM 왕복 없음)로
        ///    먼저 배제 — 볼륨 밖 케이블은 추출 자체를 안 한다. 2만 케이블 중 실제 추출은
        ///    볼륨과 AABB가 겹치는 후보뿐. (초기 구현은 추출 후 세그먼트 AABB로 배제해서
        ///    첫 배치에 전 케이블 COM 추출이 일어났다 — 그게 "엄청 느림"의 원인.)
        /// ② 살아남은 후보만 세그먼트 추출·캐시 → 세그먼트 AABB 재확인 → 세그먼트별
        ///    Cyrus–Beck, 하나라도 통과하면 true.
        /// 관리형 bbox는 실형상보다 크거나 같아(보수적) 배제에만 써도 놓침이 없다.
        /// 세그먼트가 0개면(geometry 추출 실패) false — 조용히 통과로 찍지 않는다.
        /// </summary>
        public bool PassesVolume(string cableNo, IList<ModelItem> items,
            IList<ClipPlane> planes, bool keepPositive)
        {
            if (planes == null || planes.Count == 0) return true;

            string key = cableNo ?? "";
            if (!_cache.TryGetValue(key, out var cached))
            {
                var bb = ItemsBoundingAabb(items);
                if (bb != null && ClashMath.AabbOutside(bb, planes, keepPositive))
                {
                    LastPreCulled++;
                    return false;   // 캐시 안 함 — 다른 볼륨에서 후보가 되면 그때 추출
                }
                cached = GetOrExtract(key, items);
            }
            if (cached == null || cached.Segments.Count == 0) return false;

            if (ClashMath.AabbOutside(cached.Aabb, planes, keepPositive))
            {
                LastCulled++;
                return false;
            }
            foreach (var seg in cached.Segments)
                if (ClashMath.SegmentInsideVolume(seg, planes, keepPositive))
                {
                    _lastHits[key] = new[]
                    {
                        (seg[0] + seg[3]) / 2.0, (seg[1] + seg[4]) / 2.0, (seg[2] + seg[5]) / 2.0,
                    };
                    return true;
                }
            return false;
        }

        /// <summary>items의 관리형 BoundingBox 합집합 {minX..maxZ}. 없으면 null(→ 추출 경로로).</summary>
        private static double[] ItemsBoundingAabb(IList<ModelItem> items)
        {
            if (items == null || items.Count == 0) return null;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;
            foreach (var item in items)
            {
                if (item == null) continue;
                try
                {
                    var bb = item.BoundingBox();
                    if (bb == null || bb.IsEmpty) continue;
                    if (bb.Min.X < minX) minX = bb.Min.X;
                    if (bb.Min.Y < minY) minY = bb.Min.Y;
                    if (bb.Min.Z < minZ) minZ = bb.Min.Z;
                    if (bb.Max.X > maxX) maxX = bb.Max.X;
                    if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                    if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
                    any = true;
                }
                catch
                {
                    // bbox를 못 읽는 아이템이 하나라도 있으면 보수적으로 pre-cull 포기
                    return null;
                }
            }
            return any ? new[] { minX, minY, minZ, maxX, maxY, maxZ } : null;
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
