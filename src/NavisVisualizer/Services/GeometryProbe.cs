using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// COM primitive callback: collects the LINE segments emitted by
    /// InwOaFragment3.GenerateSimplePrimitives, transformed to world/model space.
    ///
    /// Confirmed on a real cable (lcldrvm_container): the geometry is Line primitives (not
    /// Triangles), i.e. the wireframe edges of the swept tube — NOT a connected centreline.
    /// Each Line callback is an INDEPENDENT segment (v1→v2); consecutive Lines do not share
    /// endpoints, so they must be kept as pairs, never chained into one polyline. There is no
    /// separate index buffer — GenerateSimplePrimitives already hands back de-indexed,
    /// explicit-vertex primitives.
    ///
    /// Vertices arrive in fragment-LOCAL space, so a per-fragment LocalToWorld matrix
    /// (translation observed as ~tens of metres) MUST be applied — set it via
    /// <see cref="SetMatrix"/> before each fragment's GenerateSimplePrimitives call.
    ///
    /// InwSimplePrimitivesCB is a COM callback interface; all four methods must be present
    /// with these exact signatures or the class won't bind to it.
    /// </summary>
    public class PrimitiveCollector : InwSimplePrimitivesCB
    {
        public int LineCount, TriangleCount, PointCount, SnapCount;

        /// <summary>World-space segments; each entry = {x1,y1,z1, x2,y2,z2}.</summary>
        public readonly List<double[]> Segments = new List<double[]>();
        public int SegmentCap = 20000;

        public int VertexArrayLen = -1;
        public int VertexLowerBound = 0;

        // Current fragment's LocalToWorld (row-major, row-vector convention:
        // world.k = Σ_j local_j * m[j*4 + k] + m[12 + k]). Null = identity.
        private double[] _m;

        public void SetMatrix(double[] m) => _m = (m != null && m.Length >= 16) ? m : null;

        public void Line(InwSimpleVertex v1, InwSimpleVertex v2)
        {
            LineCount++;
            var a = World(Vtx(v1));
            var b = World(Vtx(v2));
            if (a != null && b != null && Segments.Count < SegmentCap)
                Segments.Add(new[] { a[0], a[1], a[2], b[0], b[1], b[2] });
        }

        // A cable here is Lines; Triangles are only counted (handled separately if a future
        // cable turns out to be a mesh).
        public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3) => TriangleCount++;
        public void Point(InwSimpleVertex v1) => PointCount++;
        public void SnapPoint(InwSimpleVertex v1) => SnapCount++;

        private double[] World(double[] p)
        {
            if (p == null) return null;
            if (_m == null) return p;
            return new double[]
            {
                p[0] * _m[0] + p[1] * _m[4] + p[2] * _m[8]  + _m[12],
                p[0] * _m[1] + p[1] * _m[5] + p[2] * _m[9]  + _m[13],
                p[0] * _m[2] + p[1] * _m[6] + p[2] * _m[10] + _m[14],
            };
        }

        /// <summary>
        /// Reads InwSimpleVertex.coord defensively (SAFEARRAY of floats; base index recorded
        /// once rather than assumed). Never throws.
        /// </summary>
        private double[] Vtx(InwSimpleVertex v)
        {
            try
            {
                object boxed = v?.coord;
                if (!(boxed is Array arr) || arr.Length < 3) return null;
                if (VertexArrayLen < 0)
                {
                    VertexArrayLen = arr.Length;
                    VertexLowerBound = arr.GetLowerBound(0);
                }
                int lo = arr.GetLowerBound(0);
                return new double[]
                {
                    Convert.ToDouble(arr.GetValue(lo + 0)),
                    Convert.ToDouble(arr.GetValue(lo + 1)),
                    Convert.ToDouble(arr.GetValue(lo + 2)),
                };
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Diagnostic (Tools tab): extracts a selected item's geometry as world-space LINE
    /// segments so the cable↔clip-box clash can be verified against the real route. No model
    /// state is modified.
    ///
    /// L4/L5: fragment traversal goes through <c>dynamic</c> so a build-specific signature
    /// difference degrades to a caught runtime error, not a compile break; "dump first, then
    /// commit to logic" is how the clip path was calibrated.
    /// </summary>
    public static class GeometryProbe
    {
        public static string DumpItem(Document doc, ModelItem item, SectionService sec)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 선택 항목 형상(선분) 진단 — 월드 좌표 ===");
            if (item == null) { sb.AppendLine("항목 없음."); return sb.ToString(); }

            sb.AppendLine($"DisplayName : {item.DisplayName ?? "(none)"}");
            sb.AppendLine($"ClassName   : {item.ClassName}");
            sb.AppendLine($"HasGeometry : {item.HasGeometry}");

            try
            {
                BoundingBox3D bb = item.BoundingBox();
                if (bb != null)
                    sb.AppendLine($"BoundingBox : min({F(bb.Min)})  max({F(bb.Max)})  center({F(bb.Center)})");
            }
            catch (Exception ex) { sb.AppendLine($"BoundingBox 실패: {ex.Message}"); }
            sb.AppendLine();

            var collector = new PrimitiveCollector();
            int fragCount = 0;
            try
            {
                dynamic path = ComApiBridge.ToInwOaPath(item);
                dynamic frags = path.Fragments();
                foreach (dynamic frag in frags)
                {
                    fragCount++;
                    double[] m = ReadMatrix((object)frag);
                    collector.SetMatrix(m);
                    if (fragCount <= 3)
                        sb.AppendLine($"  frag#{fragCount} LocalToWorld: {FmtMatrix(m)}");
                    frag.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, collector);
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                sb.AppendLine($"형상 추출 EXCEPTION: {inner.GetType().Name}: {inner.Message}");
            }

            sb.AppendLine();
            sb.AppendLine($"Fragments          : {fragCount}");
            sb.AppendLine($"Line(선분)         : {collector.LineCount}  → 확보 세그먼트 {collector.Segments.Count}");
            sb.AppendLine($"Triangle           : {collector.TriangleCount}");
            sb.AppendLine($"Point / SnapPoint  : {collector.PointCount} / {collector.SnapCount}");
            sb.AppendLine($"coord 배열 len={collector.VertexArrayLen}, lowerBound={collector.VertexLowerBound}");
            sb.AppendLine();
            sb.AppendLine("※ 각 줄은 '독립 선분(pair)' — 연속 폴리라인으로 잇지 말 것 (스윕 튜브 wireframe).");
            sb.AppendLine("※ clash엔 route 복원 불필요 — 아래 각 선분을 clip 박스와 slab test하면 됨.");
            sb.AppendLine();

            List<ClipPlane> planes = null;
            try { planes = sec?.GetActiveClipPlanes(doc); }
            catch (Exception ex) { sb.AppendLine($"clip 평면 읽기 실패: {ex.Message}"); }
            sb.AppendLine($"활성 clip 평면 수   : {(planes?.Count ?? 0)} (0이면 단면 없음/미인식)");
            sb.AppendLine();

            int cap = Math.Min(40, collector.Segments.Count);
            sb.AppendLine($"[월드 좌표 선분 샘플 (최대 {cap} / 전체 {collector.Segments.Count})]");
            for (int i = 0; i < cap; i++)
            {
                var s = collector.Segments[i];
                string mid = "";
                if (planes != null && planes.Count > 0)
                {
                    var c = new Point3D((s[0] + s[3]) / 2, (s[1] + s[4]) / 2, (s[2] + s[5]) / 2);
                    mid = sec.IsPointVisible(c, planes) ? "  [중점 clip 내부]" : "  [중점 clip 외부]";
                }
                sb.AppendLine($"  seg{i,-3}: ({s[0]:0.###}, {s[1]:0.###}, {s[2]:0.###}) → ({s[3]:0.###}, {s[4]:0.###}, {s[5]:0.###}){mid}");
            }
            if (collector.Segments.Count == 0)
                sb.AppendLine("  (세그먼트 0개 — geometry 없는 노드이거나 추출 실패. leaf를 선택했는지 확인)");

            return sb.ToString();
        }

        private static string F(Point3D p) => $"{p.X:0.###}, {p.Y:0.###}, {p.Z:0.###}";

        /// <summary>InwOaFragment3.GetLocalToWorldMatrix().Matrix → double[16], or null.</summary>
        private static double[] ReadMatrix(object frag)
        {
            try
            {
                object m = frag.GetType().InvokeMember(
                    "GetLocalToWorldMatrix", BindingFlags.InvokeMethod, null, frag, null);
                if (m == null) return null;
                object arr = m.GetType().InvokeMember(
                    "Matrix", BindingFlags.GetProperty, null, m, null);
                if (!(arr is Array a)) return null;
                var outm = new double[a.Length];
                int lo = a.GetLowerBound(0);
                for (int i = 0; i < a.Length; i++) outm[i] = Convert.ToDouble(a.GetValue(lo + i));
                return outm;
            }
            catch { return null; }
        }

        private static string FmtMatrix(double[] m)
        {
            if (m == null) return "(null → identity 취급)";
            var parts = new string[m.Length];
            for (int i = 0; i < m.Length; i++) parts[i] = m[i].ToString("0.##");
            return "[" + string.Join(",", parts) + "]";
        }
    }
}
