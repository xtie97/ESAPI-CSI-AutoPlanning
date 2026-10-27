using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    /// <summary>Plan Setup and Field Setup sections of the procedure.</summary>
    public class PlanBuilder
    {
        private readonly CsiSession S;
        private RunLog Log { get { return S.Log; } }
        private BeamOptions B { get { return S.Cfg.Beams; } }
        private MachineOption M { get { return S.Machine; } }

        public PlanBuilder(CsiSession session) { S = session; }

        // ------------------------------------------------------------------ plan
        public IonPlanSetup CreatePlan()
        {
            Log.Step = "Plan setup";
            var supportIds = SupportDeviceCandidates();
            if (supportIds.Count == 0)
            {
                Log.Error("Machine '{0}' has no PatientSupportDeviceId in SiteSettings.cs and this patient has no proton plan to copy it from.", M.DisplayName);
                return null;
            }

            bool createdCourse = false;
            var course = S.Patient.Courses.FirstOrDefault(c => string.Equals(c.Id, S.CourseId, StringComparison.OrdinalIgnoreCase));
            if (course == null)
            {
                if (!S.Patient.CanAddCourse()) { Log.Error("Eclipse does not allow adding a course."); return null; }
                course = S.Patient.AddCourse();
                course.Id = S.CourseId;
                createdCourse = true;
                Log.Info("Created course {0}.", S.CourseId);
            }
            if (course.PlanSetups.Any(p => string.Equals(p.Id, S.PlanId, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Error("Course {0} already has a plan '{1}'. Choose another plan ID, or untick 'Create plan & fields' to work on the existing plan.", course.Id, S.PlanId);
                return null;
            }
            if (!course.CanAddPlanSetup(S.SS)) { Log.Error("Cannot add a plan to course {0} for structure set {1}.", course.Id, S.SS.Id); return null; }

            IonPlanSetup plan = null;
            var rejected = new List<string>();
            foreach (var cand in supportIds)
            {
                try { plan = course.AddIonPlanSetup(S.SS, cand.Key); }
                catch (ArgumentException ex) { rejected.Add(string.Format("'{0}' ({1})", cand.Key, ex.Message)); continue; }
                if (!string.Equals(cand.Key, M.PatientSupportDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Eclipse rejected patient support device {0}; used '{1}' ({2}). Set PatientSupportDeviceId=\"{1}\" for {3} in {4}.",
                             string.Join(", ", rejected), cand.Key, cand.Value, M.DisplayName, "SiteSettings.cs");
                    M.PatientSupportDeviceId = cand.Key;
                }
                else Log.Info("Patient support device '{0}'.", cand.Key);
                break;
            }
            if (plan == null)
            {
                Log.Error("Eclipse rejected every patient support device ID tried: {0}. In Eclipse open any proton plan of {1}, read the patient support " +
                          "device in Plan Properties (or run the plugin on a patient that has one, see tab 5) and put that ID in {2}.",
                          string.Join(", ", rejected), M.DisplayName, "SiteSettings.cs");
                if (createdCourse && !course.PlanSetups.Any())
                {
                    try
                    {
                        if (S.Patient.CanRemoveCourse(course)) { S.Patient.RemoveCourse(course); Log.Info("Removed the empty course {0} created by this run.", S.CourseId); }
                    }
                    catch (Exception ex) { Log.Info("Empty course {0} could not be removed ({1}) - delete it or close the patient without saving.", S.CourseId, ex.Message); }
                }
                return null;
            }
            plan.Id = S.PlanId;
            Log.Info("Created ion plan {0}/{1} on {2}.", course.Id, plan.Id, S.SS.Id);

            var draftId = RecipeBook.Id(Role.DraftUnion);
            var target = S.UseDraftUnion ? (draftId != null ? StructureTools.Find(S.SS, draftId) : null) : S.TargetCtv;
            if (target != null)
            {
                var hint = new StringBuilder();
                if (!plan.SetTargetStructureIfNoDose(target, hint)) Log.Warn("Could not set plan target {0}: {1}", target.Id, hint);
            }

            try { plan.SetPrescription(S.Fractions, new DoseValue(S.DosePerFractionGy, DoseValue.DoseUnit.Gy), 1.0); }
            catch (Exception)   // systems configured for cGy reject Gy dose values
            { plan.SetPrescription(S.Fractions, new DoseValue(S.DosePerFractionGy * 100, DoseValue.DoseUnit.cGy), 1.0); }
            Log.Info("Prescription {0} x {1:F2} Gy(RBE) = {2:F2} Gy(RBE).", S.Fractions, S.DosePerFractionGy, S.TotalDoseGy);
            Log.Manual("Verify the prescription, primary reference point and treatment intent against the EBRT Order.");

            SetCalculationModels(plan);
            plan.SetOptimizationMode(IonPlanOptimizationMode.MultiFieldOptimization);
            Log.Info("Optimization mode: Multi-field (MFO).");
            return plan;
        }

        /// <summary>
        /// Patient support device IDs to try, with where each came from: SiteSettings.cs, the device of existing plans
        /// on the same room, other existing proton plans, then each of those without a "&lt;MachineId&gt;-" prefix.
        /// DICOM exports carry e.g. "Proton_3-Table" as Patient Support ID, which AddIonPlanSetup rejected on
        /// Proton 3; the Eclipse device ID is presumably the part after the machine prefix ("Table", as in
        /// Varian's ProtonFeaturesDemo).
        /// </summary>
        public List<KeyValuePair<string, string>> SupportDeviceCandidates()
        {
            var list = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (v, src) =>
            {
                if (string.IsNullOrWhiteSpace(v)) return;
                v = v.Trim();
                if (!list.Any(kv => string.Equals(kv.Key, v, StringComparison.OrdinalIgnoreCase))) list.Add(new KeyValuePair<string, string>(v, src));
            };
            add(M.PatientSupportDeviceId, "SiteSettings.cs");
            var pm = S.PlanMachine(M);
            if (pm != null) add(pm.PatientSupportDeviceId, "device of this patient's existing " + M.MachineId + " plan(s)");
            if (S.PlanSettings != null) foreach (var d in S.PlanSettings.SupportDevices) add(d, "device of this patient's existing proton plan(s)");
            foreach (var kv in list.ToList())
            {
                var stripped = StripMachinePrefix(kv.Key);
                if (stripped != null) add(stripped, "'" + kv.Key + "' without the machine prefix");
            }
            return list;
        }

        private string StripMachinePrefix(string id)
        {
            foreach (var sep in new[] { "-", "_", " " })
            {
                var prefix = M.MachineId + sep;
                if (id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return id.Substring(prefix.Length).Trim();
            }
            return null;
        }

        private void SetCalculationModels(IonPlanSetup plan)
        {
            var P = S.Cfg.Plan;
            var H = S.PlanSettings;
            // SiteSettings.cs first; if empty, the model this patient's existing proton plans use (optimizer: a NUPO model if any).
            TrySetModel(plan, CalculationType.ProtonVolumeDose, P.VolumeDoseModel, H != null ? H.VolumeDoseModels : null, null);
            TrySetModel(plan, CalculationType.ProtonOptimization, P.OptimizationModel, H != null ? H.OptimizationModels : null, "NUPO");
            TrySetModel(plan, CalculationType.ProtonBeamLineModifiers, P.BeamLineModifierModel, H != null ? H.BeamLineModels : null, null);
            TrySetModel(plan, CalculationType.ProtonMSPostProcessing, P.PostProcessingModel, H != null ? H.PostProcessingModels : null, null);

            var dose = plan.GetCalculationModel(CalculationType.ProtonVolumeDose);
            var opt = plan.GetCalculationModel(CalculationType.ProtonOptimization);
            var blm = plan.GetCalculationModel(CalculationType.ProtonBeamLineModifiers);
            Log.Info("Models: dose='{0}', optimizer='{1}', beam line='{2}'.", dose, opt, blm);
            if (string.IsNullOrEmpty(opt) || opt.IndexOf("NUPO", StringComparison.OrdinalIgnoreCase) < 0)
                Log.Manual("Optimization algorithm should be NUPO_##### (currently '{0}'): set Plan.OptimizationModel in SiteSettings.cs, " +
                           "or run on a patient whose existing proton plan uses NUPO.", opt);

            // Dose grid 0.25 cm
            string grid = P.DoseGridCm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(dose) && SetOption(plan, dose, P.DoseGridOptionKey, grid))
                Log.Info("Dose grid set to {0} cm.", P.DoseGridCm);
            else
                Log.Manual("Set the dose calculation grid to {0} cm (option '{1}' not accepted by model '{2}').", P.DoseGridCm, P.DoseGridOptionKey, dose);

            // Spot spacing overrides on the beam-line modifier model
            if (!string.IsNullOrEmpty(blm))
            {
                if (P.SpotSpacingOptions.Count > 0)
                {
                    bool all = true;
                    foreach (var o in P.SpotSpacingOptions)
                    {
                        if (SetOption(plan, blm, o.Key, o.Value)) Log.Info("Calc option {0} = {1}", o.Key, o.Value);
                        else { all = false; Log.Error("Calc option '{0}' was rejected by model '{1}'.", o.Key, blm); }
                    }
                    if (!all) LogModulatedScanningOptions(plan, blm, "Beam-line model");
                }
                else
                {
                    LogModulatedScanningOptions(plan, blm, "Beam-line model");
                    Log.Manual("Calculation Options: set 'Overwrite spot spacing in scanning direction' and 'between scanning lines' = True, spacing {0} cm.", P.SpotSpacingCm);
                }
            }
            if (!string.IsNullOrEmpty(opt) && !string.Equals(opt, blm, StringComparison.OrdinalIgnoreCase))
                LogModulatedScanningOptions(plan, opt, "Optimizer");
        }

        private static Dictionary<string, string> SafeOptions(PlanSetup plan, string model)
        {
            try { return plan.GetCalculationOptions(model) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        private void TrySetModel(IonPlanSetup plan, CalculationType t, string model, List<string> fromPlans, string prefer)
        {
            string source = "SiteSettings.cs";
            if (string.IsNullOrWhiteSpace(model) && fromPlans != null && fromPlans.Count > 0)
            {
                model = (prefer != null ? fromPlans.FirstOrDefault(m => m.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0) : null) ?? fromPlans[0];
                source = "this patient's existing proton plan(s)";
            }
            if (string.IsNullOrWhiteSpace(model)) return;
            try
            {
                plan.SetCalculationModel(t, model);
                if (source != "SiteSettings.cs") Log.Info("{0} model '{1}' from {2} (none in SiteSettings.cs).", t, model, source);
            }
            catch (Exception ex) { Log.Error("Could not set {0} model '{1}': {2}", t, model, ex.Message); }
        }

        /// <summary>
        /// Sets a calculation option. The key is matched against the model's own keys: exactly, or - for a key
        /// without "Group/" prefix - by a unique ".../key" suffix (e.g. CalculationGridSizeInCM -> General/CalculationGridSizeInCM).
        /// </summary>
        private bool SetOption(IonPlanSetup plan, string model, string key, string value)
        {
            var keys = SafeOptions(plan, model).Keys.ToList();
            string resolved = keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (resolved == null)
            {
                var hits = keys.Where(k => k.EndsWith("/" + key.TrimStart('/'), StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 1) resolved = hits[0];
            }
            try { return plan.SetCalculationOption(model, resolved ?? key, value); }
            catch (Exception) { return false; }
        }

        /// <summary>Lists the model's general and modulated-scanning options (not the long scattering/compensator list).</summary>
        private void LogModulatedScanningOptions(IonPlanSetup plan, string model, string label)
        {
            var keys = SafeOptions(plan, model).Where(kv => !kv.Key.StartsWith("ScatteringUniformScanning", StringComparison.OrdinalIgnoreCase));
            Log.Info("{0} '{1}' options: {2}", label, model, string.Join(" | ", keys.Select(kv => kv.Key + "=" + kv.Value)));
        }

        // ------------------------------------------------------------------ fields
        public bool CreateFields(IonPlanSetup plan)
        {
            Log.Step = "Fields";
            var brainIso = S.BrainIso;
            Log.Info("Brain iso (DICOM mm): ({0:F1}, {1:F1}, {2:F1}) - {3}", brainIso.x, brainIso.y, brainIso.z, S.UseUserOrigin ? "user origin" : "computed");

            // Brain fields: RPO / LPO at the brain isocenter (+ POST at the brain iso, see the room's SpineScheme)
            var brTarget = FieldTarget("BR");
            if (brTarget == null) { Log.Error("Brain field target missing - run 'Field targets' first."); return false; }
            int fieldNo = 1;
            AddField(plan, B.BrainRpoId, B.BrainRpoGantry, brainIso, brTarget, B.BrainLateralMarginMm, B.BrainProximalMarginMm, B.BrainDistalMarginMm, B.LateralAirGapCm); fieldNo++;
            AddField(plan, B.BrainLpoId, B.BrainLpoGantry, brainIso, brTarget, B.BrainLateralMarginMm, B.BrainProximalMarginMm, B.BrainDistalMarginMm, B.LateralAirGapCm); fieldNo++;
            CheckFieldLimit(brTarget, brainIso.z, "brain");
            if (S.Geometry.Post != null)
            {
                var postTarget = FieldTarget("POST");
                if (postTarget == null) { Log.Error("POST field target missing - run 'Field targets' first."); return false; }
                var id = string.Format("{0:00} {1}", fieldNo++, B.BrainPostId);
                AddField(plan, id, B.PostBrainGantry, brainIso, postTarget, B.SpineLateralMarginMm, B.SpineProximalMarginMm, B.SpineDistalMarginMm, B.PosteriorAirGapCm);
                CheckFieldLimit(postTarget, brainIso.z, id);
            }
            else if (S.AddPostBrainField)
            {
                AddField(plan, string.Format("{0:00} {1}", fieldNo++, B.BrainPostId), B.PostBrainGantry, brainIso, brTarget,
                         B.BrainLateralMarginMm, B.BrainProximalMarginMm, B.BrainDistalMarginMm, B.PosteriorAirGapCm);
            }

            // Field targets left over from an earlier run with a different split would be silently ignored - flag them.
            var current = new HashSet<string>(S.Geometry.All().Select(g => g.Name));
            foreach (var seg in new[] { "POST", "UP", "MID", "MID2", "LO" }.Where(n => !current.Contains(n)))
            {
                var staleId = RecipeBook.Id(Role.FieldTarget, seg);
                var stale = staleId != null ? StructureTools.Find(S.SS, staleId) : null;
                if (stale != null) Log.Warn("{0} exists but is not part of the current split - delete it or re-run 'Field targets'.", stale.Id);
            }

            // Spine PA fields: iso at mid-level of each field target, whole-cm from brain iso, x = 0, common z
            foreach (var seg in S.Geometry.Spine)
            {
                var tgt = FieldTarget(seg.Name);
                if (tgt == null) { Log.Error("Field target for {0} missing - run 'Field targets' first.", seg.Name); return false; }
                double isoZ = IsoFromStructure(tgt, seg);
                var iso = S.SpineIso(isoZ);
                var id = string.Format(B.SpineIdFormat, fieldNo++, SpineSuffix(seg.Name));
                AddField(plan, id, B.SpineGantry, iso, tgt, B.SpineLateralMarginMm, B.SpineProximalMarginMm, B.SpineDistalMarginMm, B.PosteriorAirGapCm);
                Log.Info("{0}: iso offset from brain iso X 0.0, Y {1:+0;-0} cm, Z {2:+0;-0} cm.", id, S.IsoOffsetYcm(isoZ), S.IsoOffsetZcm(iso.y));
                CheckFieldLimit(tgt, isoZ, id);
            }
            Log.Manual("Add setup fields / DRRs and confirm isocenter groups; run the collision script to check air gaps and clearances " +
                       "(clinical example ended at ~20 cm RPO/LPO, ~8 cm POST, ~13 cm POST LOWER).");
            return true;
        }

        private Structure FieldTarget(string seg)
        {
            // field target = the Role.FieldTarget structure of StructureRecipes.cs, or its Role.FieldOpt structure
            // if SiteSettings.cs Beams.FieldTargetIsCompo is false (falls back to the field target if that field has no OPT)
            var compo = RecipeBook.Id(Role.FieldTarget, seg);
            var opt = RecipeBook.Id(Role.FieldOpt, seg);
            Structure s = null;
            if (!B.FieldTargetIsCompo && opt != null) s = StructureTools.Find(S.SS, opt);
            return s ?? (compo != null ? StructureTools.Find(S.SS, compo) : null);
        }

        private string SpineSuffix(string seg)
        {
            switch (seg) { case "UP": return B.SuffixUp; case "MID": return B.SuffixMid; case "MID2": return B.SuffixMid2; default: return B.SuffixLow; }
        }

        /// <summary>Procedure: iso at mid-level of the field target in sup-inf, then adjusted to a whole number.</summary>
        private double IsoFromStructure(Structure tgt, FieldSegment seg)
        {
            var ext = StructureTools.SliceExtents(tgt, S.Image);
            double mid = 0.5 * (ext.Max(e => e.Z) + ext.Min(e => e.Z));
            double iso = S.Cfg.Geometry.RoundSpineIsoToWholeCm ? S.BrainIso.z + CsiGeometryPlanner.RoundToCm(mid - S.BrainIso.z) : mid;
            if (Math.Abs(iso - seg.IsoZ) > 1)
                Log.Warn("{0}: iso from structure ({1:+0.0;-0.0} cm) differs from the planned split ({2:+0.0;-0.0} cm) - using the structure.",
                         seg.Name, S.IsoOffsetYcm(iso), S.IsoOffsetYcm(seg.IsoZ));
            return iso;
        }

        private void CheckFieldLimit(Structure tgt, double isoZ, string label)
        {
            var ext = StructureTools.SliceExtents(tgt, S.Image);
            double lat = Math.Max(B.BrainLateralMarginMm, B.SpineLateralMarginMm);
            double sup = ext.Max(e => e.Z) - isoZ + lat, inf = isoZ - ext.Min(e => e.Z) + lat;
            if (sup > M.HalfFieldLimitMm || inf > M.HalfFieldLimitMm)
                Log.Error("{0}: spots reach {1:F1} cm sup / {2:F1} cm inf of iso (limit {3:F1} cm on {4}).", label, sup / 10, inf / 10, M.HalfFieldLimitMm / 10, M.DisplayName);
            else
                Log.Info("{0}: field extent {1:F1} cm sup / {2:F1} cm inf of iso (limit {3:F1}).", label, sup / 10, inf / 10, M.HalfFieldLimitMm / 10);
        }

        private IonBeam AddField(IonPlanSetup plan, string id, double gantry, VVector iso, Structure target,
                                 double latMm, double proxMm, double distMm, double airGapCm)
        {
            // Snout position for the air gap, computed before the field exists (no BODY / offset -> initial position + checklist).
            var ag = PlanAirGap(iso, gantry, airGapCm);
            double snout = ag.Ok ? Math.Round(ag.SnoutMm / MmPerSnoutUnit, 2) : M.InitialSnoutPosition;

            IonBeam beam;
            try { beam = NewBeam(plan, gantry, iso, snout); }
            catch (Exception ex)
            {
                beam = null;
                // 1) Eclipse rejects unknown technique / tolerance / snout IDs: retry with what existing plans on this room use.
                var pm = S.PlanMachine(M);
                if (pm != null && !(CsiSession.SameId(M.TechniqueId, pm.TechniqueId) && CsiSession.SameId(M.ToleranceId, pm.ToleranceId) && CsiSession.SameId(M.SnoutId, pm.SnoutId)))
                {
                    Log.Warn("{0}: Eclipse rejected technique '{1}' / tolerance '{2}' / snout '{3}' ({4}); using '{5}' / '{6}' / '{7}' from this patient's existing {8} plan(s). Update {9}.",
                             id, M.TechniqueId, M.ToleranceId, M.SnoutId, ex.Message,
                             Pick(pm.TechniqueId, M.TechniqueId), Pick(pm.ToleranceId, M.ToleranceId), Pick(pm.SnoutId, M.SnoutId), M.MachineId, "SiteSettings.cs");
                    M.TechniqueId = Pick(pm.TechniqueId, M.TechniqueId);
                    M.ToleranceId = Pick(pm.ToleranceId, M.ToleranceId);
                    M.SnoutId = Pick(pm.SnoutId, M.SnoutId);
                    try { beam = NewBeam(plan, gantry, iso, snout); } catch (Exception ex2) { ex = ex2; }
                }
                // 2) the computed snout position may be outside the snout's travel: create at the initial position instead
                if (beam == null && ag.Ok && Math.Abs(snout - M.InitialSnoutPosition) > 1e-6)
                {
                    beam = NewBeam(plan, gantry, iso, M.InitialSnoutPosition);   // throws if the IDs are the problem
                    Log.Warn("{0}: snout position {1:F2} not accepted ({2}); field created at {3:F2}.", id, snout, ex.Message, M.InitialSnoutPosition);
                    ag.Ok = false; ag.Note = "snout position " + snout.ToString("F2") + " not accepted";
                    snout = M.InitialSnoutPosition;
                }
                if (beam == null) throw;
            }
            beam.Id = id;

            try { ApplyFieldParameters(beam, target, M.RangeShifterId); }
            catch (Exception ex)
            {
                var pm = S.PlanMachine(M);
                if (pm == null || CsiSession.SameId(M.RangeShifterId, pm.RangeShifterId)) throw;
                Log.Warn("{0}: Eclipse rejected range shifter '{1}' ({2}); using '{3}' from this patient's existing {4} plan(s). Update {5}.",
                         id, M.RangeShifterId, ex.Message, pm.RangeShifterId, M.MachineId, "SiteSettings.cs");
                M.RangeShifterId = pm.RangeShifterId;
                ApplyFieldParameters(beam, target, M.RangeShifterId);
            }

            beam.LateralMargins = new VRect<double>(latMm, latMm, latMm, latMm);
            beam.ProximalTargetMargin = proxMm;
            beam.DistalTargetMargin = distMm;
            Log.Info("{0}: gantry {1}, target {2}, RS {3}, target margins lateral {4:0.#} / proximal {5:0.#} / distal {6:0.#} mm (read back {7:0.#} / {8:0.#} / {9:0.#}){10}.",
                     id, gantry, target.Id, M.RangeShifterId, latMm, proxMm, distMm,
                     beam.LateralMargins.X1, beam.ProximalTargetMargin, beam.DistalTargetMargin,
                     S.UseRangeMargins ? "; proximal / distal are replaced by the range margins below" : "");

            ReportAirGap(beam, ag, airGapCm, snout);
            return beam;
        }

        private IonBeam NewBeam(IonPlanSetup plan, double gantry, VVector iso, double snoutPosition)
        {
            var mp = new ProtonBeamMachineParameters(M.MachineId, M.TechniqueId, M.ToleranceId);
            return (IonBeam)plan.AddModulatedScanningBeam(mp, M.SnoutId, snoutPosition, gantry, B.CouchAngle, iso);
        }

        private void ApplyFieldParameters(IonBeam beam, Structure target, string rangeShifterId)
        {
            var p = beam.GetEditableParameters();
            p.TargetStructure = target;
            if (!string.IsNullOrWhiteSpace(rangeShifterId))
            {
                p.PreSelectedRangeShifter1Id = rangeShifterId;
                p.PreSelectedRangeShifter1Setting = B.RangeShifterSetting;
            }
            p.WeightFactor = 1.0;
            beam.ApplyParameters(p);
        }

        private static string Pick(string preferred, string fallback) { return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred; }

        // ------------------------------------------------------------------ air gap
        /// <summary>Result of the air-gap geometry for one field (computed before the field is created).</summary>
        private class AirGapPlan
        {
            public bool Ok;
            public double SnoutMm, DepthMm, OffsetMm;
            public string Surface = "", Note = "";
        }

        /// <summary>
        /// Snout position for the requested air gap, using Eclipse's definition (Core/AirGapCalculator): the smallest
        /// distance from the range-shifter face to the BODY (or a couch / insert structure) inside the snout's
        /// Max X x Max Y footprint. The snout position goes straight into AddModulatedScanningBeam, so nothing has to be
        /// moved afterwards (the API reports no air gap on the 18.1 test system).
        /// </summary>
        private AirGapPlan PlanAirGap(VVector iso, double gantryDeg, double targetCm)
        {
            var r = new AirGapPlan();
            if (S.Body == null) { r.Note = "no BODY structure"; return r; }
            if (Math.Abs(B.CouchAngle) > 0.01) { r.Note = "couch angle is not 0"; return r; }
            if (!(M.SnoutToRangeShifterMm > 0)) { r.Note = "SnoutToRangeShifterMm is not set for " + M.DisplayName; return r; }
            double halfX = M.SnoutSizeXcm * 5.0, halfY = M.SnoutSizeYcm * 5.0;   // cm full width -> mm half width
            if (!(halfX > 0 && halfY > 0)) { r.Note = "SnoutSizeXcm / SnoutSizeYcm not set for " + M.DisplayName; return r; }

            var structures = new List<Structure> { S.Body };
            structures.AddRange(StructureTools.FindByRegex(S.SS, StructureRecipes.CouchRegex).Where(x => x != S.Body && !x.IsEmpty));
            double best = double.NegativeInfinity;
            foreach (var st in structures)
            {
                double d = AirGapCalculator.MaxDepthTowardSource(Outlines(st, iso.z, halfY), iso.x, iso.y, iso.z, gantryDeg, halfX, halfY);
                if (d > best + 0.5) { best = d; r.Surface = st.Id; }
            }
            if (double.IsNegativeInfinity(best)) { r.Note = "the BODY is not inside the snout footprint"; return r; }
            r.DepthMm = best;
            r.OffsetMm = M.SnoutToRangeShifterMm;
            r.SnoutMm = AirGapCalculator.SnoutPositionMm(best, targetCm * 10, r.OffsetMm);
            if (r.SnoutMm < 100 || r.SnoutMm > 800) { r.Note = string.Format("computed snout position {0:F1} cm is implausible", r.SnoutMm / 10); return r; }
            r.Ok = true;
            return r;
        }

        /// <summary>Outlines of a structure on the image planes within halfY of the isocenter plane.</summary>
        private IEnumerable<Contour> Outlines(Structure st, double isoZ, double halfY)
        {
            var img = S.Image;
            for (int k = 0; k < img.ZSize; k++)
            {
                double z = StructureTools.PlaneZ(img, k);
                if (Math.Abs(z - isoZ) > halfY) continue;
                VVector[][] cs;
                try { cs = st.GetContoursOnImagePlane(k); } catch (Exception) { continue; }
                if (cs == null) continue;
                foreach (var c in cs)
                    if (c != null && c.Length >= 2)
                        yield return new Contour(z, c.Select(p => p.x).ToArray(), c.Select(p => p.y).ToArray());
            }
        }

        private double MmPerSnoutUnit { get { return string.Equals(B.SnoutPositionApiUnit, "mm", StringComparison.OrdinalIgnoreCase) ? 1 : 10; } }

        private bool airGapNoted;

        /// <summary>Logs the air gap of a created field and checks that Eclipse kept the snout position.</summary>
        private void ReportAirGap(IonBeam beam, AirGapPlan ag, double targetCm, double requestedSnout)
        {
            if (!ag.Ok)
            {
                Log.Manual("{0}: air gap not set automatically ({1}) - in Field Properties > Snout choose 'Input to calculation: Air Gap' and enter {2} cm.",
                           beam.Id, ag.Note, targetCm);
                return;
            }
            double actual = beam.SnoutPosition;
            if (Math.Abs(actual - requestedSnout) > 0.01 * (MmPerSnoutUnit == 1 ? 10 : 1))
            {
                Log.Manual("{0}: Eclipse set the snout to {1:F2} instead of {2:F2} - set the air gap to {3} cm in Field Properties.", beam.Id, actual, requestedSnout, targetCm);
                return;
            }
            // cross-check the snout-to-range-shifter offset when Eclipse already reports the range-shifter position
            try
            {
                var rs = beam.IonControlPoints.First().RangeShifterSettings.FirstOrDefault();
                if (rs != null && rs.IsocenterToRangeShifterDistance > 0)
                {
                    double off = actual * MmPerSnoutUnit - rs.IsocenterToRangeShifterDistance;
                    if (Math.Abs(off - ag.OffsetMm) > 2)
                        Log.Warn("{0}: snout-to-range-shifter distance is {1:F1} mm in Eclipse, {2:F1} mm in SiteSettings.cs (SnoutToRangeShifterMm) - the air gap is off by the difference.",
                                 beam.Id, off, ag.OffsetMm);
                }
            }
            catch (Exception) { }

            string api = "";
            try
            {
                double a = beam.AirGap;
                if (!double.IsNaN(a) && !double.IsInfinity(a) && a > 0) api = string.Format("; Eclipse reports {0:0.##}", a);
            }
            catch (Exception) { }
            Log.Info("{0}: snout position {1:F1} cm for a {2:0.#} cm air gap to {3} (outermost point {4:F1} cm from the isocenter toward the source, within the {5:0.#} x {6:0.#} cm snout){7}.",
                     beam.Id, ag.SnoutMm / 10, targetCm, ag.Surface, ag.DepthMm / 10, M.SnoutSizeXcm, M.SnoutSizeYcm, api);
            if (!airGapNoted)
            {
                airGapNoted = true;
                Log.Manual("Air gaps were set through the snout position: in Field Properties > Snout the (read-only) Air Gap should read {0} cm; then run the collision check.", targetCm);
            }
        }

    }
}
