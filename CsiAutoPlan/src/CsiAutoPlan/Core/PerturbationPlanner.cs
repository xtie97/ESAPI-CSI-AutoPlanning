using System.Collections.Generic;
using System.Linq;

namespace CsiAutoPlan.Core
{
    /// <summary>One field-specific uncertainty scenario: a set of (beam, shift) pairs applied together.</summary>
    public class FieldPerturbation
    {
        public string Label;
        /// <summary>Beam ID -> shift in Eclipse user coordinates (cm): X = L/R, Y = sup/inf, Z = ant/post.</summary>
        public Dictionary<string, double[]> Shifts = new Dictionary<string, double[]>();

        public override string ToString()
        {
            return Label + ": " + string.Join("; ", Shifts.Select(kv =>
                string.Format("{0} ({1:+0.0;-0.0;0.0}, {2:+0.0;-0.0;0.0}, {3:+0.0;-0.0;0.0}) cm", kv.Key, kv.Value[0], kv.Value[1], kv.Value[2])));
        }
    }

    /// <summary>
    /// Builds the field-specific perturbation list described in the procedure
    /// (Optimization section, "Create Perturbations ..."):
    ///   - brain fields individually: Y +/-s and Z +/-s           (2 fields x 4 = 8)
    ///   - brain fields paired (RPO+LPO together): Y +/-s          (2)
    ///   - every PA spine field individually: Y +/-s               (2 per field)
    /// </summary>
    public static class PerturbationPlanner
    {
        public static List<FieldPerturbation> Build(IList<string> brainBeamIds, IList<string> spineBeamIds, double shiftCm,
                                                    bool brainIndividualY = true, bool brainIndividualZ = true,
                                                    bool brainPairedY = true, bool spineY = true)
        {
            var list = new List<FieldPerturbation>();
            var signs = new[] { +1.0, -1.0 };

            if (brainPairedY && brainBeamIds.Count > 1)
                foreach (var sg in signs)
                {
                    var p = new FieldPerturbation { Label = string.Format("Brain pair Y{0:+0.0;-0.0}", sg * shiftCm) };
                    foreach (var b in brainBeamIds) p.Shifts[b] = new[] { 0, sg * shiftCm, 0 };
                    list.Add(p);
                }

            if (spineY)
                foreach (var b in spineBeamIds)
                    foreach (var sg in signs)
                        list.Add(Single(b, string.Format("{0} Y{1:+0.0;-0.0}", b, sg * shiftCm), new[] { 0, sg * shiftCm, 0 }));

            if (brainIndividualY)
                foreach (var b in brainBeamIds)
                    foreach (var sg in signs)
                        list.Add(Single(b, string.Format("{0} Y{1:+0.0;-0.0}", b, sg * shiftCm), new[] { 0, sg * shiftCm, 0 }));

            if (brainIndividualZ)
                foreach (var b in brainBeamIds)
                    foreach (var sg in signs)
                        list.Add(Single(b, string.Format("{0} Z{1:+0.0;-0.0}", b, sg * shiftCm), new[] { 0, 0, sg * shiftCm }));

            return list;
        }

        private static FieldPerturbation Single(string beam, string label, double[] shift)
        {
            var p = new FieldPerturbation { Label = label };
            p.Shifts[beam] = shift;
            return p;
        }

        /// <summary>Plan-level scenarios: +/-shift on each axis combined with +/-range error (12 scenarios for the defaults).</summary>
        public static List<double[]> PlanScenarios(double shiftCm, double rangePct)
        {
            var list = new List<double[]>();
            for (int axis = 0; axis < 3; axis++)
                foreach (var sg in new[] { +1.0, -1.0 })
                    foreach (var rg in new[] { +1.0, -1.0 })
                    {
                        var v = new double[4];
                        v[axis] = sg * shiftCm;
                        v[3] = rg * rangePct;
                        list.Add(v);
                    }
            return list;
        }
    }
}
