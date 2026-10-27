using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    /// <summary>What the existing proton plans of the open patient use (IDs Eclipse is known to accept).</summary>
    public class HarvestResult
    {
        public string Report = "";
        /// <summary>Per treatment unit (MachineId): technique, tolerance, snout, range shifter, patient support of existing plans.</summary>
        public Dictionary<string, MachineOption> Machines = new Dictionary<string, MachineOption>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Patient support device IDs of all existing proton plans, most frequent first.</summary>
        public List<string> SupportDevices = new List<string>();
        /// <summary>Calculation models of the existing plans (distinct, in plan order).</summary>
        public List<string> VolumeDoseModels = new List<string>(), OptimizationModels = new List<string>(),
                            BeamLineModels = new List<string>(), PostProcessingModels = new List<string>();
    }

    /// <summary>
    /// Read-only: reads machine / snout / range shifter / tolerance / patient-support IDs, calculation models,
    /// calculation-option keys and the API's units from the existing (clinically made) proton plans of the open
    /// patient. Used as a fallback when Eclipse rejects an ID from SiteSettings.cs, and listed in tab 5 as lines
    /// that can be pasted into SiteSettings.cs. Nothing is written.
    /// </summary>
    public static class ExistingPlanReader
    {
        private static readonly Regex SpotKey = new Regex(@"spac|overwrite|override", RegexOptions.IgnoreCase);
        private static readonly Regex GridKey = new Regex(@"grid", RegexOptions.IgnoreCase);
        private static readonly Regex TimeKey = new Regex(@"time", RegexOptions.IgnoreCase);

        public static HarvestResult Read(ScriptContext ctx, CsiSettings current, RunLog log)
        {
            var result = new HarvestResult();
            var sb = new StringBuilder();
            var plans = new List<IonPlanSetup>();
            if (ctx.IonPlanSetup != null) plans.Add(ctx.IonPlanSetup);
            foreach (var c in ctx.Patient.Courses)
                foreach (var p in c.IonPlanSetups)
                    if (!plans.Contains(p)) plans.Add(p);
            plans = plans.Where(p => p.IonBeams.Any(b => !b.IsSetupField)).Take(20).ToList();
            if (plans.Count == 0)
            {
                sb.AppendLine("Settings harvest: this patient has no proton plan with treatment fields. Open a patient with a");
                sb.AppendLine("clinically made PBS plan (ideally a CSI plan) to read machine / snout / RS / tolerance / couch IDs automatically.");
                result.Report = sb.ToString();
                return result;
            }

            var cfg = current;
            var machines = new Dictionary<string, MachineOption>(StringComparer.OrdinalIgnoreCase);
            var snoutValues = new List<double>();
            var airGaps = new List<double>();
            var shifts = new List<double>();
            string volModel = null, optModel = null, blmModel = null, ppModel = null;
            var spotOptions = new Dictionary<string, string>();
            string gridKey = null;
            var timeKeys = new List<string>();
            var psdCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            sb.AppendLine("=== Settings read from existing proton plans (read-only) ===");
            foreach (var plan in plans)
            {
                string psd = Safe(() => plan.PatientSupportDevice != null ? plan.PatientSupportDevice.Id : null);
                string psdType = Safe(() => plan.PatientSupportDevice != null ? plan.PatientSupportDevice.PatientSupportDeviceType : null);
                string psdCode = Safe(() => plan.PatientSupportDevice != null ? plan.PatientSupportDevice.PatientSupportAccessoryCode : null);
                sb.AppendLine(string.Format("Plan {0}/{1}: patient support '{2}' (type '{3}', accessory code '{4}'), {5} uncertainty scenario(s)",
                                            plan.Course.Id, plan.Id, psd, psdType, psdCode, plan.PlanUncertainties.Count()));
                if (!string.IsNullOrWhiteSpace(psd)) { int n; psdCount.TryGetValue(psd, out n); psdCount[psd] = n + 1; }

                string vm = Safe(() => plan.GetCalculationModel(CalculationType.ProtonVolumeDose));
                string om = Safe(() => plan.GetCalculationModel(CalculationType.ProtonOptimization));
                string bm = Safe(() => plan.GetCalculationModel(CalculationType.ProtonBeamLineModifiers));
                string pm = Safe(() => plan.GetCalculationModel(CalculationType.ProtonMSPostProcessing));
                sb.AppendLine(string.Format("  models: dose '{0}', optimizer '{1}', beam line '{2}', post-processing '{3}'", vm, om, bm, pm));
                AddDistinct(result.VolumeDoseModels, vm); AddDistinct(result.OptimizationModels, om);
                AddDistinct(result.BeamLineModels, bm); AddDistinct(result.PostProcessingModels, pm);
                volModel = volModel ?? NullIfEmpty(vm);
                if (optModel == null || (optModel.IndexOf("NUPO", StringComparison.OrdinalIgnoreCase) < 0 && (om ?? "").IndexOf("NUPO", StringComparison.OrdinalIgnoreCase) >= 0))
                    optModel = NullIfEmpty(om) ?? optModel;
                blmModel = blmModel ?? NullIfEmpty(bm); ppModel = ppModel ?? NullIfEmpty(pm);

                foreach (var kv in Options(plan, bm).Where(kv => SpotKey.IsMatch(kv.Key)))
                {
                    sb.AppendLine(string.Format("  beam-line option {0} = {1}", kv.Key, kv.Value));
                    if (!spotOptions.ContainsKey(kv.Key)) spotOptions[kv.Key] = kv.Value;
                }
                foreach (var kv in Options(plan, vm).Where(kv => GridKey.IsMatch(kv.Key)))
                {
                    sb.AppendLine(string.Format("  dose option {0} = {1}", kv.Key, kv.Value));
                    gridKey = gridKey ?? kv.Key;
                }
                foreach (var kv in Options(plan, om).Where(kv => TimeKey.IsMatch(kv.Key)))
                {
                    sb.AppendLine(string.Format("  optimizer option {0} = {1}", kv.Key, kv.Value));
                    if (!timeKeys.Contains(kv.Key)) timeKeys.Add(kv.Key);
                }

                foreach (var u in plan.PlanUncertainties)
                {
                    var v = u.IsocenterShift;
                    double m = Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
                    if (m > 0 && !double.IsNaN(m)) shifts.Add(m);
                }

                foreach (var b in plan.IonBeams.Where(x => !x.IsSetupField))
                {
                    try
                    {
                        string unit = Safe(() => b.TreatmentUnit != null ? b.TreatmentUnit.Id : null);
                        string tech = Safe(() => b.Technique != null ? b.Technique.Id : null);
                        string tol = Safe(() => b.ToleranceTableLabel);
                        string rs = Safe(() => string.Join(",", b.RangeShifters.Select(r => r.Id)));
                        double gantry = b.ControlPoints.Count > 0 ? b.ControlPoints[0].GantryAngle : double.NaN;
                        double snout = b.SnoutPosition, gap = b.AirGap;
                        if (!double.IsNaN(snout) && snout > 0) snoutValues.Add(snout);
                        if (!double.IsNaN(gap) && gap > 0) airGaps.Add(gap);
                        string target = Safe(() => b.TargetStructure != null ? b.TargetStructure.Id : null);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  field {0,-16} unit '{1}' tech '{2}' tol '{3}' snout '{4}' pos {5:0.##} airgap {6:0.##} RS '{7}' gantry {8:0.#} target '{9}' lat {10:0.#} prox {11:0.#} dist {12:0.#}{13}",
                            b.Id, unit, tech, tol, b.SnoutId, snout, gap, rs, gantry, target,
                            b.LateralMargins.X1, b.ProximalTargetMargin, b.DistalTargetMargin, RangeNote(b, cfg.RangeMargins)));

                        if (string.IsNullOrEmpty(unit)) continue;
                        MachineOption mo;
                        if (!machines.TryGetValue(unit, out mo))
                        {
                            var known = cfg.Machines.FirstOrDefault(m => string.Equals(m.MachineId, unit, StringComparison.OrdinalIgnoreCase));
                            mo = new MachineOption
                            {
                                DisplayName = known != null ? known.DisplayName : unit,
                                MachineId = unit,
                                HalfFieldLimitMm = known != null ? known.HalfFieldLimitMm : 200,
                                Enabled = known == null || known.Enabled,
                                SpineScheme = known != null ? known.SpineScheme : SpineSchemes.PostBrain,
                                SnoutToRangeShifterMm = known != null ? known.SnoutToRangeShifterMm : 0,
                                SnoutSizeXcm = known != null ? known.SnoutSizeXcm : 37,
                                SnoutSizeYcm = known != null ? known.SnoutSizeYcm : 45,
                                InitialSnoutPosition = known != null ? known.InitialSnoutPosition : 30,
                                TechniqueId = "",
                            };
                            machines[unit] = mo;
                        }
                        mo.TechniqueId = First(Empty(mo.TechniqueId), tech);
                        mo.ToleranceId = First(Empty(mo.ToleranceId), tol);
                        mo.SnoutId = First(Empty(mo.SnoutId), b.SnoutId);
                        mo.RangeShifterId = First(Empty(mo.RangeShifterId), rs != null ? rs.Split(',').FirstOrDefault(x => x.Length > 0) : null);
                        mo.PatientSupportDeviceId = First(Empty(mo.PatientSupportDeviceId), psd);
                        if (!double.IsNaN(snout) && snout > 0) mo.InitialSnoutPosition = snout;
                    }
                    catch (Exception ex) { sb.AppendLine("  field " + b.Id + ": could not read (" + ex.Message + ")"); }
                }
            }

            // ---- unit inference
            sb.AppendLine();
            string snoutUnit = null, shiftUnit = null;
            if (snoutValues.Count > 0)
            {
                double med = Median(snoutValues);
                snoutUnit = med < 100 ? "cm" : "mm";
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Snout positions reported as {0:0.##} (median) -> API snout unit looks like '{1}'.", med, snoutUnit));
            }
            if (airGaps.Count > 0)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Air gaps reported as {0:0.##} (median). Compare with Field Properties in Eclipse: if those fields show 5.0 cm, the API reports air gap in {1}.",
                    Median(airGaps), Median(airGaps) > 20 ? "mm" : "cm"));
            if (shifts.Count > 0)
            {
                double med = Median(shifts);
                shiftUnit = med < 1.0 ? "cm" : "mm";
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Uncertainty iso shifts reported as {0:0.###} (median) -> if the clinical value is 0.3 cm, the API uses '{1}'.", med, shiftUnit));
            }

            // ---- the same as lines for SiteSettings.cs (nothing is changed or written)
            foreach (var mo in machines.Values)
                if (string.IsNullOrWhiteSpace(mo.TechniqueId)) mo.TechniqueId = "MODULAT_SCANNING";
            if (timeKeys.Count > 0) sb.AppendLine("Optimizer option(s) that may hold the time limit: " + string.Join(", ", timeKeys) + " (not set automatically).");
            sb.AppendLine();
            sb.AppendLine("--- As lines for SiteSettings.cs (check before copying; HalfFieldLimitMm / SpineScheme / snout values of rooms");
            sb.AppendLine("--- not in SiteSettings.cs yet are placeholders) ---");
            sb.AppendLine("Machines:");
            foreach (var mo in machines.Values) sb.AppendLine("    " + mo.ToCSharp());
            Func<string, string> q = v => "\"" + (v ?? "") + "\"";
            if (volModel != null || optModel != null || blmModel != null || ppModel != null)
                sb.AppendLine(string.Format("Plan: VolumeDoseModel = {0}, OptimizationModel = {1}, BeamLineModifierModel = {2}, PostProcessingModel = {3},",
                                            q(volModel), q(optModel), q(blmModel), q(ppModel)));
            if (gridKey != null) sb.AppendLine("Plan: DoseGridOptionKey = " + q(gridKey) + ",");
            if (spotOptions.Count > 0)
            {
                sb.AppendLine("Plan: SpotSpacingOptions (values of the first plan above - the procedure uses overwrite = True, 0.5 cm):");
                foreach (var kv in spotOptions) sb.AppendLine("    new CalcOption { Key = " + q(kv.Key) + ", Value = " + q(kv.Value) + " },");
            }
            if (snoutUnit != null) sb.AppendLine("Beams: SnoutPositionApiUnit = " + q(snoutUnit) + ",");
            if (shiftUnit != null) sb.AppendLine("Optimization: UncertaintyShiftApiUnit = " + q(shiftUnit) + ",");

            log.Info("Read settings from {0} proton plan(s); machines: {1}.", plans.Count, string.Join(", ", machines.Keys));
            foreach (var kv in machines) result.Machines[kv.Key] = kv.Value;
            result.SupportDevices = psdCount.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            result.Report = sb.ToString();
            return result;
        }

        /// <summary>Eclipse's nominal range / SOBP of an existing field and the margins the PBSTV margin sheet would give
        /// (to compare with the margins that plan used).</summary>
        private static string RangeNote(IonBeam b, RangeMarginOptions r)
        {
            double range, sobp;
            try { range = b.NominalRange; sobp = b.NominalSOBPWidth; } catch (Exception) { return ""; }
            string why;
            if (!RangeMarginCalculator.IsUsable(range, sobp, out why)) return " | R/SOBP: " + why;
            return string.Format(CultureInfo.InvariantCulture, " | R {0:0.00} SOBP {1:0.00} cm -> sheet distal {2:0.00} proximal {3:0.00} cm",
                                 range / 10, sobp / 10,
                                 RangeMarginCalculator.DistalMm(range, r.RangeUncertaintyPercent, r.AddedMarginMm, r.RoundUpToMm) / 10,
                                 RangeMarginCalculator.ProximalMm(range, sobp, r.RangeUncertaintyPercent, r.AddedMarginMm, r.RoundUpToMm) / 10);
        }

        private static Dictionary<string, string> Options(PlanSetup plan, string model)
        {
            if (string.IsNullOrEmpty(model)) return new Dictionary<string, string>();
            try { return plan.GetCalculationOptions(model) ?? new Dictionary<string, string>(); }
            catch (Exception) { return new Dictionary<string, string>(); }
        }

        private static void AddDistinct(List<string> list, string v)
        {
            if (!string.IsNullOrWhiteSpace(v) && !list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
        }

        private static string Safe(Func<string> f) { try { return f(); } catch (Exception) { return null; } }
        private static string NullIfEmpty(string s) { return string.IsNullOrWhiteSpace(s) ? null : s; }
        private static string Empty(string s) { return string.IsNullOrWhiteSpace(s) ? null : s; }
        private static string First(params string[] values) { return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? ""; }
        private static double Median(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    }
}
