using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    public class SliceExtent
    {
        public int Index;
        public double Z;
        public double XMin, XMax, YMin, YMax;
        public double Width { get { return XMax - XMin; } }
        public double XCenter { get { return 0.5 * (XMin + XMax); } }
        public double YCenter { get { return 0.5 * (YMin + YMax); } }
    }

    /// <summary>Low-level structure helpers (lookup, slice geometry, slabs, safe boolean writes).</summary>
    public static class StructureTools
    {
        public static Structure Find(StructureSet ss, string aliases)
        {
            if (ss == null || string.IsNullOrWhiteSpace(aliases)) return null;
            foreach (var a in aliases.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                var s = ss.Structures.FirstOrDefault(x => string.Equals(x.Id, a, StringComparison.OrdinalIgnoreCase));
                if (s != null && !s.IsEmpty) return s;
            }
            return null;
        }

        public static Structure FindExact(StructureSet ss, string id)
        {
            return ss.Structures.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<Structure> FindByRegex(StructureSet ss, string pattern)
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            return ss.Structures.Where(s => !s.IsEmpty && rx.IsMatch(s.Id));
        }

        // ---------------------------------------------------------------- image geometry
        public static double PlaneZ(Image img, int k) { return img.Origin.z + k * img.ZRes * img.ZDirection.z; }

        public static void CheckAxial(Image img)
        {
            if (Math.Abs(Math.Abs(img.XDirection.x) - 1) > 1e-3 || Math.Abs(Math.Abs(img.YDirection.y) - 1) > 1e-3 ||
                Math.Abs(Math.Abs(img.ZDirection.z) - 1) > 1e-3)
                throw new InvalidOperationException("Image " + img.Id + " is not an axis-aligned axial series; the plugin requires standard axial CT.");
        }

        /// <summary>Per-slice bounding box of a structure (contours smaller than minAreaMm2 ignored).</summary>
        public static List<SliceExtent> SliceExtents(Structure s, Image img, double minAreaMm2 = 0)
        {
            var list = new List<SliceExtent>();
            for (int k = 0; k < img.ZSize; k++)
            {
                var contours = s.GetContoursOnImagePlane(k);
                if (contours == null || contours.Length == 0) continue;
                SliceExtent e = null;
                foreach (var c in contours)
                {
                    if (c == null || c.Length < 3) continue;
                    if (minAreaMm2 > 0 && Math.Abs(PolygonArea(c)) < minAreaMm2) continue;
                    if (e == null) e = new SliceExtent { Index = k, Z = PlaneZ(img, k), XMin = double.MaxValue, XMax = double.MinValue, YMin = double.MaxValue, YMax = double.MinValue };
                    foreach (var p in c)
                    {
                        if (p.x < e.XMin) e.XMin = p.x;
                        if (p.x > e.XMax) e.XMax = p.x;
                        if (p.y < e.YMin) e.YMin = p.y;
                        if (p.y > e.YMax) e.YMax = p.y;
                    }
                }
                if (e != null) list.Add(e);
            }
            return list;
        }

        public static double PolygonArea(VVector[] c)
        {
            double a = 0;
            for (int i = 0, j = c.Length - 1; i < c.Length; j = i++)
                a += (c[j].x + c[i].x) * (c[j].y - c[i].y);
            return 0.5 * a;
        }

        // ---------------------------------------------------------------- structure creation
        /// <summary>
        /// Returns a structure ready to receive a new SegmentVolume. Existing structures are reused only
        /// when <paramref name="allowOverwrite"/> is true and Eclipse allows editing (e.g. not approved).
        /// </summary>
        public static Structure GetOrCreate(StructureSet ss, string id, string dicomType, bool allowOverwrite, RunLog log)
        {
            var s = FindExact(ss, id);
            if (s != null)
            {
                if (!allowOverwrite && !s.IsEmpty)
                {
                    log.Warn("'{0}' already exists and was left unchanged.", id);
                    return null;
                }
                string err;
                if (!s.CanEditSegmentVolume(out err))
                {
                    log.Error("'{0}' exists but cannot be edited: {1}", id, err);
                    return null;
                }
                return s;
            }
            if (!ss.CanAddStructure(dicomType, id))
            {
                log.Error("Eclipse refused to add structure '{0}' ({1}).", id, dicomType);
                return null;
            }
            return ss.AddStructure(dicomType, id);
        }

        /// <summary>High-resolution inputs require a high-resolution output for boolean operations.</summary>
        public static void MatchResolution(Structure target, params Structure[] sources)
        {
            if (target == null || target.IsHighResolution) return;
            var hi = sources.FirstOrDefault(s => s != null && s.IsHighResolution);
            if (hi == null) return;
            if (target.IsEmpty)
                target.SegmentVolume = hi.SegmentVolume.Sub(hi.SegmentVolume);   // empty, high-res (pattern from Varian MAAS samples)
            else if (target.CanConvertToHighResolution())
                target.ConvertToHighResolution();
        }

        /// <summary>
        /// Creates a temporary CONTROL structure filling the whole image on every plane with
        /// zLo &lt;= z &lt;= zHi. Used to cut structures in the sup-inf direction.
        /// </summary>
        public static Structure CreateSlab(StructureSet ss, string id, double zLo, double zHi, RunLog log, bool snapOutward = true)
        {
            var img = ss.Image;
            var s = GetOrCreate(ss, id, "CONTROL", true, log);
            if (s == null) throw new InvalidOperationException("Cannot create temporary structure " + id);
            if (!s.IsEmpty) s.SegmentVolume = s.SegmentVolume.Sub(s.SegmentVolume);   // leftover from an aborted run

            double xa = img.Origin.x, xb = img.Origin.x + (img.XSize - 1) * img.XRes * img.XDirection.x;
            double ya = img.Origin.y, yb = img.Origin.y + (img.YSize - 1) * img.YRes * img.YDirection.y;
            double x0 = Math.Min(xa, xb) + 2 * img.XRes, x1 = Math.Max(xa, xb) - 2 * img.XRes;
            double y0 = Math.Min(ya, yb) + 2 * img.YRes, y1 = Math.Max(ya, yb) - 2 * img.YRes;
            if (snapOutward)
            {
                // Include the nearest image plane at/beyond each boundary so a cut between planes never
                // shortens the volume (keeps the planned junction overlap).
                var planes = Enumerable.Range(0, img.ZSize).Select(k => PlaneZ(img, k)).ToList();
                var below = planes.Where(z => z <= zLo + 0.01).ToList();
                var above = planes.Where(z => z >= zHi - 0.01).ToList();
                if (below.Count > 0) zLo = below.Max();
                if (above.Count > 0) zHi = above.Min();
            }
            int added = 0;
            for (int k = 0; k < img.ZSize; k++)
            {
                double z = PlaneZ(img, k);
                if (z < zLo - 0.01 || z > zHi + 0.01) continue;
                var rect = new[] { new VVector(x0, y0, z), new VVector(x1, y0, z), new VVector(x1, y1, z), new VVector(x0, y1, z) };
                s.AddContourOnImagePlane(rect, k);
                added++;
            }
            if (added == 0) log.Warn("Slab {0} [{1:F1}, {2:F1}] contains no image planes.", id, zLo, zHi);
            return s;
        }

        public static SegmentVolume ApplyMargins(SegmentVolume sv, Settings.MarginSet m)
        {
            if (m.IsUniform)
                return Math.Abs(m.RightMm) < 1e-6 ? sv : sv.Margin(m.RightMm);
            // AxisAlignedMargins(geometry, x1, y1, z1, x2, y2, z2): x1/y1/z1 = towards -x (R), -y (A), -z (I)
            return sv.AsymmetricMargin(new AxisAlignedMargins(StructureMarginGeometry.Outer,
                m.RightMm, m.AntMm, m.InfMm, m.LeftMm, m.PostMm, m.SupMm));
        }

        public static void SafeRemove(StructureSet ss, Structure s, RunLog log)
        {
            if (s == null) return;
            if (ss.CanRemoveStructure(s)) ss.RemoveStructure(s);
            else log.Warn("Could not remove temporary structure {0} - delete it manually.", s.Id);
        }

        /// <summary>Removes a script-owned derived structure that no longer belongs to the current split.</summary>
        public static void RemoveIfPresent(StructureSet ss, string id, RunLog log, string why)
        {
            var s = FindExact(ss, id);
            if (s == null) return;
            if (ss.CanRemoveStructure(s)) { ss.RemoveStructure(s); log.Info("Removed stale {0} ({1}).", id, why); }
            else log.Warn("Stale {0} ({1}) could not be removed - delete it manually.", id, why);
        }

        public static void RemoveTemporaries(StructureSet ss, string prefix, RunLog log)
        {
            foreach (var s in ss.Structures.Where(x => x.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                SafeRemove(ss, s, log);
        }
    }
}
