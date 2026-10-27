using System;
using System.Collections.Generic;
using System.Linq;

namespace CsiAutoPlan.Core
{
    /// <summary>
    /// Inputs for the field-target split. All positions are DICOM patient z (mm, +z = superior).
    /// Pure data - no ESAPI dependency so it can be unit tested outside Eclipse.
    /// </summary>
    public class GeometryInput
    {
        public double BrainIsoZ;            // brain isocenter z (user origin by default)
        public double CtvTopZ;              // most superior CTV slice
        public double CtvBottomZ;           // most inferior CTV slice (sacrum)
        public double ShoulderTopZ;         // detected / user-entered top of shoulders

        public double HalfFieldLimitMm = 200;   // max |spot - iso| in sup-inf (17 cm P1/P3, 20 cm P4/P5)
        public double SpotLateralMarginMm = 5;  // field lateral margin added outside the field target
        public double SafetyMm = 5;             // extra headroom (absorbs whole-cm iso rounding)
        /// <summary>
        /// How far each end of a field target may grow when it is built on image planes (slab snapped
        /// outward, S/I margin rounded up to whole slices). Use the CT slice thickness. Reserved from the
        /// scanning limit so the built structure - not just the planned extent - fits its isocenter.
        /// </summary>
        public double SliceAllowanceMm = 0;

        public double BrainMarginSupMm = 5;     // CTV->PBSTV margins along z
        public double BrainMarginInfMm = 5;
        public double SpineMarginSupMm = 5;
        public double SpineMarginInfMm = 5;

        public double ShoulderClearanceMm = 20; // PBSTV_BR inferior edge >= shoulders + this
        public double MinOverlapMm = 20;        // overlap between adjacent field targets (PBSTVs)
        /// <summary>Overlap to aim for when the field lengths allow it (never fewer fields, never below MinOverlapMm).</summary>
        public double PreferredOverlapMm = 0;
        public int MaxSpineFields = 3;
        public int ForcedSpineFieldCount = 0;   // 0 = automatic
        public bool RoundSpineIsoToWholeCm = true;
        /// <summary>
        /// false: procedure text - every spine field gets its own isocenter (UP/MID/LO).
        /// true: a POST field at the BRAIN isocenter covers brain + upper spine as far as the scanning
        /// limit allows; the rest of the spine gets lower isocenters (clinical CSI example: RPO/LPO/POST
        /// at the brain iso + POST LOWER, i.e. one isocenter less).
        /// </summary>
        public bool PosteriorAtBrainIso = false;

        public GeometryInput Clone() { return (GeometryInput)MemberwiseClone(); }
    }

    public class FieldSegment
    {
        public string Name;          // BR, POST, UP, MID, LO
        public double CtvCutLoZ;     // CTV slab used for this field target
        public double CtvCutHiZ;
        public double PbstvLoZ;      // expected field-target extent after margins
        public double PbstvHiZ;
        public double IsoZ;          // isocenter z (DICOM mm)

        public double PbstvLengthMm { get { return PbstvHiZ - PbstvLoZ; } }
        public double IsoOffsetFromBrainCm(double brainIsoZ) { return (IsoZ - brainIsoZ) / 10.0; }
        public double DistSupEdgeMm { get { return PbstvHiZ - IsoZ; } }
        public double DistInfEdgeMm { get { return IsoZ - PbstvLoZ; } }

        public override string ToString()
        {
            return string.Format("{0}: CTV [{1:F1},{2:F1}] PBSTV [{3:F1},{4:F1}] L={5:F1} iso={6:F1}",
                Name, CtvCutLoZ, CtvCutHiZ, PbstvLoZ, PbstvHiZ, PbstvLengthMm, IsoZ);
        }
    }

    public class GeometryPlan
    {
        public FieldSegment Brain;          // lateral brain fields (RPO/LPO)
        public FieldSegment Post;           // optional POST field at the brain isocenter
        public List<FieldSegment> Spine = new List<FieldSegment>();
        public double OverlapUsedMm;
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        public bool IsFeasible { get { return Errors.Count == 0; } }

        public IEnumerable<FieldSegment> All()
        {
            if (Brain != null) yield return Brain;
            if (Post != null) yield return Post;
            foreach (var s in Spine) yield return s;
        }

        /// <summary>The field targets that hand over to each other from vertex to sacrum (overlap is checked along this chain).</summary>
        public List<FieldSegment> Chain()
        {
            var c = new List<FieldSegment>();
            var top = Post ?? Brain;
            if (top != null) c.Add(top);
            c.AddRange(Spine);
            return c;
        }

        /// <summary>Number of distinct isocenters.</summary>
        public int IsocenterCount { get { return 1 + Spine.Count; } }
    }

    /// <summary>
    /// Implements the CSI field-target split from the PBS craniospinal procedure:
    ///  - PBSTV_BR covers CTV from the vertex down to a C-spine level >= 2 cm above the shoulders,
    ///    and must stay inside the scanning limit of the brain isocenter.
    ///  - Optionally a POST field at the brain isocenter covers the CTV from the vertex as far down as
    ///    the scanning limit allows.
    ///  - Lower spine field targets overlap each neighbour by >= MinOverlap (up to PreferredOverlap
    ///    when there is room), are of equal length, and each fits inside the scanning limit of its own
    ///    isocenter, which sits at mid-level rounded to a whole-cm shift from the brain isocenter.
    /// </summary>
    public static class CsiGeometryPlanner
    {
        public static readonly string[][] SpineNames =
        {
            new string[0],
            new[] { "UP" },
            new[] { "UP", "LO" },
            new[] { "UP", "MID", "LO" },
            new[] { "UP", "MID", "MID2", "LO" },
        };

        /// <summary>Names of the lower segments when a POST field at the brain iso covers the upper spine
        /// (one lower field = "POST LOWER" as in the Proton 3 example; two = UP/LO as in the Proton 4 examples).</summary>
        public static readonly string[][] LowerNames =
        {
            new string[0],
            new[] { "LO" },
            new[] { "UP", "LO" },
            new[] { "UP", "MID", "LO" },
            new[] { "UP", "MID", "MID2", "LO" },
        };

        public static GeometryPlan Plan(GeometryInput g)
        {
            var basePlan = PlanCore(g, g.MinOverlapMm);
            if (!basePlan.IsFeasible || g.PreferredOverlapMm <= g.MinOverlapMm || basePlan.Spine.Count == 0)
                return basePlan;

            // Use spare field length for a larger overlap, keeping the same number of fields.
            var forced = g.Clone();
            forced.ForcedSpineFieldCount = basePlan.Spine.Count;
            for (double o = g.PreferredOverlapMm; o > g.MinOverlapMm; o -= 1.0)
            {
                var p = PlanCore(forced, o);
                if (p.IsFeasible && p.Spine.Count == basePlan.Spine.Count) return p;
            }
            return basePlan;
        }

        private static GeometryPlan PlanCore(GeometryInput g, double overlap)
        {
            var plan = new GeometryPlan { OverlapUsedMm = overlap };
            if (g.CtvTopZ <= g.CtvBottomZ) { plan.Errors.Add("CTV extent is empty or inverted."); return plan; }
            if (g.HalfFieldLimitMm <= 0) { plan.Errors.Add("Field limit must be positive."); return plan; }

            double hardHalf = g.HalfFieldLimitMm - g.SpotLateralMarginMm - g.SliceAllowanceMm;   // absolute limit for planned edges
            double usableHalf = hardHalf - g.SafetyMm;                                             // H (leaves room for iso rounding)

            // ---------------- Brain (lateral fields) ----------------
            var br = new FieldSegment { Name = "BR", IsoZ = g.BrainIsoZ, CtvCutHiZ = g.CtvTopZ };
            br.PbstvHiZ = g.CtvTopZ + g.BrainMarginSupMm;
            if (br.PbstvHiZ - g.BrainIsoZ > hardHalf)
                plan.Errors.Add(string.Format(
                    "Brain field target extends {0:F1} cm above the brain isocenter (limit {1:F1} cm incl. spot margin). Move the brain isocenter superiorly.",
                    (br.PbstvHiZ - g.BrainIsoZ) / 10.0, hardHalf / 10.0));

            double brLoByShoulder = g.ShoulderTopZ + g.ShoulderClearanceMm;
            double brLoByField = g.BrainIsoZ - usableHalf;
            br.PbstvLoZ = Math.Max(brLoByShoulder, brLoByField);
            if (brLoByField > brLoByShoulder && !g.PosteriorAtBrainIso)
                plan.Warnings.Add(string.Format(
                    "Brain field target bottom is limited by the scanning field ({0:F1} cm below brain iso), not by the shoulders. The upper C-spine will be covered by the UP spine field.",
                    usableHalf / 10.0));
            br.CtvCutLoZ = br.PbstvLoZ + g.BrainMarginInfMm;
            if (br.CtvCutLoZ >= g.CtvTopZ) { plan.Errors.Add("Brain field target is empty: check shoulder level / isocenter."); return plan; }
            plan.Brain = br;

            // ---------------- Optional POST field at the brain isocenter ----------------
            double chainBottomPbstv = br.PbstvLoZ;     // PBSTV bottom of the field the spine chain hands over from
            if (g.PosteriorAtBrainIso)
            {
                var post = new FieldSegment { Name = "POST", IsoZ = g.BrainIsoZ, CtvCutHiZ = g.CtvTopZ, PbstvHiZ = br.PbstvHiZ };
                post.PbstvLoZ = Math.Max(g.BrainIsoZ - usableHalf, g.CtvBottomZ - g.SpineMarginInfMm);
                post.CtvCutLoZ = Math.Max(post.PbstvLoZ + g.SpineMarginInfMm, g.CtvBottomZ);
                if (post.PbstvLoZ >= br.PbstvLoZ)
                    plan.Warnings.Add("POST field at the brain isocenter does not reach below the lateral brain fields - it adds no spine coverage.");
                plan.Post = post;
                chainBottomPbstv = post.PbstvLoZ;
                if (post.CtvCutLoZ <= g.CtvBottomZ + 1e-6) return plan;     // whole CTV covered from the brain iso
            }

            // ---------------- Lower / spine field targets ----------------
            var names = g.PosteriorAtBrainIso ? LowerNames : SpineNames;
            double firstSpinePbstvHi = chainBottomPbstv + overlap;
            double firstCutHi = firstSpinePbstvHi - g.SpineMarginSupMm;
            double S = firstCutHi - g.CtvBottomZ;                         // CTV length the spine fields must cover
            if (S <= 0)
            {
                plan.Warnings.Add("CTV does not extend below the upper field target: no spine fields generated.");
                return plan;
            }

            double maxCtvSeg = 2 * usableHalf - g.SpineMarginSupMm - g.SpineMarginInfMm;   // l_max
            double cutOverlap = overlap - g.SpineMarginSupMm - g.SpineMarginInfMm;          // c (may be <0)
            if (maxCtvSeg <= Math.Max(cutOverlap, 0)) { plan.Errors.Add("Field limit too small for the requested margins/overlap."); return plan; }

            int maxN = Math.Max(1, Math.Min(g.MaxSpineFields, names.Length - 1));
            int n;
            if (g.ForcedSpineFieldCount > 0)
            {
                n = Math.Min(g.ForcedSpineFieldCount, names.Length - 1);
            }
            else
            {
                n = 0;
                for (int k = 1; k <= maxN; k++)
                {
                    double lk = (S + (k - 1) * cutOverlap) / k;
                    if (lk <= maxCtvSeg + 1e-6) { n = k; break; }
                }
                if (n == 0)
                {
                    n = maxN;
                    plan.Errors.Add(string.Format("Spine needs more than {0} fields for this machine (CTV spine length {1:F1} cm).", maxN, S / 10.0));
                }
            }

            double segLen = (S + (n - 1) * cutOverlap) / n;
            if (segLen > maxCtvSeg + 1e-6)
                plan.Errors.Add(string.Format("Spine field targets ({0:F1} cm CTV each) exceed the usable field length ({1:F1} cm).", segLen / 10.0, maxCtvSeg / 10.0));

            double cutHi = firstCutHi;
            for (int k = 0; k < n; k++)
            {
                var s = new FieldSegment { Name = names[n][k] };
                s.CtvCutHiZ = cutHi;
                s.CtvCutLoZ = (k == n - 1) ? g.CtvBottomZ : cutHi - segLen;
                s.PbstvHiZ = s.CtvCutHiZ + g.SpineMarginSupMm;
                s.PbstvLoZ = s.CtvCutLoZ - g.SpineMarginInfMm;
                s.IsoZ = ChooseIso(g, s, hardHalf, plan);
                plan.Spine.Add(s);
                cutHi = s.CtvCutLoZ + cutOverlap;
            }

            // ---------------- Consistency checks ----------------
            var chain = plan.Chain();
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                double ov = chain[i + 1].PbstvHiZ - chain[i].PbstvLoZ;
                if (ov < g.MinOverlapMm - 0.5)
                    plan.Errors.Add(string.Format("Overlap {0}/{1} is only {2:F1} cm.", chain[i].Name, chain[i + 1].Name, ov / 10.0));
            }
            if (plan.Spine.Count > 3)
                plan.Warnings.Add("More than three spine fields: the procedure only names UP/MID/LO - review naming.");
            return plan;
        }

        /// <summary>Mid-level of the field target, rounded to a whole-cm offset from the brain iso if requested.</summary>
        private static double ChooseIso(GeometryInput g, FieldSegment s, double hardHalf, GeometryPlan plan)
        {
            double mid = 0.5 * (s.PbstvLoZ + s.PbstvHiZ);
            if (!g.RoundSpineIsoToWholeCm) return mid;

            // The nearest whole cm minimises |iso - mid| and therefore the larger iso-to-edge distance;
            // no other whole-cm position can do better.
            double iso = g.BrainIsoZ + Math.Round((mid - g.BrainIsoZ) / 10.0, MidpointRounding.AwayFromZero) * 10.0;
            double excess = Math.Max(s.PbstvHiZ - iso, iso - s.PbstvLoZ) - hardHalf;
            if (excess > 0)
                plan.Errors.Add(string.Format("{0} field target cannot be covered from a whole-cm isocenter ({1:F1} mm over the limit).", s.Name, excess));
            return iso;
        }

        /// <summary>Round an A-P (or any) offset to the nearest whole cm.</summary>
        public static double RoundToCm(double mm)
        {
            return Math.Round(mm / 10.0, MidpointRounding.AwayFromZero) * 10.0;
        }
    }
}
