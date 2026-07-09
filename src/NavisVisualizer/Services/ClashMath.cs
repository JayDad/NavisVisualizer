using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// 선분-vs-볼륨(반평면 집합) 교차 술어. 볼륨은 <see cref="SectionService.GetActiveClipPlanes"/>가
    /// 주는 <see cref="ClipPlane"/> 리스트로, 축정렬 박스(6반평면)와 Planes 모드를 한 구현으로 흡수한다.
    /// <paramref name="keepPositive"/>는 <see cref="SectionService.KeepPositiveSide"/>와 일치시켜 부호
    /// knob을 단일화한다. 결과는 캐시하지 않는다(L2) — clip 상태는 라이브.
    ///
    /// 순수 산술이라 Autodesk 의존은 Point3D(값 계산)뿐 — GeometryProbe 진단과 CableClashService가 공유.
    /// </summary>
    public static class ClashMath
    {
        private const double Eps = 1e-6;

        /// <summary>
        /// 선분 {x1,y1,z1,x2,y2,z2}이 모든 반평면의 keep쪽 교집합(볼록 영역)을 통과(교차)하는가.
        /// Cyrus–Beck: 각 반평면으로 파라미터 t∈[0,1]를 클리핑, 살아남는 t구간이 있으면 통과.
        /// planes가 비면(단면 없음) 항상 true.
        /// </summary>
        public static bool SegmentInsideVolume(double[] seg, IList<ClipPlane> planes, bool keepPositive)
        {
            if (seg == null || seg.Length < 6) return false;
            if (planes == null || planes.Count == 0) return true;

            double s = keepPositive ? 1.0 : -1.0;
            double tEnter = 0.0, tExit = 1.0;
            var p0 = new Point3D(seg[0], seg[1], seg[2]);
            var p1 = new Point3D(seg[3], seg[4], seg[5]);

            for (int i = 0; i < planes.Count; i++)
            {
                double g0 = s * planes[i].Eval(p0);
                double g1 = s * planes[i].Eval(p1);
                double denom = g1 - g0;
                if (Math.Abs(denom) < Eps)
                {
                    // Segment parallel to this plane — if its start is outside the kept
                    // half-space, no point of the segment can be inside it.
                    if (g0 < -Eps) return false;
                }
                else
                {
                    double t = -g0 / denom;                 // g(t) = 0 crossing
                    if (denom > 0) { if (t > tEnter) tEnter = t; }  // entering kept side
                    else           { if (t < tExit)  tExit  = t; }  // leaving kept side
                    if (tEnter > tExit + Eps) return false;
                }
            }
            return tEnter <= tExit + Eps;
        }

        /// <summary>
        /// AABB pre-cull: 축정렬 경계상자(min/max)가 볼륨과 겹칠 여지가 전혀 없으면 true(배제 가능).
        /// 각 반평면에 대해 상자의 keep쪽 최대값이 음수면(상자 전체가 그 반평면 밖) 통과 불가.
        /// aabb = {minX,minY,minZ, maxX,maxY,maxZ}. planes가 비면 false(배제 못 함).
        /// </summary>
        public static bool AabbOutside(double[] aabb, IList<ClipPlane> planes, bool keepPositive)
        {
            if (aabb == null || aabb.Length < 6 || planes == null || planes.Count == 0) return false;
            double s = keepPositive ? 1.0 : -1.0;
            foreach (var pl in planes)
            {
                double a = s * pl.A, b = s * pl.B, c = s * pl.C, d = s * pl.D;
                double gMax = (a >= 0 ? a * aabb[3] : a * aabb[0])
                            + (b >= 0 ? b * aabb[4] : b * aabb[1])
                            + (c >= 0 ? c * aabb[5] : c * aabb[2])
                            + d;
                if (gMax < -Eps) return true; // whole box outside this half-space → cull
            }
            return false;
        }

        /// <summary>선분 집합의 축정렬 경계상자 {minX,minY,minZ,maxX,maxY,maxZ}. 비면 null.</summary>
        public static double[] SegmentsAabb(IList<double[]> segments)
        {
            if (segments == null || segments.Count == 0) return null;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;
            foreach (var s in segments)
            {
                if (s == null || s.Length < 6) continue;
                any = true;
                for (int k = 0; k < 2; k++)
                {
                    double x = s[k * 3 + 0], y = s[k * 3 + 1], z = s[k * 3 + 2];
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
            }
            return any ? new[] { minX, minY, minZ, maxX, maxY, maxZ } : null;
        }
    }
}
