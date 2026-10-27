using System;
using System.Collections.Generic;
using System.Linq;
using CsiAutoPlan.Core;
using CsiAutoPlan.Settings;
using VMS.TPS.Common.Model.API;

namespace CsiAutoPlan.Esapi
{
    /// <summary>
    /// Step D, after the fields exist: sets each treatment field's proximal and distal target margin from its own
    /// range (PBSTV margin sheet, Core/RangeMarginCalculator):
    ///   distal = R x 3.5% + 3 mm,  proximal = (R - SOBP) x 3.5% + 3 mm.
    /// R and SOBP are Eclipse's nominal range and SOBP width of the field (IonBeam.NominalRange / NominalSOBPWidth),
    /// read with the proximal and distal margins at 0 so that they describe the field target itself. If Eclipse has
    /// no values yet, the beam line is calculated once (spot placement) to obtain them. A field without usable
    /// values keeps the fixed margins of SiteSettings.cs and gets a checklist item. Lateral margins are not changed.
    /// </summary>
    public class RangeMarginBuilder
    {
        private readonly CsiSession S;
        private RunLog Log { get { return S.Log; } }
        private RangeMarginOptions R { get { return S.Cfg.RangeMargins; } }

        public RangeMarginBuilder(CsiSession session) { S = session; }

        private class Nominal
        {
            public double RangeMm = double.NaN, SobpMm = double.NaN;
            public string Problem;
            public bool Ok { get { return Problem == null; } }
        }

        public bool Apply(IonPlanSetup plan)
        {
            Log.Step = "Range margins";
            var beams = plan.IonBeams.Where(b => !b.IsSetupField).ToList();
            if (beams.Count == 0) return true;
            if (!S.UseRangeMargins)
            {
                Log.Info("Range margins off for this run: fixed proximal / distal margins from SiteSettings.cs.");
                return true;
            }
            Log.Info("Proximal / distal margins per field: distal = R x {0}% + {1} mm, proximal = (R - SOBP) x {0}% + {1} mm, rounded up to {2} mm.",
                     R.RangeUncertaintyPercent, R.AddedMarginMm, R.RoundUpToMm);

            // The fields were created with the fixed margins; keep them to restore on any field that cannot be computed.
            var fixedMargins = beams.ToDictionary(b => b, b => new[] { b.ProximalTargetMargin, b.DistalTargetMargin });
            var done = new HashSet<IonBeam>();
            var byHand = new List<string>();
            bool beamLineCalculated = false;
            try
            {
                foreach (var b in beams) { b.ProximalTargetMargin = 0; b.DistalTargetMargin = 0; }
                var nominal = Read(beams);
                var missing = beams.Where(b => !nominal[b].Ok).ToList();
                if (missing.Count > 0 && R.CalculateBeamLineIfNeeded)
                {
                    Log.Info("No nominal range / SOBP yet for {0}: calculating the beam line with proximal and distal margins at 0 ...",
                             string.Join(", ", missing.Select(b => b.Id)));
                    var started = DateTime.Now;
                    var res = plan.CalculateBeamLine();
                    if (res != null && res.Success)
                    {
                        beamLineCalculated = true;
                        Log.Info("Beam line calculated ({0:F1} min).", (DateTime.Now - started).TotalMinutes);
                        nominal = Read(beams);
                    }
                    else Log.Warn("Beam line calculation failed, so Eclipse's nominal range / SOBP are not available.");
                }

                foreach (var b in beams)
                {
                    var n = nominal[b];
                    if (!n.Ok)
                    {
                        Restore(b, fixedMargins[b]);
                        Log.Warn("{0}: {1} - keeps the fixed margins (proximal {2:0.#} / distal {3:0.#} mm).", b.Id, n.Problem, fixedMargins[b][0], fixedMargins[b][1]);
                        byHand.Add(b.Id);
                        done.Add(b);
                        continue;
                    }
                    double distal = RangeMarginCalculator.DistalMm(n.RangeMm, R.RangeUncertaintyPercent, R.AddedMarginMm, R.RoundUpToMm);
                    double proximal = RangeMarginCalculator.ProximalMm(n.RangeMm, n.SobpMm, R.RangeUncertaintyPercent, R.AddedMarginMm, R.RoundUpToMm);
                    b.ProximalTargetMargin = proximal;
                    b.DistalTargetMargin = distal;
                    done.Add(b);
                    Log.Info("{0}: R {1:F2} cm, SOBP {2:F2} cm -> distal {3:F2} cm, proximal {4:F2} cm (read back {5:F2} / {6:F2} cm).",
                             b.Id, n.RangeMm / 10, n.SobpMm / 10, distal / 10, proximal / 10, b.DistalTargetMargin / 10, b.ProximalTargetMargin / 10);
                }
            }
            finally
            {
                // never leave a field at 0 mm after an unexpected error
                foreach (var b in beams.Where(x => !done.Contains(x))) Restore(b, fixedMargins[b]);
            }
            if (byHand.Count > 0)
                Log.Manual("Set the proximal / distal target margins of {0} by hand from R and SOBP in Field Properties: distal = R x {1}% + {2} mm, proximal = (R - SOBP) x {1}% + {2} mm.",
                           string.Join(", ", byHand), R.RangeUncertaintyPercent, R.AddedMarginMm);

            // What Eclipse reports with the margins in place, for checking on the test system.
            var after = Read(beams);
            Log.Info("Nominal range / SOBP reported now (with the margins): {0}.",
                     string.Join("; ", beams.Select(b => after[b].Ok ? string.Format("{0} {1:F2} / {2:F2} cm", b.Id, after[b].RangeMm / 10, after[b].SobpMm / 10)
                                                                    : b.Id + " -")));
            if (beamLineCalculated && S.DoOptimize)
                Log.Info("The beam line was calculated with zero margins to read R and SOBP; step F recalculates it with the range margins.");
            else if (beamLineCalculated)
                Log.Manual("Recalculate the beam line before optimizing (step F does this): the current spots were placed with zero proximal / distal margins.");
            Log.Manual("Check the proximal / distal target margins in Field Properties against the PBSTV margin sheet (R and SOBP per field in the log).");
            return true;
        }

        private Dictionary<IonBeam, Nominal> Read(IEnumerable<IonBeam> beams)
        {
            var d = new Dictionary<IonBeam, Nominal>();
            foreach (var b in beams)
            {
                var n = new Nominal();
                try
                {
                    n.RangeMm = b.NominalRange;
                    n.SobpMm = b.NominalSOBPWidth;
                    string why;
                    if (!RangeMarginCalculator.IsUsable(n.RangeMm, n.SobpMm, out why)) n.Problem = why;
                }
                catch (Exception ex) { n.Problem = "Eclipse did not report a nominal range (" + ex.Message + ")"; }
                d[b] = n;
            }
            return d;
        }

        private static void Restore(IonBeam b, double[] proxDist)
        {
            b.ProximalTargetMargin = proxDist[0];
            b.DistalTargetMargin = proxDist[1];
        }
    }
}
