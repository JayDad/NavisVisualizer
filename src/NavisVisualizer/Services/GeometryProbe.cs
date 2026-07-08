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
    /// COM primitive callback: collects the vertices emitted by
    /// InwOaFragment3.GenerateSimplePrimitives. A cable modelled as a route polyline emits
    /// Line primitives (~10-20 per cable); a cable modelled as a swept tube emits Triangles
    /// (hundreds+). This collector records per-type counts and a capped raw-coordinate sample
    /// so the Tools-tab probe can reveal which one a real cable is — that decides whether the
    /// clip test is segment-vs-box (cheap) or triangle-vs-box.
    ///
    /// InwSimplePrimitivesCB is a COM callback interface; all four methods must be implemented
    /// with these exact signatures or the class won't bind to it.
    /// </summary>
    public class PrimitiveCollector : InwSimplePrimitivesCB
    {
        public int LineCount, TriangleCount, PointCount, SnapCount;
        public readonly List<double[]> Sample = new List<double[]>();
        public int SampleCap = 60;

        // Discovered from the first coord array — the SAFEARRAY base index (0- vs 1-based)
        // varies by build, so we record it rather than assume [0..2].
        public int VertexArrayLen = -1;
        public int VertexLowerBound = 0;

        public void Line(InwSimpleVertex v1, InwSimpleVertex v2)
        {
            LineCount++;
            Add(Vtx(v1));
            Add(Vtx(v2));
        }

        public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3)
        {
            TriangleCount++;
            Add(Vtx(v1));
            Add(Vtx(v2));
            Add(Vtx(v3));
        }

        public void Point(InwSimpleVertex v1)
        {
            PointCount++;
            Add(Vtx(v1));
        }

        public void SnapPoint(InwSimpleVertex v1)
        {
            SnapCount++;
            Add(Vtx(v1));
        }

        private void Add(double[] c)
        {
            if (c != null && Sample.Count < SampleCap) Sample.Add(c);
        }

        /// <summary>
        /// Reads InwSimpleVertex.coord defensively. It boxes a SAFEARRAY of floats; we read
        /// every element from the array's real lower bound instead of assuming an index base.
        /// Never throws.
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
    /// Diagnostic (Tools tab): extracts the raw geometry vertices of one selected item so we
    /// can confirm — on real Windows/Navisworks — whether a cable's geometry can be read at
    /// all, what primitive type it uses (Line vs Triangle), and what coordinate space/units
    /// its vertices are in (cross-checked against BoundingBox and the active clip planes).
    ///
    /// L4/L5: the fragment traversal is done through <c>dynamic</c> so a build-specific
    /// signature difference degrades to a caught runtime error instead of a compile break,
    /// and this "dump first, then commit to logic" step is exactly how the clip-plane path
    /// was calibrated. No model state is modified.
    /// </summary>
    public static class GeometryProbe
    {
        public static string DumpItem(Document doc, ModelItem item, SectionService sec)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 선택 항목 형상(vertex) 진단 ===");
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
                    if (fragCount <= 3)
                        sb.AppendLine($"  frag#{fragCount} LocalToWorld: {DumpMatrix((object)frag)}");
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
            sb.AppendLine($"Line 프리미티브    : {collector.LineCount}");
            sb.AppendLine($"Triangle 프리미티브: {collector.TriangleCount}");
            sb.AppendLine($"Point / SnapPoint  : {collector.PointCount} / {collector.SnapCount}");
            sb.AppendLine($"coord 배열 len={collector.VertexArrayLen}, lowerBound={collector.VertexLowerBound} (0/1-based 확인)");
            sb.AppendLine();
            sb.AppendLine("→ 해석: Line 多 · Triangle 0 이면 '경로 폴리라인' → 선분 vs 박스로 바로 판정(가장 쌈).");
            sb.AppendLine("        Triangle 多 이면 '스윕 튜브 메시' → 삼각형 vs 박스(또는 중심선 복원 검토).");
            sb.AppendLine();

            // Cross-check the raw coordinates against the live clip volume: if the numbers make
            // sense against BoundingBox and some samples fall inside the section, the coordinate
            // space matches and the production test can reuse SectionService directly.
            List<ClipPlane> planes = null;
            try { planes = sec?.GetActiveClipPlanes(doc); }
            catch (Exception ex) { sb.AppendLine($"clip 평면 읽기 실패: {ex.Message}"); }

            sb.AppendLine($"활성 clip 평면 수   : {(planes?.Count ?? 0)} (0이면 단면 없음/미인식 — 단면 켠 뒤 다시 덤프)");
            sb.AppendLine($"[원시 vertex 샘플 (최대 {collector.SampleCap}개 — 좌표 공간·단위·단면 내부 여부)]");
            int idx = 0;
            foreach (var c in collector.Sample)
            {
                string inside = "";
                if (planes != null && planes.Count > 0 && c.Length >= 3)
                    inside = sec.IsPointVisible(new Point3D(c[0], c[1], c[2]), planes)
                        ? "  [clip 내부]" : "  [clip 외부]";
                sb.AppendLine($"  v{idx++,-3}: ({c[0]:0.###}, {c[1]:0.###}, {c[2]:0.###}){inside}");
            }
            if (collector.Sample.Count == 0)
                sb.AppendLine("  (샘플 0개 — 형상 추출 실패 또는 이 노드에 geometry 없음. 자식 leaf를 선택했는지 확인)");

            return sb.ToString();
        }

        private static string F(Point3D p) => $"{p.X:0.###}, {p.Y:0.###}, {p.Z:0.###}";

        /// <summary>Reflection dump of InwOaFragment3.GetLocalToWorldMatrix() — never breaks compile.</summary>
        private static string DumpMatrix(object frag)
        {
            try
            {
                object m = frag.GetType().InvokeMember(
                    "GetLocalToWorldMatrix", BindingFlags.InvokeMethod, null, frag, null);
                if (m == null) return "(null)";
                object arr = m.GetType().InvokeMember(
                    "Matrix", BindingFlags.GetProperty, null, m, null);
                if (arr is Array a)
                {
                    var parts = new List<string>();
                    foreach (var e in a) parts.Add(Convert.ToDouble(e).ToString("0.##"));
                    return "[" + string.Join(",", parts) + "]";
                }
                return m.GetType().Name;
            }
            catch (Exception ex) { return "(matrix 실패: " + ex.Message + ")"; }
        }
    }
}
