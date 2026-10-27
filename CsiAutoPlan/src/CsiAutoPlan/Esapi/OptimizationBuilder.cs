using System;
using System.Collections.Generic;
using System.Linq;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    /// <summary>Optimization section of the procedure: NUPO objectives, robust settings,
    /// perturbations, MFO optimization and final dose calculation.</summary>
    public class OptimizationBuilder
    {
        private readonly CsiSession S;
        private RunLog Log { get { return S.Log; } }
        private OptimizationOptions O { get { return S.Cfg.Optimization; } }

        public OptimizationBuilder(CsiSession session) { S = session; }

        // ------------------------------------------------------------------ objectives
        public void SetupObjectives(IonPlanSetup plan)
        {
            Log.Step = "Objectives";
            var os = plan.OptimizationSetup;
            if (O.RemoveExistingObjectives)
                foreach (var o in os.Objectives.ToList()) os.RemoveObjective(o);

            foreach (var p in os.Parameters.OfType<OptimizationNormalTissueParameter>().ToList())
            {
                try { os.RemoveParameter(p); } catch (Exception) { }
            }
            if (O.ProtonNormalTissueObjective) os.AddProtonNormalTissueObjective(100, 5, 100, 60);
            else Log.Info("Normal Tissue Objective: off.");

            double rxGy = S.TotalDoseGy;
            try
            {
                double planRx = CsiSession.ToGy(plan.TotalDose);
                if (!double.IsNaN(planRx) && planRx > 0)
                {
                    if (Math.Abs(planRx - rxGy) > 0.01) Log.Warn("Using the plan's prescription {0:F2} Gy(RBE) for objectives (dialog: {1:F2}).", planRx, rxGy);
                    rxGy = planRx;
                }
            }
            catch (Exception) { }
            var robustManual = new List<string>();
            int robustSet = 0;
            string robustHow = null;
            var caps = S.Caps;
            foreach (var t in S.Cfg.Objectives)
            {
                var structures = Resolve(t.Structure);
                if (structures.Count == 0) { Log.Info("Objective skipped - no structure for '{0}'.", t.Structure); continue; }
                foreach (var s in structures)
                {
                    var dose = Dose(plan, rxGy * t.DosePercentRx / 100.0);
                    bool mean = t.Type == ObjectiveType.Mean;
                    var op = t.Type == ObjectiveType.Lower ? OptimizationObjectiveOperator.Lower : OptimizationObjectiveOperator.Upper;
                    OptimizationObjective obj = null;
                    bool ro = false;
                    string how = null;

                    // RO wanted: create the objective through the API's robust overload if it has one ...
                    if (t.Robust && caps != null)
                    {
                        obj = caps.TryAddRobust(os, mean, mean ? new object[] { s, dose, t.Priority }
                                                              : new object[] { s, op, dose, t.VolumePercent, t.Priority }, out how);
                        ro = obj != null;
                    }
                    if (obj == null)
                        obj = mean ? (OptimizationObjective)os.AddMeanDoseObjective(s, dose, t.Priority)
                                   : os.AddPointObjective(s, op, dose, t.VolumePercent, t.Priority);
                    // ... or tick RO on the objective just created
                    if (t.Robust && !ro && caps != null) ro = caps.TrySetRobust(os, obj, out how);
                    if (ro && caps != null && caps.ReadRobust(obj) == false) { ro = false; Log.Warn("{0} {1}: the RO flag did not stick (read back false).", s.Id, t.Type); }

                    string desc = string.Format("{0} {1} {2:F2} {3} @ {4}% (prio {5})", s.Id, t.Type, dose.Dose, dose.Unit, t.VolumePercent, t.Priority);
                    if (t.Robust)
                    {
                        if (ro) { desc += " [RO]"; robustSet++; robustHow = robustHow ?? how; }
                        else robustManual.Add(s.Id + " " + t.Type);
                    }
                    Log.Info("Objective: " + desc);
                }
            }
            if (robustSet > 0)
                Log.Info("RO (robust) ticked automatically on {0} objective(s) via {1}{2}.", robustSet, robustHow,
                         caps != null && caps.ObjectiveRobustGetter != null ? " (verified by read-back)" : "");
            if (caps != null && caps.RobustFailure != null)
                Log.Warn("Setting RO through the API failed ({0}); the remaining objectives are on the checklist.", caps.RobustFailure);
            if (robustManual.Count > 0)
                Log.Manual("NUPO window (Optimization > objectives): tick the RO (robust) box ONLY for: {0}. Leave OAR objectives non-robust.", string.Join(", ", robustManual.Distinct()));
            else if (robustSet > 0)
                Log.Manual("NUPO window: confirm the RO box is ticked on the target OPT objectives only (first runs with automatic RO).");
            Log.Manual("Add/adjust OAR objectives per the EBRT Order (eyes, lenses, cochleae, other listed OARs) - not robust.");
        }

        /// <summary>Dose value in the unit the plan uses (Eclipse can be configured for Gy or cGy).</summary>
        private static DoseValue Dose(PlanSetup plan, double gy)
        {
            DoseValue.DoseUnit unit = DoseValue.DoseUnit.Gy;
            try { unit = plan.TotalDose.Unit; } catch (Exception) { }
            return unit == DoseValue.DoseUnit.cGy ? new DoseValue(gy * 100, DoseValue.DoseUnit.cGy) : new DoseValue(gy, DoseValue.DoseUnit.Gy);
        }

        private List<Structure> Resolve(string token)
        {
            // Tokens -> structures by their Role in StructureRecipes.cs; anything else is a structure ID ('|' = alternatives).
            var ss = S.SS;
            var list = new List<Structure>();
            Action<string> add = id => { if (id == null) return; var s = StructureTools.Find(ss, id); if (s != null && !list.Contains(s)) list.Add(s); };
            switch ((token ?? "").Trim().ToUpperInvariant())
            {
                case "@BR_OPT": add(RecipeBook.Id(Role.FieldOpt, "BR")); break;
                case "@POST_OPT": add(RecipeBook.Id(Role.FieldOpt, "POST")); break;
                case "@SPN_OPT":
                    if (S.Geometry != null)
                        foreach (var seg in S.Geometry.Spine) add(RecipeBook.Id(Role.FieldOpt, seg.Name));
                    if (list.Count == 0) add(RecipeBook.Id(Role.SpineOpt));
                    break;
                case "@VB_OPT": add(RecipeBook.Id(Role.VbOpt)); break;
                case "@BR_COMPO": add(RecipeBook.Id(Role.FieldTarget, "BR")); break;
                case "@CTV":
                    if (!S.UseDraftUnion && S.TargetCtv != null) list.Add(S.TargetCtv); else add(RecipeBook.Id(Role.DraftUnion));
                    break;
                default: var s0 = StructureTools.Find(ss, token); if (s0 != null) list.Add(s0); break;
            }
            return list;
        }

        // ------------------------------------------------------------------ uncertainties
        public void SetupUncertainties(IonPlanSetup plan)
        {
            Log.Step = "Perturbations";
            List<string> brainIds, spineIds;
            ClassifyBeams(plan, out brainIds, out spineIds);

            if (O.PlanUncertainties)
            {
                if (plan.PlanUncertainties.Any())
                    Log.Warn("Plan already has {0} uncertainty scenarios - not adding duplicates.", plan.PlanUncertainties.Count());
                else
                {
                    double unit = string.Equals(O.UncertaintyShiftApiUnit, "mm", StringComparison.OrdinalIgnoreCase) ? 10 : 1;
                    foreach (var sc in PerturbationPlanner.PlanScenarios(O.PlanIsoShiftCm, O.PlanRangeErrorPercent))
                    {
                        // Scenario vector is in Eclipse user axes (X L/R, Y sup/inf, Z ant/post); convert to DICOM axes for HFS.
                        var shift = new VVector(sc[0] * unit, -sc[2] * unit, sc[1] * unit);
                        plan.AddPlanUncertaintyWithParameters(PlanUncertaintyType.RobustOptimizationUncertainty, true, sc[3], shift);
                    }
                    foreach (var u in plan.PlanUncertainties)
                        Log.Info("Uncertainty '{0}': shift ({1:F2}, {2:F2}, {3:F2}), curve error {4:F2}%",
                                 u.DisplayName, u.IsocenterShift.x, u.IsocenterShift.y, u.IsocenterShift.z, u.CalibrationCurveError);
                    Log.Manual("First run on a test patient: open Plan Uncertainty Parameters and confirm the shifts read {0} cm and range error {1}% (UncertaintyShiftApiUnit = {2}).",
                               O.PlanIsoShiftCm, O.PlanRangeErrorPercent, O.UncertaintyShiftApiUnit);
                }
            }

            var perts = PerturbationPlanner.Build(brainIds, spineIds, O.FieldShiftCm, O.FieldBrainIndividualY, O.FieldBrainIndividualZ,
                                                  O.FieldBrainPairedY, O.FieldSpineY);
            if (perts.Count > 0)
            {
                Log.Manual("Plan/Field Uncertainty Parameters: add these {0} field-specific scenarios ({1} cm iso shift), then re-run the plugin with only 'Optimize & calculate' ticked:", perts.Count, O.FieldShiftCm);
                int i = 1;
                foreach (var p in perts) Log.Manual("   F{0:00}  {1}", i++, p);
            }
        }

        /// <summary>Brain fields share the most superior isocenter; all others are spine fields.</summary>
        /// <summary>Lateral brain fields (RPO/LPO: brain iso, gantry away from 180) get Y and Z perturbations;
        /// every posterior field - including a POST field at the brain iso - gets Y only (procedure).</summary>
        public static void ClassifyBeams(IonPlanSetup plan, out List<string> brain, out List<string> spine)
        {
            var beams = plan.IonBeams.Where(b => !b.IsSetupField).ToList();
            brain = new List<string>(); spine = new List<string>();
            if (beams.Count == 0) return;
            double topZ = beams.Max(b => b.IsocenterPosition.z);
            foreach (var b in beams)
            {
                double g = b.ControlPoints.Count > 0 ? b.ControlPoints[0].GantryAngle : 0;
                bool posterior = Math.Abs(g - 180) < 30;
                (Math.Abs(b.IsocenterPosition.z - topZ) < 1.0 && !posterior ? brain : spine).Add(b.Id);
            }
        }

        // ------------------------------------------------------------------ optimize & calculate
        public bool OptimizeAndCalculate(IonPlanSetup plan)
        {
            Log.Step = "Optimize";
            var optModel = plan.GetCalculationModel(CalculationType.ProtonOptimization);
            if (!string.IsNullOrEmpty(S.Cfg.Plan.MaxOptimizationTimeOptionKey) && !string.IsNullOrEmpty(optModel))
            {
                if (plan.SetCalculationOption(optModel, S.Cfg.Plan.MaxOptimizationTimeOptionKey, S.Cfg.Plan.MaxOptimizationTimeValue))
                    Log.Info("Max optimization time option set: {0} = {1}", S.Cfg.Plan.MaxOptimizationTimeOptionKey, S.Cfg.Plan.MaxOptimizationTimeValue);
                else Log.Warn("Optimizer rejected option '{0}'.", S.Cfg.Plan.MaxOptimizationTimeOptionKey);
            }

            var bl = plan.CalculateBeamLine();
            if (!bl.Success) { Log.Error("Beam line calculation failed."); return false; }
            Log.Info("Beam line calculated.");

            var started = DateTime.Now;
            var opt = plan.OptimizeIMPT(new OptimizationOptionsIMPT(O.MaxIterations, OptimizationOption.RestartOptimization));
            if (!opt.Success) { Log.Error("NUPO optimization failed."); return false; }
            Log.Info("Optimization done: {0} iterations, objective {1:G4}, {2:F1} min.", opt.NumberOfIMRTOptimizerIterations, opt.TotalObjectiveFunctionValue, (DateTime.Now - started).TotalMinutes);

            Log.Step = "Dose";
            var d = plan.PostProcessAndCalculateDose();
            if (!d.Success) { Log.Error("Dose calculation failed."); return false; }
            Log.Info("Dose calculated.");

            if (O.CalculateUncertaintyDoses)
            {
                var u = plan.CalculatePlanUncertaintyDoses();
                if (!u.Success) Log.Warn("Uncertainty dose calculation failed.");
                else Log.Info("Uncertainty doses calculated.");
            }
            ReportDose(plan);
            Log.Manual("Evaluate field-specific doses: smooth gradient through each junction, reasonably uniform brain fields.");
            Log.Manual("If coverage / hot spots / spot MU are poor: adjust the OPT / field-target margins or recipes in StructureRecipes.cs (e.g. SplitSpineOptPerField), or decrease spot spacing; re-optimize.");
            return true;
        }

        private void ReportDose(IonPlanSetup plan)
        {
            if (plan.Dose == null) return;
            // OPTs, field targets, then any other structure StructureRecipes.cs creates in step C (e.g. one you added)
            var segs = S.Geometry != null ? S.Geometry.All().Select(g => g.Name).ToList() : new List<string>();
            var ids = new List<string>();
            ids.AddRange(segs.Select(g => RecipeBook.Id(Role.FieldOpt, g)));
            ids.Add(RecipeBook.Id(Role.SpineOpt));
            ids.Add(RecipeBook.Id(Role.VbOpt));
            ids.AddRange(segs.Select(g => RecipeBook.Id(Role.FieldTarget, g)));
            foreach (var r in RecipeBook.All.Where(x => x.Saved && x.Step == Step.C && x.Role == Role.None))
                ids.AddRange(r.PerField ? segs.Select(g => r.IdFor(RecipeBook.FieldCode(g))) : new[] { r.Id });
            ids = ids.Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var structs = new List<Structure>();
            if (!S.UseDraftUnion && S.TargetCtv != null) structs.Add(S.TargetCtv);
            foreach (var id in ids) { var s = StructureTools.Find(S.SS, id); if (s != null) structs.Add(s); }
            foreach (var s in structs)
            {
                try
                {
                    var d98 = plan.GetDoseAtVolume(s, 98, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                    var d2 = plan.GetDoseAtVolume(s, 2, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                    var dvh = plan.GetDVHCumulativeData(s, DoseValuePresentation.Absolute, VolumePresentation.Relative, 0.01);
                    Log.Info("{0,-17} D98 {1:F2}  D2 {2:F2}  Dmean {3:F2}  Dmax {4:F2} {5}", s.Id, d98.Dose, d2.Dose,
                             dvh != null ? dvh.MeanDose.Dose : double.NaN, dvh != null ? dvh.MaxDose.Dose : double.NaN, d98.Unit);
                }
                catch (Exception ex) { Log.Warn("DVH for {0} failed: {1}", s.Id, ex.Message); }
            }
        }
    }
}
