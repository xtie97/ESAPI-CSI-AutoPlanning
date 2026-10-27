using System;
using System.Collections.Generic;

namespace CsiAutoPlan.Core
{
    /// <summary>
    /// Eclipse's proton "Air Gap" = smallest distance, along the beam direction, from the downstream face of the
    /// range shifter to the patient outline anywhere inside the snout footprint (Snout tab: Max X x Max Y).
    /// Checked against the clinical Proton 3 CSI plan (RPO, LPO, POST, POST LOWER): with a 37 x 45 cm footprint
    /// all four fields come out at exactly the planned 5.0 cm, whereas the central-axis distances were 7.5-20 cm
    /// (the shoulders limit the lateral brain fields). Pure geometry - no ESAPI types - so it is unit tested.
    ///
    /// Coordinates: DICOM patient mm, HFS, couch 0 (IEC 61217): direction isocenter -> source for gantry g is
    /// (sin g, -cos g, 0); the snout X axis lies in the axial plane, its Y axis along the patient's sup-inf axis.
    /// </summary>
    public static class AirGapCalculator
    {
        /// <summary>
        /// Largest depth (mm) toward the source, measured from the isocenter, of any outline point inside the
        /// footprint |x| &lt;= halfX, |z - isoZ| &lt;= halfY. NegativeInfinity if no point falls inside.
        /// Each contour is a closed polygon of (x, y) points on the plane at z; edges are sampled every stepMm.
        /// </summary>
        public static double MaxDepthTowardSource(IEnumerable<Contour> contours, double isoX, double isoY, double isoZ,
                                                  double gantryDeg, double halfXmm, double halfYmm, double stepMm = 2.0)
        {
            double g = gantryDeg * Math.PI / 180.0;
            double dx = Math.Sin(g), dy = -Math.Cos(g);   // toward the source
            double ux = Math.Cos(g), uy = Math.Sin(g);     // snout X axis in the axial plane
            double best = double.NegativeInfinity;
            foreach (var c in contours)
            {
                if (c == null || c.X == null || c.X.Length == 0) continue;
                if (Math.Abs(c.Z - isoZ) > halfYmm) continue;
                int n = c.X.Length;
                for (int i = 0; i < n; i++)
                {
                    double x0 = c.X[i], y0 = c.Y[i], x1 = c.X[(i + 1) % n], y1 = c.Y[(i + 1) % n];
                    double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    int steps = Math.Max(1, (int)Math.Ceiling(len / stepMm));
                    for (int s = 0; s < steps; s++)
                    {
                        double t = s / (double)steps;
                        double rx = x0 + t * (x1 - x0) - isoX, ry = y0 + t * (y1 - y0) - isoY;
                        if (Math.Abs(rx * ux + ry * uy) > halfXmm) continue;
                        double depth = rx * dx + ry * dy;
                        if (depth > best) best = depth;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Snout position (mm) that gives the requested air gap: depth of the outermost outline point + air gap +
        /// (snout position - isocenter-to-range-shifter distance), the last term being constant per snout / range
        /// shifter (65 mm "PBS Snout P5", 85 mm "PBS Snout" in the clinical exports).
        /// </summary>
        public static double SnoutPositionMm(double maxDepthMm, double airGapMm, double snoutToRangeShifterMm)
        {
            return maxDepthMm + airGapMm + snoutToRangeShifterMm;
        }
    }

    /// <summary>One closed outline on an image plane (DICOM mm).</summary>
    public class Contour
    {
        public double Z;
        public double[] X, Y;
        public Contour(double z, double[] x, double[] y) { Z = z; X = x; Y = y; }
    }
}
