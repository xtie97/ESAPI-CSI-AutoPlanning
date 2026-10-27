using System;
using System.Collections.Generic;
using System.Linq;

namespace CsiAutoPlan.Settings
{
    // =====================================================================================================
    //  The SHAPE of the site settings. The VALUES are set in one place: SiteSettings.cs (rooms, geometry,
    //  plan, fields, range margins, robust optimization, objectives, HU rules) and StructureRecipes.cs
    //  (everything about structures). There is no settings file: the values are compiled into the plugin.
    //  Fields here have no default values on purpose, so a value can only come from SiteSettings.cs.
    // =====================================================================================================

    /// <summary>All site-specific settings of one run (a fresh copy from <c>SiteSettings.Create()</c> per run).</summary>
    public class CsiSettings
    {
        public List<MachineOption> Machines = new List<MachineOption>();
        /// <summary>Room pre-selected in the dialog (MachineId). Empty = first enabled room.</summary>
        public string DefaultMachineId = "";
        public GeometryOptions Geometry = new GeometryOptions();
        public PlanOptions Plan = new PlanOptions();
        public BeamOptions Beams = new BeamOptions();
        public RangeMarginOptions RangeMargins = new RangeMarginOptions();
        public OptimizationOptions Optimization = new OptimizationOptions();
        public List<HuOverride> HuOverrides = new List<HuOverride>();
        public List<ObjectiveTemplate> Objectives = new List<ObjectiveTemplate>();
        /// <summary>Folder for the run logs (environment variables allowed, e.g. %TEMP%).</summary>
        public string LogDirectory = "";

        /// <summary>Problems with the settings (empty = OK). Shown at start and in tab 5.</summary>
        public List<string> Validate()
        {
            var p = new List<string>();
            if (Machines.Count == 0) p.Add("No treatment rooms defined.");
            foreach (var m in Machines)
            {
                if (string.IsNullOrWhiteSpace(m.MachineId)) p.Add("Room '" + m.DisplayName + "' has no MachineId.");
                if (!SpineSchemes.IsValid(m.SpineScheme))
                    p.Add(string.Format("Room '{0}': SpineScheme '{1}' is not one of {2} (or empty).", m.DisplayName, m.SpineScheme, string.Join(", ", SpineSchemes.All)));
                if (m.Enabled && (m.HalfFieldLimitMm < 100 || m.HalfFieldLimitMm > 250)) p.Add("Room '" + m.DisplayName + "': HalfFieldLimitMm should be 100-250 mm.");
            }
            if (!Machines.Any(m => m.Enabled)) p.Add("No room is enabled.");
            if (!string.IsNullOrWhiteSpace(DefaultMachineId) && !Machines.Any(m => m.Enabled && string.Equals(m.MachineId, DefaultMachineId, StringComparison.OrdinalIgnoreCase)))
                p.Add("DefaultMachineId '" + DefaultMachineId + "' is not an enabled room.");
            if (string.IsNullOrWhiteSpace(Plan.PlanId) || Plan.PlanId.Length > 13) p.Add("Plan ID must be 1-13 characters.");
            if (Plan.NumberOfFractions < 1 || Plan.DosePerFractionGy <= 0) p.Add("Default prescription must be positive.");
            if (Geometry.MinFieldTargetOverlapMm <= 0) p.Add("MinFieldTargetOverlapMm must be positive.");
            if (Beams.BrainLateralMarginMm < 0 || Beams.SpineLateralMarginMm < 0) p.Add("Lateral target margins must not be negative.");
            var r = RangeMargins;
            if (r.Enabled && (r.RangeUncertaintyPercent < 0 || r.RangeUncertaintyPercent > 10 || r.AddedMarginMm < 0 || r.AddedMarginMm > 20))
                p.Add("Range margins: RangeUncertaintyPercent must be 0-10 and AddedMarginMm 0-20 mm.");
            if (r.RoundUpToMm < 0) p.Add("Range margins: RoundUpToMm must not be negative.");
            if (string.IsNullOrWhiteSpace(LogDirectory)) p.Add("LogDirectory is empty.");
            return p;
        }

        /// <summary>Enabled rooms whose IDs are not filled in yet: room name -> missing fields.</summary>
        public Dictionary<string, List<string>> IncompleteMachines()
        {
            var d = new Dictionary<string, List<string>>();
            foreach (var m in Machines.Where(x => x.Enabled))
            {
                var miss = m.MissingIds();
                if (miss.Count > 0) d[m.DisplayName] = miss;
            }
            return d;
        }

        public static string ExpandPath(string p) { return Environment.ExpandEnvironmentVariables(p ?? ""); }
    }

    /// <summary>One treatment room. IDs exactly as Eclipse knows them.</summary>
    public class MachineOption
    {
        public string DisplayName = "";
        public string MachineId = "";
        /// <summary>false = room hidden in the dialog (kept for later, e.g. while a room is being commissioned).</summary>
        public bool Enabled;
        public string TechniqueId = "";
        public string ToleranceId = "";
        public string SnoutId = "";
        public string RangeShifterId = "";
        public string PatientSupportDeviceId = "";
        /// <summary>Max |spot - isocenter| in the sup-inf direction (mm).</summary>
        public double HalfFieldLimitMm;
        /// <summary>Snout position used if the air-gap geometry cannot be computed (API snout unit, see Beams.SnoutPositionApiUnit).</summary>
        public double InitialSnoutPosition;
        /// <summary>Default field arrangement for this room (<see cref="SpineSchemes"/>); the dialog can change it per patient.</summary>
        public string SpineScheme = "";
        /// <summary>Snout position minus isocenter-to-range-shifter distance (mm) for the range shifter in use.
        /// 0 = unknown (then read from the beam's range-shifter settings if Eclipse provides them).</summary>
        public double SnoutToRangeShifterMm;
        /// <summary>Snout footprint (Field Properties > Snout: Max X / Max Y, cm). Eclipse's air gap is the smallest
        /// range-shifter-to-BODY distance inside this rectangle.</summary>
        public double SnoutSizeXcm;
        public double SnoutSizeYcm;

        public List<string> MissingIds()
        {
            var miss = new List<string>();
            if (string.IsNullOrWhiteSpace(TechniqueId)) miss.Add("TechniqueId");
            if (string.IsNullOrWhiteSpace(SnoutId)) miss.Add("SnoutId");
            if (string.IsNullOrWhiteSpace(ToleranceId)) miss.Add("ToleranceId");
            if (string.IsNullOrWhiteSpace(PatientSupportDeviceId)) miss.Add("PatientSupportDeviceId");
            if (string.IsNullOrWhiteSpace(RangeShifterId)) miss.Add("RangeShifterId");
            return miss;
        }

        /// <summary>This room as a line for SiteSettings.cs (used to report the IDs of existing plans).</summary>
        public string ToCSharp()
        {
            Func<double, string> n = v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            Func<string, string> q = s => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return string.Format("new MachineOption {{ DisplayName = {0}, MachineId = {1}, Enabled = {2}, TechniqueId = {3}, ToleranceId = {4}, " +
                                 "SnoutId = {5}, RangeShifterId = {6}, PatientSupportDeviceId = {7}, HalfFieldLimitMm = {8}, InitialSnoutPosition = {9}, " +
                                 "SpineScheme = {10}, SnoutToRangeShifterMm = {11}, SnoutSizeXcm = {12}, SnoutSizeYcm = {13} }},",
                                 q(DisplayName), q(MachineId), Enabled ? "true" : "false", q(TechniqueId), q(ToleranceId), q(SnoutId), q(RangeShifterId),
                                 q(PatientSupportDeviceId), n(HalfFieldLimitMm), n(InitialSnoutPosition),
                                 string.IsNullOrWhiteSpace(SpineScheme) ? "\"\"" : "SpineSchemes." + SpineScheme.Trim(),
                                 n(SnoutToRangeShifterMm), n(SnoutSizeXcm), n(SnoutSizeYcm));
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>Field arrangements seen in the clinical plans / procedure.</summary>
    public static class SpineSchemes
    {
        /// <summary>RPO/LPO + POST at the brain iso, POST extended down the spine as far as the field allows; lower isos below.</summary>
        public const string PostExtended = "PostExtended";
        /// <summary>RPO/LPO/POST at the brain iso, all on the brain field target; separate UP/(MID)/LO spine isos.</summary>
        public const string PostBrain = "PostBrain";
        /// <summary>RPO/LPO at the brain iso; separate UP/(MID)/LO spine isos (procedure text).</summary>
        public const string Separate = "Separate";
        public static readonly string[] All = { PostExtended, PostBrain, Separate };
        public static bool IsValid(string s) { return string.IsNullOrWhiteSpace(s) || All.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase); }
    }

    public class GeometryOptions
    {
        /// <summary>Brain fields use the brain iso = user origin placed at simulation (else a computed iso).</summary>
        public bool UseUserOriginAsBrainIso;
        /// <summary>Brain field target ends at least this far above the top of the shoulders (mm).</summary>
        public double ShoulderClearanceMm;
        /// <summary>Neighbouring field targets overlap by at least this much (mm).</summary>
        public double MinFieldTargetOverlapMm;
        /// <summary>Overlap to aim for when the field lengths allow it (never adds a field), mm.</summary>
        public double PreferredFieldTargetOverlapMm;
        /// <summary>Layout used when a room has no SpineScheme: true = POST at the brain iso extended down the spine.</summary>
        public bool PosteriorAtBrainIso;
        /// <summary>Headroom kept inside the scanning limit when sizing field targets (mm).</summary>
        public double FieldLimitSafetyMm;
        public int MaxSpineFields;
        public bool RoundSpineIsoToWholeCm;
        /// <summary>Spine iso A-P = thecal-sac centre shifted anteriorly by this amount, rounded to whole cm (mm).</summary>
        public double SpineIsoAnteriorOffsetFromCanalMm;
        /// <summary>Use the involved-VB centre instead of the canal for the spine iso depth.</summary>
        public bool SpineIsoFromInvolvedVb;
        /// <summary>Shoulder detection from the BODY width profile.</summary>
        public double ShoulderSearchWindowMm;
        public double ShoulderRiseFraction;
        public double MinBodyContourAreaMm2;
    }

    /// <summary>Margins (mm) in DICOM axes: Right = -x, Left = +x, Ant = -y, Post = +y, Inf = -z, Sup = +z.</summary>
    public class MarginSet
    {
        public double RightMm, LeftMm, AntMm, PostMm, InfMm, SupMm;
        public double[] All() { return new[] { RightMm, LeftMm, AntMm, PostMm, InfMm, SupMm }; }
        public bool IsUniform { get { var a = All(); return a.All(v => Math.Abs(v - a[0]) < 1e-6); } }
    }

    public class PlanOptions
    {
        /// <summary>{0} = next free course number.</summary>
        public string CourseIdFormat = "";
        public string PlanId = "";
        /// <summary>Defaults shown in the dialog; always confirm against the EBRT Order.</summary>
        public double DosePerFractionGy;
        public int NumberOfFractions;
        /// <summary>Calculation models. Empty = the models of this patient's existing proton plans (optimizer: a NUPO model
        /// if any), else Eclipse's defaults.</summary>
        public string VolumeDoseModel = "";
        public string OptimizationModel = "";
        public string BeamLineModifierModel = "";
        public string PostProcessingModel = "";
        public double DoseGridCm;
        /// <summary>Option key on the dose model; a key without the "Group/" prefix also works if it matches one key.</summary>
        public string DoseGridOptionKey = "";
        /// <summary>Spot spacing (cm) named in the checklist item when SpotSpacingOptions is empty.</summary>
        public double SpotSpacingCm;
        /// <summary>Beam-line-modifier model options (spot spacing). Empty = manual checklist item.</summary>
        public List<CalcOption> SpotSpacingOptions = new List<CalcOption>();
        /// <summary>Optional NUPO option for the optimization time limit (key from the run report's option list).</summary>
        public string MaxOptimizationTimeOptionKey = "";
        public string MaxOptimizationTimeValue = "";
    }

    public class CalcOption
    {
        public string Key = "";
        public string Value = "";
    }

    public class BeamOptions
    {
        /// <summary>Gantry angles (HFS, IEC 61217): 265 = RPO, 95 = LPO, 180 = PA.</summary>
        public double BrainRpoGantry;
        public double BrainLpoGantry;
        public double SpineGantry;
        public double CouchAngle;
        /// <summary>Only for the Separate layout: optional PA field at the brain iso on the brain field target.</summary>
        public bool AddPostBrainField;
        public double PostBrainGantry;

        /// <summary>Field target margins (mm; Field Properties > Target Margin shows cm). Lateral margins are always these;
        /// proximal / distal are these only when RangeMargins is off or a field's range is not available.</summary>
        public double BrainLateralMarginMm;
        public double BrainProximalMarginMm;
        public double BrainDistalMarginMm;
        public double SpineLateralMarginMm;
        public double SpineProximalMarginMm;
        public double SpineDistalMarginMm;

        /// <summary>Air gap (cm) as Eclipse defines it: smallest range-shifter-to-BODY distance inside the snout footprint.</summary>
        public double LateralAirGapCm;
        public double PosteriorAirGapCm;
        /// <summary>"cm" or "mm": unit of the snout position in AddModulatedScanningBeam.</summary>
        public string SnoutPositionApiUnit = "";
        public string RangeShifterSetting = "";
        /// <summary>true = COMPO structures as field targets; false = OPT structures.</summary>
        public bool FieldTargetIsCompo;

        public string BrainRpoId = "";
        public string BrainLpoId = "";
        /// <summary>Prefixed with the field number.</summary>
        public string BrainPostId = "";
        /// <summary>{0} = field number, {1} = suffix below.</summary>
        public string SpineIdFormat = "";
        public string SuffixUp = "";
        public string SuffixMid = "";
        public string SuffixMid2 = "";
        public string SuffixLow = "";
    }

    /// <summary>
    /// Proximal / distal field target margins from each field's range (PBSTV margin sheet):
    ///   distal = R x RangeUncertaintyPercent + AddedMarginMm, proximal = (R - SOBP) x RangeUncertaintyPercent + AddedMarginMm,
    /// with R and SOBP = Eclipse's nominal range and SOBP width of the field.
    /// </summary>
    public class RangeMarginOptions
    {
        /// <summary>Compute the margins per field (the dialog can switch it off per patient).</summary>
        public bool Enabled;
        public double RangeUncertaintyPercent;
        public double AddedMarginMm;
        /// <summary>Round the computed margins UP to a multiple of this (mm); 0 = no rounding.</summary>
        public double RoundUpToMm;
        /// <summary>If Eclipse has no nominal range / SOBP for a field yet, calculate the beam line (with proximal and distal
        /// margins at 0) to get them. false = such fields keep the fixed margins and get a checklist item.</summary>
        public bool CalculateBeamLineIfNeeded;
    }

    public class OptimizationOptions
    {
        public bool RemoveExistingObjectives;
        /// <summary>Procedure: Normal Tissue Objective off.</summary>
        public bool ProtonNormalTissueObjective;
        public int MaxIterations;
        /// <summary>Plan-level robust scenarios: +/- PlanIsoShiftCm on each axis x +/- PlanRangeErrorPercent.</summary>
        public bool PlanUncertainties;
        public double PlanIsoShiftCm;
        public double PlanRangeErrorPercent;
        /// <summary>Unit expected by AddPlanUncertaintyWithParameters for the isocenter shift ("cm" or "mm").</summary>
        public string UncertaintyShiftApiUnit = "";
        /// <summary>Field-specific perturbations (listed for manual entry).</summary>
        public double FieldShiftCm;
        public bool FieldBrainIndividualY;
        public bool FieldBrainIndividualZ;
        public bool FieldBrainPairedY;
        public bool FieldSpineY;
        public bool CalculateUncertaintyDoses;
        /// <summary>Tick the RO (robust) checkbox through the member the run-time API probe finds (reflection).
        /// false = RO stays a checklist item.</summary>
        public bool AllowReflectionForRobustFlag;
    }

    public enum HuMode { Fixed, NeighborMean, ReportOnly }

    public class HuOverride
    {
        /// <summary>'|' separated structure IDs.</summary>
        public string Structure = "";
        public HuMode Mode;
        /// <summary>Fixed mode: the HU to assign.</summary>
        public double Hu;
        /// <summary>NeighborMean mode: sample a shell NeighborInnerMm..NeighborOuterMm around the structure, inside @BRAIN or @BODY,
        /// using only voxels within NeighborLoHu..NeighborHiHu.</summary>
        public string RestrictTo = "";
        public double NeighborInnerMm;
        public double NeighborOuterMm;
        public double NeighborLoHu;
        public double NeighborHiHu;
        /// <summary>Ticked in tab 4 by default.</summary>
        public bool ApplyByDefault;
        public bool OverwriteExisting;
        public string Note = "";
    }

    public enum ObjectiveType { Upper, Lower, Mean }

    public class ObjectiveTemplate
    {
        /// <summary>Role token (@BR_OPT, @POST_OPT, @SPN_OPT, @VB_OPT, @BR_COMPO, @CTV) or '|' separated structure IDs.</summary>
        public string Structure = "";
        public ObjectiveType Type;
        public double VolumePercent;
        /// <summary>% of the total prescription.</summary>
        public double DosePercentRx;
        public double Priority;
        /// <summary>RO (robust) box; procedure: only for the OPT targets.</summary>
        public bool Robust;
    }
}
