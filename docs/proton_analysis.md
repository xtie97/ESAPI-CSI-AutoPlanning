# Proton planning code analysis

This is a static review of the supplied archive, whose project version is 1.7. It explains the actual implementation rather than treating README claims as proof of successful execution. The source root is `CsiAutoPlan/src/CsiAutoPlan`. No Eclipse execution, dose calculation, build against ESAPI, or clinical validation was performed for this review.

## What the five planning modules do

| Module | Main job | Important entry points |
|---|---|---|
| `Esapi/PlanBuilder.cs` | Create the ion plan, select calculation models, create scanning beams, assign field targets and range shifters, estimate snout positions | `CreatePlan` line 23; `CreateFields` line 237 |
| `Esapi/OptimizationBuilder.cs` | Write optimization objectives, add plan uncertainties, print field perturbations, optimize and calculate dose | `SetupObjectives` line 22; `SetupUncertainties` line 133; `OptimizeAndCalculate` line 188 |
| `Esapi/ApiCapabilities.cs` | Inspect the installed ESAPI assemblies for possible public robust objective members | `Probe` line 54; `TryAddRobust` line 136; `TrySetRobust` line 154 |
| `Esapi/RangeMarginBuilder.cs` | Replace fixed proximal and distal beam margins with margins calculated from each field's nominal range and SOBP | `Apply` line 34 |
| `Esapi/ExistingPlanReader.cs` | Read settings from existing proton plans of the currently open patient and report possible site configuration values | `Read` line 39 |

The session object `S` provides the selected structures, room, geometry, prescription, configuration, and log to every builder. Each builder stores the same session reference rather than opening its own patient.

## PlanBuilder in execution order

### Create the plan

`CreatePlan` line 23 first builds a list of patient support device IDs. This ID is an Eclipse device identifier; it does not itself insert a couch contour. Candidate order is the configured ID, the ID found in the same room's existing plans, IDs from other existing proton plans, and variants with a machine prefix removed (`SupportDeviceCandidates`, lines 110–128). Removal of a prefix is a compatibility heuristic, not a general rule for Eclipse IDs.

The method reuses the requested course or creates one with `Patient.AddCourse` (lines 33–42). It refuses to overwrite a plan with the requested ID (lines 43–47). It then calls `Course.AddIonPlanSetup` with the session structure set and a candidate support device ID (line 54). Only rejected candidate support IDs are retried here. If all fail, it attempts to remove a newly created empty course (lines 65–78). There is no general transaction that rolls back every modification elsewhere in the program.

The plan target is the selected physician CTV, or the draft union if the user chose the development path (lines 83–89). The prescription is written as dose per fraction and number of fractions, initially in Gy, with a cGy retry on any exception (lines 91–94). `1.0` in `SetPrescription` means a treatment percentage of 100%, not 1%. The log asks the planner to verify the primary reference point and treatment intent; the code does not set them explicitly (line 95).

`SetCalculationModels` runs next, then `SetOptimizationMode(MultiFieldOptimization)` sets MFO (lines 97–99).

### Select models and calculation options

Four calculation model categories are handled: proton volume dose, proton optimization, proton beam line modifiers, and proton scanning post processing (`SetCalculationModels`, lines 141–149). A configured model is tried first. An empty configuration can use a model read from the current patient's other proton plans. The optimizer fallback prefers a name containing `NUPO` (`TrySetModel`, lines 195–209). With no configured or harvested model, the code leaves the Eclipse default unchanged. A model rejected by Eclipse produces a log error and the method continues. A non-NUPO optimizer produces a manual checklist entry; it does not block optimization (lines 155–157).

The dose grid option is set to the configured 0.25 cm (lines 159–164; `SiteSettings.cs` lines 99–100). Spot spacing overrides are written to the beam line model with the four configured keys (lines 166–184; settings lines 104–109). `SetOption` resolves an exact key or a unique slash-delimited suffix before calling `SetCalculationOption` (lines 216–226). A successful return means that call was accepted; this code does not compare the returned current option value with the intended value. Exact keys are model and version dependent.

Changing `SpotSpacingCm` alone does not change the active configured override values. The four string values in `SpotSpacingOptions` are what get written when that collection is populated. `SpotSpacingCm` is used by the manual checklist fallback.

### Create beams

`CreateFields` starts with RPO and LPO at the brain isocenter, with the BR target (lines 240–249). Gantry angles are 265 and 95 degrees by default (`SiteSettings.cs` lines 120–121). It can add a posterior brain field on the BR target or a posterior field with its own POST target, depending on the selected geometry scheme (lines 250–262). All four supplied rooms default to the PostBrain scheme, so the global `AddPostBrainField = false` setting does not disable the posterior field for those rooms. Room-specific scheme selection takes precedence (`CsiSession.cs` lines 67–81).

For each spine segment, the method obtains its target structure, derives the actual superior–inferior midpoint from its contours, optionally rounds the offset from brain iso to a whole centimeter, and uses the session's common spine anterior–posterior position (lines 273–283; `IsoFromStructure`, lines 307–315). This can differ from the provisional geometry split, and the difference is logged.

`AddField` line 329 is the shared implementation for all treatment beams:

1. Estimate a snout position for the requested air gap; use the initial snout position if estimation is unavailable (lines 332–334).
2. Create a scanning beam with `ProtonBeamMachineParameters` and `AddModulatedScanningBeam` (`NewBeam`, lines 388–391).
3. If beam creation fails, try IDs harvested from existing plans of the same room, then possibly retry the initial snout position (lines 337–362).
4. Obtain editable beam parameters, set the field target, select the range shifter and `IN` setting, set weight factor 1.0, and call `ApplyParameters` (lines 394–404).
5. Set lateral, proximal and distal target margins and log readback values (lines 376–382).
6. Log the air gap calculation and its limitations (`ReportAirGap`, lines 472–513).

The editable parameter object is a copy. Changes to that copy do not affect the beam until `beam.ApplyParameters(p)` is called. Conversely, margins have their own directly writable properties.

Field limit checks use target superior–inferior extent, the isocenter, and the larger configured lateral margin (lines 318–326). They do not constitute a complete beam's eye view or collision check. Exceeding the limit logs an error but does not make `CreateFields` return false. Setup fields, DRRs, isocenter group verification, and collision checking are manual (lines 285–286).

### Air gap calculation

`PlanAirGap` requires BODY, couch angle zero, a positive snout-to-range-shifter offset, and positive snout footprint dimensions (lines 424–431). It considers BODY and structures matching the couch regular expression (lines 433–434), collects axial contours within the superior–inferior snout footprint (`Outlines`, lines 451–464), and passes them to the geometric calculator. It rejects computed snout positions outside a generic 100–800 mm plausibility window (line 445). It does not use room-specific travel limits there; Eclipse may reject a position and trigger the initial-position fallback.

The method controls the snout position, not a writable AirGap property. `ReportAirGap` checks that Eclipse retained the requested snout position, optionally compares the range shifter face offset when available, and reports the API air gap if available. It always requests subsequent collision verification. The room's offsets and dimensions are configuration assumptions to verify locally.

## RangeMarginBuilder

This step runs after beam creation (`App.cs` line 125). If range margins are disabled it leaves the fixed beam margins (`RangeMarginBuilder.cs` lines 39–42). Otherwise it saves each beam's original proximal and distal margins, temporarily sets both to zero, and reads nominal range and nominal SOBP width (lines 47–56). If readings are unusable, it can calculate the beam line once and read again (lines 57–69).

For each field with usable values, it writes:

```text
distal_mm = round_up(R_mm × percent / 100 + added_mm, step_mm)
proximal_mm = round_up(max(0, R_mm − SOBP_mm) × percent / 100 + added_mm, step_mm)
```

Defaults are 3.5%, 3 mm, and upward rounding to 0.1 mm (`SiteSettings.cs` lines 159–165; formulas in `Core/RangeMarginCalculator.cs`). These formulas come from the supplied code's stated margin-sheet implementation. The actual margin spreadsheet was not included and was not independently verified.

Unusable readings keep the original fixed margins and generate a manual item (lines 72–99). A `finally` block attempts to restore all unfinished beams after unexpected errors (lines 92–95). If the beam line was calculated with zero margins, it must be recalculated with the new margins; optimization step F does so (lines 106–110). `Apply` returns true even when some margins require manual completion (line 111).

Potential validation issue: the code recalculates only when R/SOBP are unusable, not whenever margins were changed. Whether a finite value immediately read after zeroing margins represents updated geometry or cached prior data must be checked in the installed Eclipse version.

## OptimizationBuilder

### Objectives

`SetupObjectives` can remove every existing objective (`OptimizationBuilder.cs` lines 26–27), and this is enabled by default (`SiteSettings.cs` line 173). Running step E against an existing plan can therefore remove hand-edited objectives. It also attempts to remove normal tissue parameters, swallowing removal exceptions, and reports NTO off when the setting is false (lines 29–34). The report alone does not prove that a failed removal succeeded.

The method uses the existing plan's total prescription when available instead of using only the dialog value (lines 36–46). Each template dose is a percentage of that total, converted to the plan's Gy/cGy unit (lines 57 and 99–104). Templates specify lower, upper, or mean objectives, relative volume percentage, priority, and requested robustness. These are optimizer settings, not proof that the final dose meets a clinical goal.

Structure role tokens are resolved in `Resolve` (lines 107–129): `@BR_OPT`, `@POST_OPT`, `@SPN_OPT`, `@VB_OPT`, `@BR_COMPO`, and `@CTV`. `@SPN_OPT` expands to the current per-field OPT structures, falling back to the combined spine OPT if none exist. Other strings are structure IDs or `|` separated aliases, resolved by `StructureTools.Find`. Absent or empty structures are skipped and logged (line 54); missing requested objectives do not block optimization.

The code first tries to create a robust objective using a discovered overload, then tries a discovered setter, otherwise creates an ordinary objective and prints a manual RO instruction (lines 64–95). Lens constraints in the settings are explicitly examples at 50% of Rx, not patient-specific EBRT Order constraints (`SiteSettings.cs` lines 195–209).

### Uncertainties and perturbations

`SetupUncertainties` adds plan-level uncertainties only if the plan has none at all (lines 139–157). Existing uncertainties are counted, not checked against requested shifts, range errors, scenario types, or completeness. The default helper generates 12 combinations: three axes × two shift signs × two range error signs. These are single-axis setup shifts combined with a range error, not all three-axis corner combinations. The procedure's embedded palliative screenshot shows this 12-scenario pattern.

Default `UncertaintyShiftApiUnit` is `cm`; the code optionally multiplies by 10 for a configured `mm` API unit. User coordinate vectors are converted for HFS to `(X, -Z, Y)` before writing (lines 145–150). The installed API documentation is the authority for units and coordinate conventions. Public ESAPI 17 documentation specifies centimeters for `AddPlanUncertaintyWithParameters` setup shifts; this is a notable exception to the widespread mm convention for VVector positions.

Field-specific perturbations are generated as descriptions only (lines 160–166). `ApiCapabilities.CanAddFieldUncertainty` unconditionally returns false (line 50). With two lateral brain fields and N posterior fields, the default list has `8 + 2 + 2N = 10 + 2N` scenarios: individual brain Y and Z shifts, paired brain Y shifts, and individual posterior Y shifts. A POST at the brain isocenter is classified with posterior fields and receives Y shifts only (`ClassifyBeams`, lines 173–184).

Beam classification uses the highest DICOM-z isocenter, a 1 mm tolerance, and gantry within 30 degrees of 180 as posterior. It is suitable only for the assumed layout and HFS orientation; changing beam types or angles requires revisiting this classifier.

### Optimization and dose

`OptimizeAndCalculate` tries to set a time option only if a nonempty key is configured (lines 191–197). The supplied key is empty, so the configured string `30` does not enforce a 30 minute time limit. It then calculates the beam line; restarts IMPT optimization with `MaxIterations`; post processes the final spot list and calculates dose; optionally calculates plan uncertainty doses; and reports DVH metrics (lines 199–222).

The default iteration limit is 300, and every run uses `RestartOptimization` (line 204). This is not a continuation run. The supplied procedure instead says to leave maximum iterations blank and use a 30 minute maximum time, then re-optimize as needed. The API's available options must be checked before designing a faithful implementation.

`CalculateUncertaintyDoses` is false by default. Optimization under uncertain scenarios and calculation/reporting of uncertainty dose distributions are separate operations. The current `ReportDose` queries nominal plan D98, D2, mean and maximum for selected CTV and generated targets (lines 225–252). It does not compute a robust acceptance decision, field junction smoothness, or a comprehensive OAR constraint report.

## ApiCapabilities and the meaning of reflection

Reflection lets C# inspect available types, methods and properties at runtime. Here it is used because the author wanted the binary to discover a robust objective setter that might exist in the installed API even when unavailable in the compile-time reference assemblies.

`Probe` searches public VMS.TPS types for names or parameter names containing `robust`, recognizes four setter shapes, and lists uncertainty-related methods for developer review (lines 54–107). It does not manufacture an API capability, prove that a name has the intended meaning, or prove that a script's license/security policy permits an invocation. Field perturbation candidates are only listed.

`TryAddRobust` inserts a `true` bool into a matching argument list and invokes it (lines 136–150). `TrySetRobust` tries the selected property, objective method, or setup method (lines 154–169). One failed robust invocation records `RobustFailure`, disabling later attempts for that run. Readback can be true, false, or unknown/null (`ReadRobust`, lines 173–177). A false result is flagged; an unknown result is not treated as verified failure.

A reporting weakness exists at `OptimizationBuilder.cs` line 89: the message says readback was verified whenever getter metadata was found, even when a particular read returned null because it failed or did not apply to that object type. Teach the distinction between detected member, successful invocation, and positively verified stored value. A future implementation should use exact documented APIs for the locally supported version and report those states separately.

There is also inconsistent historical commentary: `ApiCapabilities.cs` line 13 refers to ESAPI 16.1 reference assemblies while the project is labeled for Eclipse 18.x. The archive contains no ESAPI assemblies, so the actual build-time API version cannot be established from the source comments.

## ExistingPlanReader

The reader starts with the open ion plan, then other ion plans of the same patient, takes at most 20 plans containing treatment fields, and reports their settings (`ExistingPlanReader.cs` lines 43–48). It reads device IDs, selected calculation models and options, uncertainty shifts, treatment unit, technique, tolerance table, snout, range shifters, beam targets and margins (lines 69–158). It does not access another patient's data and does not write the patient.

For each room it keeps the first nonempty identifiers it encounters. It takes the first range shifter from a comma-separated list, so this does not represent a general multiple-range-shifter configuration (line 154). Missing geometry values for unknown rooms are placeholders. It prints C# configuration suggestions for review (lines 183–203); it does not edit `SiteSettings.cs` or persist those suggestions.

Unit inference is heuristic: median snout <100 suggests cm, median shift <1 suggests cm (lines 162–180). Those guesses assume expected clinical magnitudes and should not replace exact local API documentation. `CsiSession.UsePlanSettings` separately fills blank in-memory room IDs; configured nonempty IDs remain first choice until rejected by Eclipse (`CsiSession.cs` lines 140–163).

## Workflow issues to teach before any new function

1. **Use separate creation and optimization passes.** Step E creates a manual field perturbation list, but `App.Execute` can proceed directly to F with no opportunity to enter it (`App.cs` lines 135–141). The pre-run confirmation only asks the user to make sure settings exist (`MainForm.cs` lines 415–417). For a newly generated plan, run the required structure/plan/objective steps with F unchecked, review and complete manual items in Eclipse, then run F only. This also preserves manually edited objectives by leaving E unchecked on the second pass.
2. **Errors are not a universal stop signal.** `App.Guard` logs exceptions and returns false, but results for range margins, uncertainties and objectives are ignored (`App.cs` lines 125 and 138–141). Calculation-model errors, missing objective structures, and field-limit errors also need explicit preconditions if they are to prevent optimization.
3. **Existing plan matching is incomplete.** `FindExistingPlan` accepts the open ion plan when its plan ID matches without checking the requested course ID (`App.cs` lines 170–175). Reused IDs such as INITIAL can occur in different courses. There is no explicit plan structure set UID versus session structure set UID check before objectives are built. Future existing-plan work should require exact patient, course, plan and structure set identity.
4. **There is no complete rollback.** An exception can leave a new plan or partially edited objectives/structures. Review the log and the Eclipse state after failure. `BeginModifications` enables writes; it is not an all-or-nothing transaction managed by this code.

These are static code findings and development priorities, not proof of a dose error in a plan.

## Where new features belong

For a new configurable numeric setting, add its typed field to `Settings/SettingsModel.cs` and its explicit site value to `SiteSettings.cs`. Use unit-bearing names such as `Mm`, `Cm`, `Gy`, or `Percent`. For geometry/math, use an ESAPI-independent Core helper and independently test it. For patient reads/writes, use a builder under Esapi and call it through the orchestrator on the ESAPI thread. For a user choice, add a session field and UI control; copy the UI value to the session in `MainForm.OnRun`. For a new clinical workflow such as palliative posterior-only CSI, introduce an explicit workflow setting and separate its target selection, beam recipe, objectives and uncertainty defaults rather than just changing the prescription.

Good first additions are read-only reports: list beam IDs and targets; show field margin readback; show exact plan/structure set identity; or add a nominal DVH export. A preflight gate before step F can check required model selection, plan identity, expected target structures, and completion of manual settings, but it needs designed success/failure results instead of relying on log text. Field perturbation automation requires an exact supported per-field API and verification of per-beam shifts; discovering a method with `Uncertainty` in its name is insufficient.

## Official references consulted

- [Varian ESAPI 17 PlanSetup reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.PlanSetup.html): plan uncertainty parameters and calculation options.
- [Varian ESAPI 17 IonPlanSetup reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.IonPlanSetup.html): IMPT optimization, MFO, and post processing.
- [Varian ESAPI 16.1 IonBeam reference](https://docs.developer.varian.com/api/16.1/VMS.TPS.Common.Model.API.IonBeam.html): units for the beam properties and editable parameter pattern. Use the installed version's local help to resolve any release differences.
