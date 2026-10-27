using System;
using System.Collections.Generic;
using System.Linq;

namespace CsiAutoPlan.Core
{
    public class WidthSample
    {
        public double Z;        // DICOM z (mm)
        public double Width;    // left-right body width on this slice (mm)
        public WidthSample(double z, double w) { Z = z; Width = w; }
    }

    public class ShoulderResult
    {
        public bool Success;
        public double ShoulderTopZ;
        public double NeckZ;
        public double NeckWidth;
        public double ShoulderWidth;
        public string Message = "";
    }

    /// <summary>
    /// Finds the top of the shoulders from the left-right width profile of the BODY contour.
    /// Scanning inferiorly from the bottom of the brain: the narrowest slice is the neck; the
    /// shoulder top is the first slice below the neck whose width has risen by
    /// <c>riseFraction</c> of the neck-to-shoulder width difference.
    /// </summary>
    public static class ShoulderDetector
    {
        public static ShoulderResult Detect(IEnumerable<WidthSample> profile, double brainBottomZ,
                                            double searchWindowMm = 200, double riseFraction = 0.3,
                                            double minShoulderToNeckRatio = 1.4)
        {
            var r = new ShoulderResult();
            var win = profile
                .Where(p => p.Width > 0 && p.Z <= brainBottomZ && p.Z >= brainBottomZ - searchWindowMm)
                .OrderByDescending(p => p.Z)                 // superior -> inferior
                .ToList();
            if (win.Count < 5)
            {
                r.Message = "Too few BODY slices below the brain to detect the shoulders.";
                return r;
            }

            int neckIdx = 0;
            for (int i = 1; i < win.Count; i++)
                if (win[i].Width < win[neckIdx].Width) neckIdx = i;

            var below = win.Skip(neckIdx + 1).ToList();
            if (below.Count == 0)
            {
                r.Message = "Neck minimum is at the bottom of the search window - extend the scan / window.";
                return r;
            }

            r.NeckZ = win[neckIdx].Z;
            r.NeckWidth = win[neckIdx].Width;
            r.ShoulderWidth = below.Max(p => p.Width);
            if (r.ShoulderWidth < minShoulderToNeckRatio * r.NeckWidth)
            {
                r.Message = string.Format("Shoulders not clearly found (max width {0:F0} mm vs neck {1:F0} mm).",
                                          r.ShoulderWidth, r.NeckWidth);
                return r;
            }

            double threshold = r.NeckWidth + riseFraction * (r.ShoulderWidth - r.NeckWidth);
            var hit = below.FirstOrDefault(p => p.Width >= threshold);
            if (hit == null)
            {
                r.Message = "No slice reached the shoulder threshold.";
                return r;
            }
            r.ShoulderTopZ = hit.Z;
            r.Success = true;
            r.Message = string.Format("Neck {0:F0} mm wide at z={1:F1}; shoulders {2:F0} mm wide; shoulder top at z={3:F1}.",
                                      r.NeckWidth, r.NeckZ, r.ShoulderWidth, r.ShoulderTopZ);
            return r;
        }
    }
}
