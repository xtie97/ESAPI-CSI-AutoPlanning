using CsiAutoPlan.Settings;

namespace CsiAutoPlan
{
    /// <summary>
    /// ===================================================================================================
    ///  THE ONE PLACE for the site settings of the CSI script: treatment rooms, geometry rules, plan and
    ///  field settings, range margins, robust optimization, objectives and HU override rules.
    ///  Structures (names, margins, how they are built) are in StructureRecipes.cs.
    ///  There is no settings file: these values are compiled into the plugin. After a change: raise
    ///  ScriptVersion in CsiAutoPlan.csproj, build, run the verification checks, test on the non-clinical system, approve (README 12.2).
    ///  Units: mm unless the name says Cm / Percent / Gy.
    /// ===================================================================================================
    /// </summary>
    public static class SiteSettings
    {
        /// <summary>A fresh copy of the settings for one run.</summary>
        public static CsiSettings Create()
        {
            return new CsiSettings
            {
                // ---------------------------------------------------------------------------------------------
                // TREATMENT ROOMS. IDs exactly as Eclipse knows them, read from approved clinical CSI plans
                // (RT Ion Plan exports; tools/DicomRoomSummary prints these lines). Proton 1, 3 and 4: plan and
                // fields created with these IDs on the Eclipse 18.1 test system.
                //   HalfFieldLimitMm      max |spot - iso| sup-inf: 170 Proton 1/3, 200 Proton 4/5
                //                         (clinical maximum 16.0 / 18.5 / 17.5 cm on P1 / P4 / P5)
                //   InitialSnoutPosition  used only if the air-gap geometry cannot be computed (API snout unit)
                //   SnoutToRangeShifterMm snout position - isocenter-to-range-shifter distance:
                //                         85 (PBS Snout, P1/P3), 65 (PBS Snout P5, P4/P5)
                //   SnoutSizeXcm/Ycm      snout footprint (Field Properties > Snout: Max X / Max Y)
                //   SpineScheme           default field layout (the dialog can change it per patient):
                //     PostBrain     RPO/LPO/POST at the brain iso on the brain target, separate UP/LO spine isos
                //                   (all eight P1/P4/P5 plans)
                //     PostExtended  RPO/LPO + POST at the brain iso extended down the spine, lower iso(s) below
                //                   (the one older P3 plan)
                //     Separate      RPO/LPO at the brain iso, separate UP/MID/LO spine isos (procedure text)
                //   Tolerance: "CAM CNS" in 5 of 8 P1/P4/P5 plans ("CAM CNS PBS" in one P4 and one P5 plan);
                //   the Proton 3 value is from its one clinical plan - confirm.
                //   PatientSupportDeviceId: if Eclipse rejects it, the device of the patient's existing plans and the
                //   ID without the machine prefix ("Table") are tried; the log names the one that worked.
                // ---------------------------------------------------------------------------------------------
                Machines =
                {
                    new MachineOption { DisplayName = "Proton 1 (IBA)", MachineId = "Proton_1", Enabled = true, TechniqueId = "MODULAT_SCANNING",
                        ToleranceId = "CAM CNS", SnoutId = "PBS Snout", RangeShifterId = "RS_Block", PatientSupportDeviceId = "Proton_1-Table",
                        HalfFieldLimitMm = 170, InitialSnoutPosition = 30, SpineScheme = SpineSchemes.PostBrain,
                        SnoutToRangeShifterMm = 85, SnoutSizeXcm = 37, SnoutSizeYcm = 45 },
                    new MachineOption { DisplayName = "Proton 3 (IBA)", MachineId = "Proton_3", Enabled = true, TechniqueId = "MODULAT_SCANNING",
                        ToleranceId = "CAM Head", SnoutId = "PBS Snout", RangeShifterId = "RS_Block1", PatientSupportDeviceId = "Proton_3-Table",
                        HalfFieldLimitMm = 170, InitialSnoutPosition = 30, SpineScheme = SpineSchemes.PostBrain,
                        SnoutToRangeShifterMm = 85, SnoutSizeXcm = 37, SnoutSizeYcm = 45 },
                    new MachineOption { DisplayName = "Proton 4 (IBA)", MachineId = "Proton_4", Enabled = true, TechniqueId = "MODULAT_SCANNING",
                        ToleranceId = "CAM CNS", SnoutId = "PBS Snout P5", RangeShifterId = "RS_Block", PatientSupportDeviceId = "Proton_4-Table",
                        HalfFieldLimitMm = 200, InitialSnoutPosition = 30, SpineScheme = SpineSchemes.PostBrain,
                        SnoutToRangeShifterMm = 65, SnoutSizeXcm = 37, SnoutSizeYcm = 45 },
                    new MachineOption { DisplayName = "Proton 5 (IBA)", MachineId = "Proton_5", Enabled = true, TechniqueId = "MODULAT_SCANNING",
                        ToleranceId = "CAM CNS", SnoutId = "PBS Snout P5", RangeShifterId = "RS_Block", PatientSupportDeviceId = "Proton_5-Table",
                        HalfFieldLimitMm = 200, InitialSnoutPosition = 30, SpineScheme = SpineSchemes.PostBrain,
                        SnoutToRangeShifterMm = 65, SnoutSizeXcm = 37, SnoutSizeYcm = 45 },
                },
                DefaultMachineId = "Proton_4",           // room pre-selected in the dialog

                // ---------------------------------------------------------------------------------------------
                // GEOMETRY: isocenters and the sup-inf split into field targets
                // ---------------------------------------------------------------------------------------------
                Geometry = new GeometryOptions
                {
                    UseUserOriginAsBrainIso = true,      // procedure: brain iso (0,0,0) = user origin placed at simulation
                    ShoulderClearanceMm = 20,            // brain field target ends >= 2 cm above the shoulders
                    MinFieldTargetOverlapMm = 20,        // procedure: >= 2 cm overlap of neighbouring field targets
                    PreferredFieldTargetOverlapMm = 50,  // aimed for when the field lengths allow it (never adds a field)
                    PosteriorAtBrainIso = true,          // only for a room without SpineScheme
                    FieldLimitSafetyMm = 5,              // headroom inside the scanning limit (absorbs whole-cm iso rounding)
                    MaxSpineFields = 3,
                    RoundSpineIsoToWholeCm = true,
                    SpineIsoAnteriorOffsetFromCanalMm = 20,  // spine iso A-P: thecal-sac centre + 2 cm anterior, whole cm
                    SpineIsoFromInvolvedVb = false,      // VB contours include posterior elements: canal rule matched the clinical plan
                    ShoulderSearchWindowMm = 200,
                    ShoulderRiseFraction = 0.3,
                    MinBodyContourAreaMm2 = 400,
                },

                // ---------------------------------------------------------------------------------------------
                // PLAN
                // ---------------------------------------------------------------------------------------------
                Plan = new PlanOptions
                {
                    CourseIdFormat = "C{0}-CSI",
                    PlanId = "INITIAL",
                    DosePerFractionGy = 1.8,             // dialog defaults - always confirm against the EBRT Order
                    NumberOfFractions = 20,
                    // Empty = the models of this patient's existing proton plans (optimizer: a NUPO model if any), else
                    // Eclipse's defaults (PCS_15603 for all four on the 18.1 test system - the optimizer must be NUPO).
                    VolumeDoseModel = "",
                    OptimizationModel = "",
                    BeamLineModifierModel = "",
                    PostProcessingModel = "",
                    DoseGridCm = 0.25,
                    DoseGridOptionKey = "General/CalculationGridSizeInCM",
                    SpotSpacingCm = 0.5,
                    // Spot spacing 0.5 cm (procedure); keys as listed by the PCS_15603 beam-line model on the 18.1 test
                    // system. A rejected key makes the run list the model's options.
                    SpotSpacingOptions =
                    {
                        new CalcOption { Key = "ModulatedScanning/Beamline/OverwriteSpotSpacingInScanningDirection", Value = "True" },
                        new CalcOption { Key = "ModulatedScanning/Beamline/SpotSpacingInScanningDirectionInCM", Value = "0.5" },
                        new CalcOption { Key = "ModulatedScanning/Beamline/OverwriteSpacingBetweenScanningLines", Value = "True" },
                        new CalcOption { Key = "ModulatedScanning/Beamline/SpacingBetweenScanningLinesInCM", Value = "0.5" },
                    },
                    MaxOptimizationTimeOptionKey = "",   // optional NUPO time-limit key (see "Optimizer options" in the report)
                    MaxOptimizationTimeValue = "30",
                },

                // ---------------------------------------------------------------------------------------------
                // FIELDS
                // ---------------------------------------------------------------------------------------------
                Beams = new BeamOptions
                {
                    BrainRpoGantry = 265,                // HFS, IEC 61217: 265 = RPO, 95 = LPO, 180 = PA
                    BrainLpoGantry = 95,
                    SpineGantry = 180,
                    CouchAngle = 0,
                    AddPostBrainField = false,           // Separate layout only: extra PA field at the brain iso
                    PostBrainGantry = 180,
                    // Field target margins. Lateral 0.5 cm on all fields. Proximal / distal: computed per field from R and
                    // SOBP (RangeMargins below); these values are used only if that is off or a field has no range.
                    BrainLateralMarginMm = 5,
                    BrainProximalMarginMm = 5,
                    BrainDistalMarginMm = 5,
                    SpineLateralMarginMm = 5,
                    SpineProximalMarginMm = 5,
                    SpineDistalMarginMm = 5,
                    // Air gap as Eclipse defines it (Field Properties > Snout > Air Gap): smallest distance from the
                    // range-shifter face to the BODY inside the snout footprint. Procedure and clinical plans: 5 cm.
                    // The plugin computes the snout position for it before each field is created.
                    LateralAirGapCm = 5,
                    PosteriorAirGapCm = 5,
                    SnoutPositionApiUnit = "cm",         // unit of the snout position in the API
                    RangeShifterSetting = "IN",
                    FieldTargetIsCompo = true,           // true: (D)PBSTV_xx_COMPO; false: the OPT structures
                    // Field IDs as in the clinical plans: 01 RPO, 02 LPO, 03 POST, 04 POST UPPER, 05 POST LOWER
                    BrainRpoId = "01 RPO",
                    BrainLpoId = "02 LPO",
                    BrainPostId = "POST",
                    SpineIdFormat = "{0:00} POST {1}",
                    SuffixUp = "UPPER",
                    SuffixMid = "MID",
                    SuffixMid2 = "MID2",
                    SuffixLow = "LOWER",
                },

                // ---------------------------------------------------------------------------------------------
                // RANGE MARGINS (PBSTV margin sheet), per field, from Eclipse's nominal range R and SOBP width:
                //   distal margin   = R x 3.5% + 3 mm
                //   proximal margin = (R - SOBP) x 3.5% + 3 mm
                // Set as the field's proximal / distal target margin (Field Properties > Target Margin).
                // ---------------------------------------------------------------------------------------------
                RangeMargins = new RangeMarginOptions
                {
                    Enabled = true,
                    RangeUncertaintyPercent = 3.5,
                    AddedMarginMm = 3,
                    RoundUpToMm = 0.1,                   // Eclipse shows margins in cm with two decimals
                    CalculateBeamLineIfNeeded = true,    // get R / SOBP from a beam-line calculation if Eclipse has none yet
                },

                // ---------------------------------------------------------------------------------------------
                // ROBUST OPTIMIZATION
                // ---------------------------------------------------------------------------------------------
                Optimization = new OptimizationOptions
                {
                    RemoveExistingObjectives = true,
                    ProtonNormalTissueObjective = false, // procedure: NTO off
                    MaxIterations = 300,
                    PlanUncertainties = true,            // +/-0.3 cm on each axis x +/-3.5 % range = 12 scenarios (U1-U12)
                    PlanIsoShiftCm = 0.3,
                    PlanRangeErrorPercent = 3.5,
                    UncertaintyShiftApiUnit = "cm",      // unit AddPlanUncertaintyWithParameters expects for the shift
                    FieldShiftCm = 0.3,                  // field-specific perturbations (listed for manual entry)
                    FieldBrainIndividualY = true,
                    FieldBrainIndividualZ = true,
                    FieldBrainPairedY = true,
                    FieldSpineY = true,
                    CalculateUncertaintyDoses = false,
                    // Tick the RO (robust) box of the objectives marked Robust = true through the member the run-time
                    // API probe finds (first log line "RO (robust) checkbox: ..."). false = RO stays a checklist item.
                    AllowReflectionForRobustFlag = true,
                },

                // ---------------------------------------------------------------------------------------------
                // NUPO OBJECTIVES. Structure = role token (@BR_OPT, @POST_OPT, @SPN_OPT = the spine field OPTs,
                // @VB_OPT, @CTV, @BR_COMPO; see StructureRecipes.cs) or '|' separated structure IDs. Tokens whose
                // structure does not exist in the current split are skipped. Dose = % of the total prescription.
                // Robust = true only for the OPT targets (procedure). Target levels are starting values; the OAR
                // lines are EXAMPLES - replace them with the EBRT Order constraints.
                // ---------------------------------------------------------------------------------------------
                Objectives =
                {
                    new ObjectiveTemplate { Structure = "@BR_OPT",   Type = ObjectiveType.Lower, VolumePercent = 100, DosePercentRx = 100, Priority = 150, Robust = true },
                    new ObjectiveTemplate { Structure = "@BR_OPT",   Type = ObjectiveType.Upper, VolumePercent = 0,   DosePercentRx = 105, Priority = 120, Robust = true },
                    new ObjectiveTemplate { Structure = "@POST_OPT", Type = ObjectiveType.Lower, VolumePercent = 100, DosePercentRx = 100, Priority = 150, Robust = true },
                    new ObjectiveTemplate { Structure = "@POST_OPT", Type = ObjectiveType.Upper, VolumePercent = 0,   DosePercentRx = 105, Priority = 120, Robust = true },
                    new ObjectiveTemplate { Structure = "@SPN_OPT",  Type = ObjectiveType.Lower, VolumePercent = 100, DosePercentRx = 100, Priority = 150, Robust = true },
                    new ObjectiveTemplate { Structure = "@SPN_OPT",  Type = ObjectiveType.Upper, VolumePercent = 0,   DosePercentRx = 105, Priority = 120, Robust = true },
                    new ObjectiveTemplate { Structure = "@VB_OPT",   Type = ObjectiveType.Lower, VolumePercent = 100, DosePercentRx = 100, Priority = 120, Robust = true },
                    new ObjectiveTemplate { Structure = "@VB_OPT",   Type = ObjectiveType.Upper, VolumePercent = 0,   DosePercentRx = 105, Priority = 100, Robust = true },
                    new ObjectiveTemplate { Structure = "Lens_L|LENS_L|Lens L|Lt_Lens", Type = ObjectiveType.Upper, VolumePercent = 0, DosePercentRx = 50, Priority = 40, Robust = false },
                    new ObjectiveTemplate { Structure = "Lens_R|LENS_R|Lens R|Rt_Lens", Type = ObjectiveType.Upper, VolumePercent = 0, DosePercentRx = 50, Priority = 40, Robust = false },
                },

                // ---------------------------------------------------------------------------------------------
                // HU OVERRIDES (step B, tab 4). Mode: Fixed | NeighborMean (mean HU in a shell NeighborInnerMm..
                // NeighborOuterMm around the structure, inside @BRAIN or @BODY, within NeighborLoHu..NeighborHiHu) |
                // ReportOnly (checklist item with the note; ESAPI sets HU, not RED).
                // ---------------------------------------------------------------------------------------------
                HuOverrides =
                {
                    new HuOverride { Structure = "(D)BB", Mode = HuMode.Fixed, Hu = -1000, ApplyByDefault = true, Note = "BBs absent at treatment" },
                    new HuOverride { Structure = "(D)ARTIFACT_BONE", Mode = HuMode.NeighborMean, RestrictTo = "@BRAIN", NeighborInnerMm = 2, NeighborOuterMm = 7,
                                     NeighborLoHu = -100, NeighborHiHu = 150, ApplyByDefault = true, Note = "average HU of neighbouring brain (~35-45)" },
                    new HuOverride { Structure = "(D)ARTIFACT_CLIPS", Mode = HuMode.NeighborMean, RestrictTo = "@BODY", NeighborInnerMm = 2, NeighborOuterMm = 7,
                                     NeighborLoHu = -200, NeighborHiHu = 300, ApplyByDefault = true, Note = "average HU of neighbouring tissue" },
                    new HuOverride { Structure = "(D)CLIPS", Mode = HuMode.ReportOnly, Note = "Ti 4450 / SS 4500; none needed if < 3071 HU" },
                    new HuOverride { Structure = "(D)SHUNT|(D)Catheter", Mode = HuMode.ReportOnly, Note = "override as tissue/fluid (clinical example: RED 1.01)" },
                    new HuOverride { Structure = "(D)AIR", Mode = HuMode.ReportOnly, Note = "bowel gas -> water-equivalent (clinical example: RED 1.01)" },
                    new HuOverride { Structure = "(D)ARTIFACT_muscle", Mode = HuMode.ReportOnly, Note = "muscle (clinical example: RED 1.05)" },
                    new HuOverride { Structure = "(D)ARTIFACT_FAT", Mode = HuMode.ReportOnly, Note = "fat (clinical example: RED 0.95)" },
                },

                LogDirectory = @"%TEMP%\CsiAutoPlan",
            };
        }
    }
}
