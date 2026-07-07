using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using ComTypes = System.Runtime.InteropServices.ComTypes;

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
                object comState = (object)ComApiBridge.State;
                object view = Invoke(comState, "CurrentView");
                if (view == null) return planes;

                object clipColl = Invoke(view, "ClippingPlanes");
                if (clipColl == null) return planes;

                object countObj = Invoke(clipColl, "Count");
                if (countObj == null) return planes;
                int count = Convert.ToInt32(countObj);

                for (int i = 1; i <= count; i++) // COM collections are 1-based
                {
                    object plane = Invoke(clipColl, "Item", i);
                    if (plane == null) continue;

                    object enabledObj = Invoke(plane, "Enabled");
                    if (enabledObj == null || !Convert.ToBoolean(enabledObj)) continue;

                    object lp = Invoke(plane, "Plane"); // InwLPlane3f
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
                object comState = (object)ComApiBridge.State;
                sb.AppendLine($"State type   = {TypeName(comState)}");

                object view = Invoke(comState, "CurrentView");
                sb.AppendLine($"CurrentView  = {(view == null ? "NULL (멤버명 다름?)" : TypeName(view))}");
                if (view == null) { sb.AppendLine("→ CurrentView를 못 읽음. 여기서 중단."); return sb.ToString(); }

                // Section BOX may not live in ClippingPlanes(). Enumerate the view's real
                // members so a box/section accessor (SectionBox / ClipBox / GetClipPlaneSet …)
                // shows up. Run this dump WHILE a section box is active to compare.
                sb.AppendLine("  [InwOpView(CurrentView) 실제 멤버 목록 — 박스 접근자 탐색용]");
                foreach (var m in ListComMembers(view))
                    sb.AppendLine($"    · {m}");

                // GetTypeInfo often exposes only base members, so also probe likely box
                // accessor names by late binding (same trick as GetNormal/distance below).
                sb.AppendLine("  [View 박스 접근자 후보 프로브 (non-null이면 그 경로가 박스)]");
                foreach (var name in new[] { "SectionBox", "ClipBox", "ClippingBox", "CuttingBox",
                                             "Box", "GetClipPlaneSet", "ClipPlaneSet", "SectionPlanes",
                                             "GetSectionBox", "ClippingBoxPlanes", "OrientedBox" })
                {
                    object val = TryGet(view, name);
                    if (val != null) sb.AppendLine($"    {name} → {TypeName(val)}");
                }
                sb.AppendLine();

                object clipColl = Invoke(view, "ClippingPlanes");
                sb.AppendLine($"ClippingPlanes = {(clipColl == null ? "NULL (멤버명 다름?)" : TypeName(clipColl))}");
                if (clipColl == null) { sb.AppendLine("→ ClippingPlanes를 못 읽음. 여기서 중단."); return sb.ToString(); }

                // Enumerate the collection's members too — box mode may expose a mode flag
                // or a box definition here rather than as extra planes.
                sb.AppendLine("  [InwOpClipPlaneColl 실제 멤버 목록]");
                foreach (var m in ListComMembers(clipColl))
                    sb.AppendLine($"    · {m}");

                object countObj = Invoke(clipColl, "Count");
                sb.AppendLine($"Count        = {(countObj?.ToString() ?? "NULL")}");
                sb.AppendLine($"KeepPositiveSide = {KeepPositiveSide}");
                sb.AppendLine("(Planes 모드와 Box 모드에서 각각 덤프해 Count·Enabled 개수·법선을 비교할 것)");
                sb.AppendLine();

                int count = countObj == null ? 0 : Convert.ToInt32(countObj);
                for (int i = 1; i <= count; i++)
                {
                    object plane = Invoke(clipColl, "Item", i);
                    sb.AppendLine($"--- Plane {i} ---");
                    if (plane == null) { sb.AppendLine("Item() = NULL"); continue; }
                    sb.AppendLine($"  plane type = {TypeName(plane)}");

                    // The normal/alignment may live on the clip-plane object itself
                    // (set by the Sectioning "정렬 방향"), not only on InwLPlane3f.
                    sb.AppendLine("  [ClipPlane(InwOaClipPlane) 실제 멤버 목록]");
                    foreach (var m in ListComMembers(plane))
                        sb.AppendLine($"    · {m}");

                    object enabled = Invoke(plane, "Enabled");
                    sb.AppendLine($"  Enabled = {(enabled?.ToString() ?? "NULL")}");

                    object lp = Invoke(plane, "Plane");
                    if (lp == null) { sb.AppendLine("  Plane = NULL"); continue; }
                    sb.AppendLine($"  Plane type = {TypeName(lp)}");

                    // Probe every plausible accessor so the real representation shows up.
                    foreach (var name in new[] { "GetNormal", "distance", "DistanceEx",
                                                 "data1", "data2", "data3", "data4",
                                                 "normal", "Normal" })
                    {
                        object val = TryGet(lp, name);
                        if (val != null) sb.AppendLine($"    {name} = {Describe(val)}");
                    }

                    // Definitive: enumerate the actual IDispatch member names of the plane.
                    sb.AppendLine("  [InwLPlane3f 실제 멤버 목록]");
                    foreach (var m in ListComMembers(lp))
                        sb.AppendLine($"    · {m}");
                }

                var parsed = GetActiveClipPlanes(doc);
                sb.AppendLine();
                sb.AppendLine($"==> 파싱된 활성 평면 수 = {parsed.Count}");
                foreach (var p in parsed)
                    sb.AppendLine($"    A={p.A:0.###} B={p.B:0.###} C={p.C:0.###} D={p.D:0.###}");
                if (parsed.Count == 0)
                    sb.AppendLine("(0이면 보이는 것만 필터가 단면을 인식 못함 → 위 멤버명 확인 필요)");
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
        /// Extract the plane equation. Confirmed on Navisworks 2022: InwLPlane3f exposes
        /// GetNormal() (method → unit vector with data1/2/3) and distance() (method →
        /// signed distance of the plane from the origin along the normal).
        /// We store Eval(p) = n·p - distance = signed distance of p from the plane;
        /// the kept side is chosen by <see cref="KeepPositiveSide"/>.
        /// </summary>
        private static bool TryReadPlane(object lp, out ClipPlane cp)
        {
            cp = new ClipPlane();
            try
            {
                object normal = Invoke(lp, "GetNormal");   // InwLUnitVec3f
                object distObj = Invoke(lp, "distance");
                if (normal != null && distObj != null && TryReadVec(normal, out double nx, out double ny, out double nz))
                {
                    double dist = Convert.ToDouble(distObj);
                    cp.A = nx; cp.B = ny; cp.C = nz;
                    cp.D = -dist;                          // n·p - distance = 0 at the plane
                    return true;
                }

                // Fallbacks (older/other builds): data1..4 = A,B,C,D, or normal+distance.
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
            }
            catch { }
            return false;
        }

        /// <summary>Read a 3-component Inw vector, trying the known component accessors.</summary>
        private static bool TryReadVec(object vec, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (vec == null) return false;
            object vx = TryGet(vec, "data1") ?? TryGet(vec, "x") ?? TryGet(vec, "X");
            object vy = TryGet(vec, "data2") ?? TryGet(vec, "y") ?? TryGet(vec, "Y");
            object vz = TryGet(vec, "data3") ?? TryGet(vec, "z") ?? TryGet(vec, "Z");
            if (vx == null || vy == null || vz == null) return false;
            x = Convert.ToDouble(vx); y = Convert.ToDouble(vy); z = Convert.ToDouble(vz);
            return true;
        }

        /// <summary>Invoke a COM member, trying property-get first then method call. Null on failure.</summary>
        private static object Invoke(object obj, string member, params object[] args)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            try { return t.InvokeMember(member, BindingFlags.GetProperty, null, obj, args); }
            catch { }
            try { return t.InvokeMember(member, BindingFlags.InvokeMethod, null, obj, args); }
            catch { return null; }
        }

        private static object TryGet(object obj, string member)
        {
            if (obj == null) return null;
            try { return obj.GetType().InvokeMember(member, BindingFlags.GetProperty, null, obj, null); }
            catch { try { return obj.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, obj, null); } catch { return null; } }
        }

        private static string TypeName(object o)
        {
            if (o == null) return "(null)";
            try { return o.GetType().Name; } catch { return "?"; }
        }

        [ComImport, Guid("00020400-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            void GetTypeInfoCount(out int pctinfo);
            void GetTypeInfo(int iTInfo, int lcid, out ComTypes.ITypeInfo info);
            // GetIDsOfNames / Invoke intentionally omitted — not called.
        }

        /// <summary>List the real IDispatch member names of a COM object via its ITypeInfo.</summary>
        private static List<string> ListComMembers(object obj)
        {
            var result = new List<string>();
            if (obj == null) return result;
            try
            {
                if (!(obj is IDispatch disp)) { result.Add("(IDispatch 아님)"); return result; }
                disp.GetTypeInfo(0, 0, out var ti);
                if (ti == null) { result.Add("(ITypeInfo 없음)"); return result; }

                ti.GetTypeAttr(out IntPtr pAttr);
                var attr = (ComTypes.TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(ComTypes.TYPEATTR));
                try
                {
                    for (int i = 0; i < attr.cFuncs; i++)
                    {
                        ti.GetFuncDesc(i, out IntPtr pFunc);
                        try
                        {
                            var fd = (ComTypes.FUNCDESC)Marshal.PtrToStructure(pFunc, typeof(ComTypes.FUNCDESC));
                            var names = new string[1];
                            ti.GetNames(fd.memid, names, 1, out int cn);
                            if (cn > 0) result.Add($"{names[0]}  (invkind={fd.invkind}, params={fd.cParams})");
                        }
                        finally { ti.ReleaseFuncDesc(pFunc); }
                    }
                }
                finally { ti.ReleaseTypeAttr(pAttr); }
            }
            catch (Exception ex) { result.Add($"(열거 실패: {ex.Message})"); }
            return result;
        }

        private static string Describe(object val)
        {
            if (val == null) return "(null)";
            var t = val.GetType();
            if (t.IsPrimitive || val is string) return val.ToString();
            // Nested struct (e.g. normal vector) — show its components.
            if (TryReadVec(val, out double x, out double y, out double z))
                return $"{TypeName(val)}(vec) {{ {x}, {y}, {z} }}";
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
