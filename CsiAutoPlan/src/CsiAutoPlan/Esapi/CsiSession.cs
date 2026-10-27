using System;
using System.Collections.Generic;
using System.Linq;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    public class HuRow
    {
        public HuOverride Rule;
        public Structure Structure;
        public string CurrentHu = "";
        public bool Apply;
    }

    /// <summary>
    /// Everything the dialog shows and the automation uses: selected structures, detected anatomy,
    /// isocenters and the field-target split. Built read-only by <see cref="Analyze"/>; nothing is
    /// written to the patient until the automation steps run.
    /// </summary>
    public class CsiSession
    {
        public readonly ScriptContext Context;
        public readonly Patient Patient;
        public readonly StructureSet SS;
        public readonly Image Image;
        public readonly CsiSettings Cfg;
        public readonly RunLog Log;
        public ApiCapabilities Caps;

        // ---- structures (UI selectable)
        public Structure Body, AiBrain, AiThecalSac, TargetCtv, InvolvedVb;
        public bool UseDraftUnion;               // non-clinical: CTV = (D)CTV_BRAIN OR (D)CTV_THECALSAC
        public bool OverwriteDraftStructures;

        // ---- geometry
        private MachineOption machine;
        /// <summary>Selected room. Changing it applies that room's default spine scheme (MachineOption.SpineScheme).</summary>
        public MachineOption Machine
        {
            get { return machine; }
            set { if (value != machine) { machine = value; ApplySchemeDefaults(); } }
        }
        public bool UseUserOrigin = true;
        public VVector UserOrigin;
        public VVector ComputedBrainIso;
        public ShoulderResult Shoulder;
        public double ShoulderTopZ;
        public double CtvTopZ, CtvBottomZ, BrainBottomZ;
        public double SpineIsoY;                 // DICOM y of the spine isocenters (whole-cm from brain iso)
        public double SpineApTargetY;            // raw A-P target (VB centre or canal centre shifted anteriorly)
        public double? SpineIsoZcmOverride;      // user override: spine iso Z (cm, Eclipse display, +ant) relative to brain iso
        private double brainCenterX, brainCenterY, brainTopZ;
        public int ForcedSpineFieldCount;
        public double OverlapMm, ShoulderClearanceMm, PreferredOverlapMm;
        public bool PosteriorAtBrainIso;         // POST at the brain iso extended down the spine (SpineSchemes.PostExtended)
        public bool AddPostBrainField;           // POST at the brain iso on the brain target (SpineSchemes.PostBrain)
        public GeometryPlan Geometry;
        public List<string> BlockingProblems = new List<string>();
        public string IsoSummary = "";
        public string SchemeComparison = "";

        /// <summary>Scheme of a room: its SpineScheme, else the global Geometry / Beams flags of SiteSettings.cs.</summary>
        public string SchemeFor(MachineOption m)
        {
            if (m != null && !string.IsNullOrWhiteSpace(m.SpineScheme))
            {
                var hit = SpineSchemes.All.FirstOrDefault(x => string.Equals(x, m.SpineScheme.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            if (Cfg.Geometry.PosteriorAtBrainIso) return SpineSchemes.PostExtended;
            return Cfg.Beams.AddPostBrainField ? SpineSchemes.PostBrain : SpineSchemes.Separate;
        }

        public string Scheme
        {
            get { return PosteriorAtBrainIso ? SpineSchemes.PostExtended : AddPostBrainField ? SpineSchemes.PostBrain : SpineSchemes.Separate; }
            set { PosteriorAtBrainIso = value == SpineSchemes.PostExtended; AddPostBrainField = value == SpineSchemes.PostBrain; }
        }

        public static string SchemeDescription(string scheme)
        {
            switch (scheme)
            {
                case SpineSchemes.PostExtended: return "RPO/LPO + POST at brain iso extended down the spine, lower iso(s) below";
                case SpineSchemes.PostBrain: return "RPO/LPO/POST at brain iso on the brain target, separate spine iso(s)";
                default: return "RPO/LPO at brain iso, separate spine iso(s)";
            }
        }

        private void ApplySchemeDefaults() { if (Cfg != null) Scheme = SchemeFor(machine); }

        /// <summary>
        /// Field-target structures the current split needs that are missing or were built for a different split
        /// (e.g. by an earlier run with another scheme / room). Step D uses these structures for targets and
        /// isocenters, so they must match before fields are created without re-running step C.
        /// </summary>
        public List<string> FieldTargetProblems()
        {
            var problems = new List<string>();
            if (Geometry == null || SS == null) return problems;
            double tol = 5 + 2 * Image.ZRes;     // slab snapping, margin rounding and BODY crop move the ends by a few mm
            foreach (var seg in Geometry.All())
            {
                var id = RecipeBook.Id(Role.FieldTarget, seg.Name);
                if (id == null) { problems.Add("StructureRecipes.cs has no field-target recipe"); break; }
                var st = SS.Structures.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (st == null || st.IsEmpty) { problems.Add(id + " does not exist"); continue; }
                var ext = StructureTools.SliceExtents(st, Image);
                if (ext.Count == 0) { problems.Add(id + " is empty"); continue; }
                double hi = ext.Max(e => e.Z), lo = ext.Min(e => e.Z);
                if (Math.Abs(hi - seg.PbstvHiZ) > tol || Math.Abs(lo - seg.PbstvLoZ) > tol)
                    problems.Add(string.Format("{0} spans Y {1:F1} to {2:F1} cm, the current split needs {3:F1} to {4:F1} cm",
                                               id, ToUserYcm(hi), ToUserYcm(lo), ToUserYcm(seg.PbstvHiZ), ToUserYcm(seg.PbstvLoZ)));
            }
            return problems;
        }

        /// <summary>Number of treatment fields of the current split.</summary>
        public int FieldCount { get { return Geometry == null ? 0 : 2 + (Geometry.Post != null || AddPostBrainField ? 1 : 0) + Geometry.Spine.Count; } }

        // ---- IDs used by this patient's existing proton plans (read-only harvest; null if none)
        public HarvestResult PlanSettings;

        /// <summary>Machine settings of existing plans on the same treatment unit, or null.</summary>
        public MachineOption PlanMachine(MachineOption m)
        {
            MachineOption pm;
            return m != null && PlanSettings != null && PlanSettings.Machines.TryGetValue(m.MachineId, out pm) ? pm : null;
        }

        /// <summary>
        /// Fills EMPTY room IDs of SiteSettings.cs with the values this patient's existing plans use on the same
        /// room (Eclipse accepted those). Non-empty SiteSettings.cs values are kept; they are only replaced at plan
        /// creation if Eclipse rejects them (see PlanBuilder).
        /// </summary>
        public void UsePlanSettings(HarvestResult h)
        {
            PlanSettings = h;
            if (h == null) return;
            foreach (var m in Cfg.Machines)
            {
                var pm = PlanMachine(m);
                if (pm == null) continue;
                var filled = new List<string>();
                if (string.IsNullOrWhiteSpace(m.PatientSupportDeviceId) && !string.IsNullOrWhiteSpace(pm.PatientSupportDeviceId)) { m.PatientSupportDeviceId = pm.PatientSupportDeviceId; filled.Add("patient support '" + pm.PatientSupportDeviceId + "'"); }
                if (string.IsNullOrWhiteSpace(m.SnoutId) && !string.IsNullOrWhiteSpace(pm.SnoutId)) { m.SnoutId = pm.SnoutId; filled.Add("snout '" + pm.SnoutId + "'"); }
                if (string.IsNullOrWhiteSpace(m.ToleranceId) && !string.IsNullOrWhiteSpace(pm.ToleranceId)) { m.ToleranceId = pm.ToleranceId; filled.Add("tolerance '" + pm.ToleranceId + "'"); }
                if (string.IsNullOrWhiteSpace(m.RangeShifterId) && !string.IsNullOrWhiteSpace(pm.RangeShifterId)) { m.RangeShifterId = pm.RangeShifterId; filled.Add("range shifter '" + pm.RangeShifterId + "'"); }
                if (filled.Count > 0)
                    Log.Warn("{0}: SiteSettings.cs has no {1}; using {2} from this patient's existing plan(s). Add them to {3}.",
                             m.DisplayName, filled.Count == 1 ? "value" : "values", string.Join(", ", filled), "SiteSettings.cs");
                var differ = new List<string>();
                if (!SameId(m.PatientSupportDeviceId, pm.PatientSupportDeviceId)) differ.Add(string.Format("patient support '{0}' vs '{1}'", m.PatientSupportDeviceId, pm.PatientSupportDeviceId));
                if (!SameId(m.SnoutId, pm.SnoutId)) differ.Add(string.Format("snout '{0}' vs '{1}'", m.SnoutId, pm.SnoutId));
                if (!SameId(m.ToleranceId, pm.ToleranceId)) differ.Add(string.Format("tolerance '{0}' vs '{1}'", m.ToleranceId, pm.ToleranceId));
                if (!SameId(m.RangeShifterId, pm.RangeShifterId)) differ.Add(string.Format("range shifter '{0}' vs '{1}'", m.RangeShifterId, pm.RangeShifterId));
                if (differ.Count > 0)
                    Log.Info("{0}: SiteSettings.cs differs from existing plans ({1}). SiteSettings.cs is used; existing-plan IDs are the fallback if Eclipse rejects it.",
                             m.DisplayName, string.Join("; ", differ));
            }
        }

        public static bool SameId(string a, string b)
        {
            return string.IsNullOrWhiteSpace(b) || string.Equals((a ?? "").Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // ---- plan
        public string CourseId, PlanId;
        public double DosePerFractionGy;
        public int Fractions;
        public bool DoDraftStructures = true, DoHuOverrides = true, DoFieldTargets = true, DoPlan = true, DoObjectives = true, DoOptimize = true;
        /// <summary>Step D: proximal / distal target margins from each field's R and SOBP (else the fixed margins).</summary>
        public bool UseRangeMargins;
        public List<HuRow> HuRows = new List<HuRow>();

        public VVector BrainIso { get { return UseUserOrigin ? UserOrigin : ComputedBrainIso; } }
        public double TotalDoseGy { get { return DosePerFractionGy * Fractions; } }

        public CsiSession(ScriptContext ctx, CsiSettings cfg, RunLog log)
        {
            Context = ctx;
            Patient = ctx.Patient;
            SS = ctx.StructureSet ?? (ctx.PlanSetup != null ? ctx.PlanSetup.StructureSet : null);
            Image = SS != null ? SS.Image : ctx.Image;
            Cfg = cfg;
            Log = log;
            OverlapMm = cfg.Geometry.MinFieldTargetOverlapMm;
            PreferredOverlapMm = cfg.Geometry.PreferredFieldTargetOverlapMm;
            Machine = cfg.Machines.FirstOrDefault(m => m.Enabled && string.Equals(m.MachineId, cfg.DefaultMachineId, StringComparison.OrdinalIgnoreCase))
                      ?? cfg.Machines.FirstOrDefault(m => m.Enabled) ?? cfg.Machines.FirstOrDefault();
            ApplySchemeDefaults();
            ShoulderClearanceMm = cfg.Geometry.ShoulderClearanceMm;
            UseUserOrigin = cfg.Geometry.UseUserOriginAsBrainIso;
            PlanId = cfg.Plan.PlanId;
            DosePerFractionGy = cfg.Plan.DosePerFractionGy;
            Fractions = cfg.Plan.NumberOfFractions;
            UseRangeMargins = cfg.RangeMargins.Enabled;
        }

        // ================================================================ analysis
        public void Analyze()
        {
            Log.Step = "Analyze";
            BlockingProblems.Clear();
            if (Patient == null || SS == null || Image == null)
            {
                BlockingProblems.Add("Open the patient with the planning CT structure set loaded before running the plugin.");
                return;
            }
            UserOrigin = Image.HasUserOrigin ? Image.UserOrigin : Image.Origin;
            StructureTools.CheckAxial(Image);

            Body = StructureTools.Find(SS, StructureRecipes.Body);
            AiBrain = StructureTools.Find(SS, StructureRecipes.AiBrain);
            AiThecalSac = StructureTools.Find(SS, StructureRecipes.AiThecalSac);
            InvolvedVb = StructureTools.Find(SS, StructureRecipes.InvolvedVb);
            TargetCtv = PickCsiCtv();
            UseDraftUnion = TargetCtv == null;

            if (!StructureTools.FindByRegex(SS, StructureRecipes.CouchRegex).Any())
                Log.Manual("No couch structure found: insert the kVue One couch (with the simulation overlay) before dose calculation.");
            if (!Image.HasUserOrigin) { UseUserOrigin = false; Log.Warn("Image has no user origin - using a computed brain isocenter."); }

            // Prefill Rx / IDs from an ion plan that is open in Eclipse (optimize-only passes).
            var open = Context.IonPlanSetup;
            if (open != null)
            {
                try
                {
                    if (open.NumberOfFractions.HasValue && open.NumberOfFractions.Value > 0)
                    {
                        Fractions = open.NumberOfFractions.Value;
                        DosePerFractionGy = ToGy(open.DosePerFraction);
                    }
                }
                catch (Exception) { }
            }

            BuildHuRows();
            CourseId = SuggestCourseId();
            Reevaluate();
        }

        /// <summary>
        /// The CSI CTV among the CTV_#### matches: the one with the largest sup-inf extent (a boost CTV such as
        /// CTV_5400 can be listed before CTV_3600 in the structure set - the clinical example does exactly that).
        /// </summary>
        private Structure PickCsiCtv()
        {
            var cands = StructureTools.FindByRegex(SS, StructureRecipes.PhysicianCtvRegex).ToList();
            if (cands.Count <= 1) return cands.FirstOrDefault();
            Structure best = null; double bestLen = -1;
            foreach (var c in cands)
            {
                var ext = StructureTools.SliceExtents(c, Image);
                double len = ext.Count == 0 ? 0 : ext.Max(e => e.Z) - ext.Min(e => e.Z);
                if (len > bestLen) { bestLen = len; best = c; }
            }
            Log.Info("CTV candidates {0}: using {1} (largest sup-inf extent, {2:F1} cm).", string.Join(", ", cands.Select(c => c.Id)), best.Id, bestLen / 10);
            return best;
        }

        /// <summary>(D) structures (dosimetrist-edited) take precedence over raw AI contours for geometry and the draft CTV.</summary>
        public Structure BrainSource() { var id = RecipeBook.Id(Role.DraftBrain); return (id != null ? StructureTools.Find(SS, id) : null) ?? AiBrain; }
        public Structure CanalSource() { var id = RecipeBook.Id(Role.DraftThecalSac); return (id != null ? StructureTools.Find(SS, id) : null) ?? AiThecalSac; }

        /// <summary>Re-checks the selected structures and re-measures the anatomy (called again when the user changes a selection).</summary>
        public void Reevaluate()
        {
            BlockingProblems.Clear();
            if (Image.ImagingOrientation != PatientOrientation.HeadFirstSupine)
                BlockingProblems.Add("Image orientation is " + Image.ImagingOrientation + "; beam angles in this plugin assume Head First-Supine.");
            if (Body == null) BlockingProblems.Add("BODY structure not selected/found (StructureRecipes.Body: " + StructureRecipes.Body + ").");
            if (BrainSource() == null) BlockingProblems.Add("Brain structure not selected/found (StructureRecipes.AiBrain: " + StructureRecipes.AiBrain + ").");
            if (CanalSource() == null) BlockingProblems.Add("Thecal sac structure not selected/found (StructureRecipes.AiThecalSac: " + StructureRecipes.AiThecalSac + ").");
            if (!UseDraftUnion && TargetCtv == null) BlockingProblems.Add("Select the physician CTV or the draft option.");
            if (BlockingProblems.Count == 0) MeasureAnatomy();
        }

        public static double ToGy(DoseValue d)
        {
            return d.Unit == DoseValue.DoseUnit.cGy ? d.Dose / 100.0 : d.Dose;
        }

        private void MeasureAnatomy()
        {
            var brain = BrainSource();
            var canal = CanalSource();
            var brainSlices = StructureTools.SliceExtents(brain, Image);
            var canalSlices = StructureTools.SliceExtents(canal, Image);
            if (brainSlices.Count == 0 || canalSlices.Count == 0) { BlockingProblems.Add("Brain or thecal sac has no contours."); return; }

            BrainBottomZ = brainSlices.Min(s => s.Z);
            RecomputeCtvExtent(brainSlices, canalSlices);

            // Shoulders from the BODY width profile
            var body = StructureTools.SliceExtents(Body, Image, Cfg.Geometry.MinBodyContourAreaMm2);
            Shoulder = ShoulderDetector.Detect(body.Select(b => new WidthSample(b.Z, b.Width)), BrainBottomZ,
                                               Cfg.Geometry.ShoulderSearchWindowMm, Cfg.Geometry.ShoulderRiseFraction);
            if (Shoulder.Success) { ShoulderTopZ = Shoulder.ShoulderTopZ; Log.Info("Shoulders: " + Shoulder.Message); }
            else
            {
                ShoulderTopZ = BrainBottomZ - 100;
                Log.Warn("Shoulder detection failed (" + Shoulder.Message + "). Enter the shoulder level manually in the Geometry tab.");
            }

            // Computed brain iso: L-R / A-P centre of the brain; S-I halfway between vertex and shoulders
            double xmin = brainSlices.Min(s => s.XMin), xmax = brainSlices.Max(s => s.XMax);
            double ymin = brainSlices.Min(s => s.YMin), ymax = brainSlices.Max(s => s.YMax);
            brainCenterX = 0.5 * (xmin + xmax);
            brainCenterY = 0.5 * (ymin + ymax);
            brainTopZ = brainSlices.Max(s => s.Z);
            // Display reference for "user" coordinates: the user origin, or (without one) the brain centre at
            // mid-brain height, so dialog values stay in a sensible range.
            UserOrigin = Image.HasUserOrigin ? Image.UserOrigin : new VVector(brainCenterX, brainCenterY, 0.5 * (brainTopZ + BrainBottomZ));

            // Spine iso A-P: in/near the vertebral bodies -> whole-cm posterior shift from brain iso
            var vbSlices = InvolvedVb != null && Cfg.Geometry.SpineIsoFromInvolvedVb
                ? StructureTools.SliceExtents(InvolvedVb, Image).Where(s => s.Z < ShoulderTopZ).ToList() : new List<SliceExtent>();
            if (vbSlices.Count > 0) SpineApTargetY = vbSlices.Average(s => s.YCenter);
            else
            {
                var spine = canalSlices.Where(s => s.Z < ShoulderTopZ).ToList();
                if (spine.Count == 0) spine = canalSlices;
                SpineApTargetY = spine.Average(s => s.YCenter) - Cfg.Geometry.SpineIsoAnteriorOffsetFromCanalMm;   // anterior = -y
            }
            Recompute();
        }

        public void RecomputeCtvExtent(List<SliceExtent> brainSlices = null, List<SliceExtent> canalSlices = null)
        {
            if (!UseDraftUnion && TargetCtv != null)
            {
                var ctv = StructureTools.SliceExtents(TargetCtv, Image);
                CtvTopZ = ctv.Max(s => s.Z);
                CtvBottomZ = ctv.Min(s => s.Z);
                return;
            }
            var b = brainSlices ?? StructureTools.SliceExtents(BrainSource(), Image);
            var c = canalSlices ?? StructureTools.SliceExtents(CanalSource(), Image);
            CtvTopZ = Math.Max(b.Max(s => s.Z), c.Max(s => s.Z));
            CtvBottomZ = Math.Min(b.Min(s => s.Z), c.Min(s => s.Z));
        }

        /// <summary>Re-runs the field-target split with the current UI values.</summary>
        public void Recompute()
        {
            if (Machine == null) return;
            ComputedBrainIso = new VVector(brainCenterX, brainCenterY, 0.5 * (brainTopZ + ShoulderTopZ));
            SpineIsoY = SpineIsoZcmOverride.HasValue
                ? BrainIso.y - SpineIsoZcmOverride.Value * 10.0
                : BrainIso.y + CsiGeometryPlanner.RoundToCm(SpineApTargetY - BrainIso.y);
            var input = new GeometryInput
            {
                BrainIsoZ = BrainIso.z,
                CtvTopZ = CtvTopZ,
                CtvBottomZ = CtvBottomZ,
                ShoulderTopZ = ShoulderTopZ,
                HalfFieldLimitMm = Machine.HalfFieldLimitMm,
                SpotLateralMarginMm = Math.Max(Cfg.Beams.BrainLateralMarginMm, Cfg.Beams.SpineLateralMarginMm),
                SafetyMm = Cfg.Geometry.FieldLimitSafetyMm,
                SliceAllowanceMm = Image.ZRes,           // built structures can be one plane longer at each end
                BrainMarginSupMm = StructureRecipes.BrainMargins.SupMm,
                BrainMarginInfMm = StructureRecipes.BrainMargins.InfMm,
                SpineMarginSupMm = StructureRecipes.SpineMargins.SupMm,
                SpineMarginInfMm = StructureRecipes.SpineMargins.InfMm,
                ShoulderClearanceMm = ShoulderClearanceMm,
                MinOverlapMm = OverlapMm + Image.ZRes,   // one slice of padding: structure extents are quantised to planes
                PreferredOverlapMm = PreferredOverlapMm,
                PosteriorAtBrainIso = PosteriorAtBrainIso,
                MaxSpineFields = Cfg.Geometry.MaxSpineFields,
                ForcedSpineFieldCount = ForcedSpineFieldCount,
                RoundSpineIsoToWholeCm = Cfg.Geometry.RoundSpineIsoToWholeCm,
            };
            Geometry = CsiGeometryPlanner.Plan(input);

            // Tell the planner what the other spine scheme would need (the POST field only saves an isocenter on shorter CTVs).
            var alt = input.Clone();
            alt.PosteriorAtBrainIso = !input.PosteriorAtBrainIso;
            alt.ForcedSpineFieldCount = 0;
            var altPlan = CsiGeometryPlanner.Plan(alt);
            SchemeComparison = altPlan.IsFeasible
                ? string.Format("With {0} it would be {1} isocenter(s).",
                                alt.PosteriorAtBrainIso ? "the POST field extended down the spine" : "separate spine isocenters", altPlan.IsocenterCount)
                : "";

            // Brain iso to shoulder distance check (procedure: iso -> landmarks within scanning limits)
            double toVertex = CtvTopZ - BrainIso.z, toShoulder = BrainIso.z - ShoulderTopZ;
            if (toShoulder > Machine.HalfFieldLimitMm)
                Geometry.Warnings.Add(string.Format("Brain iso is {0:F1} cm above the shoulders (> {1:F1} cm field limit): the lateral fields cannot reach the lower C-spine.",
                                                    toShoulder / 10, Machine.HalfFieldLimitMm / 10));
            IsoSummary = string.Format("Brain iso: CTV vertex {0:F1} cm above, shoulders {1:F1} cm below (procedure: iso about halfway, both within the {2:F1} cm limit).",
                                       toVertex / 10, toShoulder / 10, Machine.HalfFieldLimitMm / 10);
        }

        // ================================================================ helpers for UI (Eclipse user coordinates, cm)
        public double ToUserYcm(double z) { return (z - UserOrigin.z) / 10.0; }
        public double FromUserYcm(double ycm) { return UserOrigin.z + ycm * 10.0; }
        public double IsoOffsetYcm(double z) { return (z - BrainIso.z) / 10.0; }
        public double IsoOffsetZcm(double y) { return -(y - BrainIso.y) / 10.0; }   // +Z = anterior in Eclipse HFS display

        public VVector SpineIso(double isoZ) { return new VVector(BrainIso.x, SpineIsoY, isoZ); }

        private string SuggestCourseId()
        {
            var existing = Patient.Courses.Select(c => c.Id).ToList();
            var csi = existing.FirstOrDefault(id => id.EndsWith("-CSI", StringComparison.OrdinalIgnoreCase));
            if (csi != null) return csi;
            int max = 0;
            foreach (var id in existing)
            {
                int n;
                if (id.Length > 1 && (id[0] == 'C' || id[0] == 'c'))
                {
                    var digits = new string(id.Skip(1).TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out n) && n > max) max = n;
                }
            }
            return string.Format(Cfg.Plan.CourseIdFormat, max + 1);
        }

        private void BuildHuRows()
        {
            HuRows.Clear();
            foreach (var rule in Cfg.HuOverrides)
            {
                var s = StructureTools.Find(SS, rule.Structure);
                if (s == null) continue;
                double hu;
                var row = new HuRow { Rule = rule, Structure = s, Apply = rule.ApplyByDefault && rule.Mode != HuMode.ReportOnly };
                row.CurrentHu = s.GetAssignedHU(out hu) ? hu.ToString("F0") : "none";
                if (row.CurrentHu != "none" && !rule.OverwriteExisting) row.Apply = false;
                HuRows.Add(row);
            }
        }
    }
}
