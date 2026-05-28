using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// One active clipping plane expressed as the plane equation A*x + B*y + C*z + D = 0.
    /// The "kept" (visible) side is decided by <see cref="SectionService.KeepPositiveSide"/>.
    /// </summary>
    public struct ClipPlane
    {
        public double A, B, C, D;
        public double Eval(Point3D p) => A * p.X + B * p.Y + C * p.Z + D;
    }

    /// <summary>
    /// Reads the active view's section (clip) planes through the COM API and decides
    /// whether a point lies in the visible region. Navisworks' managed API does not
    /// expose section planes, so this mirrors the late-bound IDispatch pattern already
    /// used in <see cref="UserDataService"/>.
    ///
    /// IMPORTANT (Windows calibration): the plane representation (data1..data4) and the
    /// sign of the kept side are confirmed on a real Navisworks build via the Tools tab
    /// "Clip Plane 덤프". If sectioning appears inverted, flip <see cref="KeepPositiveSide"/>.
    /// Coordinate-unit mismatches between COM planes and ModelItem.BoundingBox() (if any)
    /// also surface through the dump.
    /// </summary>
    public class SectionService
    {
        private const double Epsilon = 1e-6;

        /// <summary>If true, a point is kept when A*x+B*y+C*z+D &gt;= 0. Flip if inverted.</summary>
        public bool KeepPositiveSide { get; set; } = true;

        /// <summary>
        /// Returns the enabled clipping planes of the current view. Empty list means
        /// "no active section" — callers treat every point as visible in that case.
        /// Never throws; returns empty on any COM failure.
        /// </summary>
        public List<ClipPlane> GetActiveClipPlanes(Document doc)
        {
            var planes = new List<ClipPlane>();
            if (doc == null) return planes;

            try
            {
                var bf = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance;
                object comState = (object)ComApiBridge.State;

                object view = Get(comState, "CurrentView");
                if (view == null) return planes;

                object clipColl = view.GetType().InvokeMember("ClippingPlanes", bf, null, view, null);
                if (clipColl == null) return planes;

                int count = Convert.ToInt32(Get(clipColl, "Count"));
                for (int i = 1; i <= count; i++) // COM collections are 1-based
                {
                    object plane = clipColl.GetType().InvokeMember(
                        "Item", bf, null, clipColl, new object[] { i });
                    if (plane == null) continue;

                    bool enabled = Convert.ToBoolean(Get(plane, "Enabled"));
                    if (!enabled) continue;

                    object lp = Get(plane, "Plane"); // InwLPlane3f
                    if (lp == null) continue;

                    if (TryReadPlane(lp, out var cp))
                        planes.Add(cp);
                }
            }
            catch
            {
                // Section reading is best-effort; on failure callers fall back to
                // Hide-only visibility (every point counted as inside-section).
                return new List<ClipPlane>();
            }

            return planes;
        }

        /// <summary>True if the point is inside every active plane's kept half-space.</summary>
        public bool IsPointVisible(Point3D point, IList<ClipPlane> planes)
        {
            if (planes == null || planes.Count == 0) return true;
            foreach (var plane in planes)
            {
                double v = plane.Eval(point);
                bool inside = KeepPositiveSide ? v >= -Epsilon : v <= Epsilon;
                if (!inside) return false;
            }
            return true;
        }

        /// <summary>
        /// A ModelItem is effectively hidden if it (or any ancestor) is hidden — Navisworks
        /// does not propagate the flag down, so the parent chain must be walked.
        /// </summary>
        public static bool IsEffectivelyHidden(ModelItem item)
        {
            for (var cur = item; cur != null; cur = cur.Parent)
            {
                if (cur.IsHidden) return true;
            }
            return false;
        }

        /// <summary>Diagnostic dump of the raw clip-plane structure for Windows calibration.</summary>
        public string DumpClipPlanes(Document doc)
        {
            var sb = new StringBuilder();
            if (doc == null) { return "No document open."; }

            try
            {
                var bf = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance;
                object comState = (object)ComApiBridge.State;
                object view = Get(comState, "CurrentView");
                if (view == null) { return "CurrentView == null"; }

                object clipColl = view.GetType().InvokeMember("ClippingPlanes", bf, null, view, null);
                if (clipColl == null) { return "ClippingPlanes() == null"; }

                int count = Convert.ToInt32(Get(clipColl, "Count"));
                sb.AppendLine($"ClippingPlanes.Count = {count}");
                sb.AppendLine($"KeepPositiveSide = {KeepPositiveSide}");
                sb.AppendLine();

                for (int i = 1; i <= count; i++)
                {
                    object plane = clipColl.GetType().InvokeMember(
                        "Item", bf, null, clipColl, new object[] { i });
                    sb.AppendLine($"--- Plane {i} ---");
                    if (plane == null) { sb.AppendLine("(null)"); continue; }

                    object enabled = TryGet(plane, "Enabled");
                    sb.AppendLine($"Enabled = {enabled}");

                    object lp = TryGet(plane, "Plane");
                    if (lp == null) { sb.AppendLine("Plane = (null)"); continue; }

                    sb.AppendLine($"Plane type = {lp.GetType().Name}");
                    // Dump every readable member so the real representation is visible.
                    foreach (var name in new[] { "data1", "data2", "data3", "data4", "distance", "normal" })
                    {
                        object val = TryGet(lp, name);
                        if (val != null) sb.AppendLine($"  {name} = {Describe(val)}");
                    }
                }

                var parsed = GetActiveClipPlanes(doc);
                sb.AppendLine();
                sb.AppendLine($"Parsed enabled planes = {parsed.Count}");
                foreach (var p in parsed)
                    sb.AppendLine($"  A={p.A:0.###} B={p.B:0.###} C={p.C:0.###} D={p.D:0.###}");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                sb.AppendLine($"EXCEPTION: {inner.GetType().Name}: {inner.Message}");
            }

            return sb.ToString();
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// Extract the plane equation. Inw geometry structs expose components as
        /// data1..dataN (cf. InwLPos3f/InwLVec3f), so InwLPlane3f is read as
        /// data1..data4 = A,B,C,D. Falls back to normal+distance if present.
        /// </summary>
        private static bool TryReadPlane(object lp, out ClipPlane cp)
        {
            cp = new ClipPlane();
            try
            {
                object d1 = TryGet(lp, "data1");
                object d2 = TryGet(lp, "data2");
                object d3 = TryGet(lp, "data3");
                object d4 = TryGet(lp, "data4");
                if (d1 != null && d2 != null && d3 != null && d4 != null)
                {
                    cp.A = Convert.ToDouble(d1);
                    cp.B = Convert.ToDouble(d2);
                    cp.C = Convert.ToDouble(d3);
                    cp.D = Convert.ToDouble(d4);
                    return true;
                }

                object normal = TryGet(lp, "normal");
                object dist = TryGet(lp, "distance");
                if (normal != null && dist != null)
                {
                    cp.A = Convert.ToDouble(TryGet(normal, "data1"));
                    cp.B = Convert.ToDouble(TryGet(normal, "data2"));
                    cp.C = Convert.ToDouble(TryGet(normal, "data3"));
                    cp.D = Convert.ToDouble(dist);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static object Get(object obj, string member) =>
            obj.GetType().InvokeMember(member, BindingFlags.GetProperty, null, obj, null);

        private static object TryGet(object obj, string member)
        {
            try { return obj.GetType().InvokeMember(member, BindingFlags.GetProperty, null, obj, null); }
            catch { try { return obj.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, obj, null); } catch { return null; } }
        }

        private static string Describe(object val)
        {
            if (val == null) return "(null)";
            var t = val.GetType();
            if (t.IsPrimitive || val is string) return val.ToString();
            // Nested struct (e.g. normal vector) — show its data fields.
            var sb = new StringBuilder(t.Name + " {");
            foreach (var name in new[] { "data1", "data2", "data3" })
            {
                object v = TryGet(val, name);
                if (v != null) sb.Append($" {name}={v}");
            }
            sb.Append(" }");
            return sb.ToString();
        }
    }
}
