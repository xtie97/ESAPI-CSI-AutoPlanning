# CSI core and structure code analysis

This note describes the supplied source as it actually executes. Paths below are relative to `CsiAutoPlan/src/CsiAutoPlan`. It does not establish that the algorithms, room settings or clinical choices have been commissioned. Comments claiming prior clinical comparisons or unit tests are claims in the supplied source; the archive contains no test project or test results.

## The main mental model

`SiteSettings.Create()` returns a fresh set of per-run settings. `CsiSession` joins those settings to the open Eclipse patient and to the choices made in the dialog. The `Core` classes do calculations with ordinary numbers; they do not write anything to Eclipse. `RecipeEngine` turns those calculated cuts and the selected structures into actual Eclipse structures. `StructureBuilder` runs those recipes and applies HU assignments. `StructureTools` holds the small repeated ESAPI operations.

There are three different kinds of margin, which should not be confused:

1. `StructureRecipes.BrainMargins` / `SpineMargins` expand a section of the CTV to make a CSI working volume. They are anatomical R/L/A/P/I/S distances in mm.
2. `SiteSettings.Beams` supplies field-target lateral, proximal and distal margins. These are properties of a beam, separate from the structure volume.
3. `RangeMarginCalculator` can replace each beam's proximal/distal margins using that field's nominal range and SOBP. These are not equivalent to a simple isotropic CTV expansion or to plan robustness settings.

## Units and coordinates

`MarginSet` (`Settings/SettingsModel.cs`) defines DICOM patient directions as Right = -x, Left = +x, Anterior = -y, Posterior = +y, Inferior = -z, Superior = +z. These assumptions are restricted to the HFS axial use implemented here. Geometry and `VVector` positions in these classes use mm. The dialog helpers use cm and Eclipse display labels, where Y is superior/inferior and Z is anterior/posterior. In particular, `CsiSession.SpineIsoZcmOverride` is the Eclipse display Z shift; internally it changes DICOM y, not DICOM z.

`CsiSession.ToUserYcm(z)` = `(z - UserOrigin.z) / 10`; `FromUserYcm(ycm)` reverses this. `IsoOffsetYcm(z)` uses the brain isocenter as reference. `IsoOffsetZcm(y)` = `-(y - BrainIso.y) / 10`, with positive displayed Z anterior. `SpineIso(isoZ)` returns `(BrainIso.x, SpineIsoY, isoZ)`, so spine isocenters keep the brain's left/right coordinate.

`Core/PerturbationPlanner` uses ordinary arrays in **Eclipse user coordinate cm**, not DICOM VVectors. Its field shift array is `[LR, SI, AP]`; its plan array adds a fourth item for range error percent. Do not hand these arrays to a new API method without checking that method's coordinate and unit contract.

## Settings and defaults

`SiteSettings.cs:18` is the factory to edit for rooms, prescription defaults, beam properties, robustness, objective templates and HU rules. `StructureRecipes.cs:18` is the normal editing surface for input aliases, structure names, structure margins and Boolean operations. These settings are compiled into the DLL; changing them requires a rebuild and version change. `Settings/SettingsModel.cs` mainly defines the shape of the settings classes. Adding a new setting requires a field/property in the appropriate model and an explicit value in the factory; if patient-adjustable, it also needs session/UI wiring.

All four supplied rooms use `SpineSchemes.PostBrain`, including Proton 3 despite a comment discussing a historical extended POST example. The current default room is Proton_4. P1/P3 half-field limits are 170 mm, P4/P5 are 200 mm. The archive has no ProBeam360 room entry. These are site-specific identifiers and assumptions, not a machine catalog queried from ESAPI. `MachineOption.MissingIds()` only checks that configured IDs are nonempty, not that Eclipse accepts them.

Other defaults include 20 x 1.8 Gy = 36 Gy (editable in dialog and sometimes prefilled from the open plan), INITIAL, C-number-CSI naming, 0.25 cm dose grid, 0.5 cm spot spacing, 5 cm requested air gap, 5 mm lateral beam margins, range-derived proximal/distal margins enabled, 3.5% + 3 mm range margin formula rounded upward to 0.1 mm, and 300 maximum optimizer iterations. The 30-minute option key is empty, so the literal `"30"` value alone does not set an optimization time limit. Verify all prescribing choices against the actual order; the defaults are not an inferred prescription.

Robust defaults are plan scenarios enabled with 0.3 cm shifts and 3.5% range error; field perturbation specifications use 0.3 cm shifts with individual brain Y and Z, paired brain Y and individual spine Y enabled. `CalculateUncertaintyDoses` is false. `RemoveExistingObjectives` is true. The lens objectives are explicitly examples at 50% total prescription, priority 40, nonrobust; they are not a complete EBRT constraint set.

`CsiSettings.Validate()` checks a subset of syntactic/numeric requirements. It does not prove the configuration clinically correct. Its results are shown/logged by the caller; these warnings are not a universal execution stop. `IncompleteMachines()` reports enabled rooms with missing IDs. `ExpandPath()` expands values such as `%TEMP%`. No external settings file is loaded.

## CsiSession method guide

- Constructor (`Esapi/CsiSession.cs:184`): captures context/patient/structure set/image, copies configured defaults, picks the default enabled room, applies the room's scheme, and initializes prescription/margins. Initial flags `DoDraftStructures`, `DoHuOverrides`, `DoFieldTargets`, `DoPlan`, `DoObjectives`, `DoOptimize` are all true at source level; the UI may alter them.
- `Analyze()` (`:206`): clears blockers; requires patient, structure set and image; checks axis-aligned imaging; finds aliases for BODY, brain, canal and VBs; picks a CTV; uses draft union if none exists; emits a couch checklist item if no matching nonempty structure name exists. If an ion plan is open, it can copy that plan's fraction count and dose per fraction. It builds HU rows, suggests course name, then reevaluates geometry.
- `PickCsiCtv()` (`:253`): considers nonempty structures matching `^CTV_\d+`, case insensitive. With several matches it selects the largest SI contour extent, not the highest prescription or volume. The regex is not end-anchored; names such as `CTV_3600_copy` can match. The selection is a heuristic, not an approval/status check.
- `BrainSource()` / `CanalSource()` (`:269`): prefer the existing recipe-role draft structures over the raw selected AI sources. Therefore picking a different AI brain does not automatically replace an already present `(D)CTV_BRAIN` used for geometry.
- `Reevaluate()` (`:273`): requires HeadFirstSupine and selected BODY, brain source and canal source; requires physician CTV unless draft mode selected. If there are no blockers, measures anatomy.
- `ToGy()` (`:285`): converts cGy to Gy, otherwise returns the numeric dose unchanged. It does not add an RBE conversion or validate every possible dose unit.
- `MeasureAnatomy()` (`:290`): measures contour extents, detects shoulders, computes reference coordinates and AP target depth, then recomputes the split. If shoulder detection fails it substitutes `brainBottomZ - 100 mm` and logs a warning requesting manual input; this is not itself a hard blocker.
- `RecomputeCtvExtent()` (`:335`): actual selected CTV extents, or max/min of brain plus canal for draft source. It assumes nonempty measured lists, so malformed or noncontoured inputs can fail at Min/Max.
- `Recompute()` (`:351`): recalculates computed brain iso at brain bounding-box LR/AP center and halfway between brain top and shoulder top in SI. It calculates spine AP iso, constructs `GeometryInput`, runs the split and computes the alternate layout comparison. Computed midpoint is only a geometric proposal.
- `SchemeFor()` / `Scheme` / `ApplySchemeDefaults()` (`:67`): per-room recognized scheme wins. Empty/unrecognized room setting falls back to global flags. Changing the selected room resets its scheme defaults. PostExtended takes precedence when deriving the current name.
- `FieldTargetProblems()` (`:101`): checks required field-target structures exist, have contours and have SI endpoints within `5 + 2 * sliceThickness` mm of planned endpoints. This is useful stale-split detection, but not a full volume identity/coverage test and does not compare every contour.
- `UsePlanSettings()` (`:140`): fills empty configured snout, tolerance, range-shifter and patient-support IDs from harvested plans on the same unit, logs differences, keeps nonempty configured values. `PlanMachine()` returns harvested settings; `SameId()` treats an empty harvested comparison value as matching.
- `SuggestCourseId()` (`:409`): reuses the first existing course whose ID ends in `-CSI`; otherwise extracts leading numbers from `C...` course names and uses max + 1 with configured format. It does not select a course by intent or clinical stage.
- `BuildHuRows()` (`:427`): includes only structures actually found by rule aliases. ReportOnly rows are not applied. Existing assigned HU unchecks a row unless `OverwriteExisting` is true. This is initial UI behavior; the applying method trusts the row's later Apply selection.

AP spine isocenter: by default average thecal-sac contour Y centers below shoulders, subtract 20 mm to move anteriorly, then round the shift relative to brain iso to nearest whole cm. If configured, involved-VB Y centers below shoulders are used instead. A user AP override takes precedence. This is a bounding-box center heuristic, not a segmented vertebral-body centroid or collision solution.

## Geometry algorithm in detail

`Core/CsiGeometryPlanner.cs` has data classes `GeometryInput`, `FieldSegment`, `GeometryPlan`; `Plan()` begins at line 135, `PlanCore()` at 152, `ChooseIso()` at 261 and `RoundToCm()` at 276.

The planner is 1D in superior/inferior position. It does not prove LR field coverage, 3D beam-eye-view clearance or dose quality. `Geometry.All()` yields BR, optional POST, then lower spine segments. `Geometry.Chain()` uses POST instead of BR as the superior chain member in PostExtended, because that target hands off down the spine. `IsocenterCount` is `1 + Spine.Count`; field count is a separate session calculation, so two oblique beams at one brain iso are not two isocenters.

Session reserves one CT slice thickness against field size and adds one slice thickness to the requested minimum overlap. Define Hlimit = machine half-field limit, Mspot = larger configured brain/spine lateral beam margin, A = CT slice allowance, S = safety reserve. Then `hardHalf = Hlimit - Mspot - A`; `usableHalf = hardHalf - S`. Actual numerical values therefore depend on image spacing, not only room.

For BR, upper PBSTV edge = CTV top + brain superior margin. If this exceeds `brainIsoZ + hardHalf`, an error asks for a more superior brain iso. Lower PBSTV edge = max(shoulder top + shoulder clearance, brainIsoZ - usableHalf). The lower CTV cutting plane is lower PBSTV edge + brain inferior margin. Empty/inverted brain extent is an error.

PostBrain does not create a separate POST segment in this geometric list; its additional posterior beam shares the BR target. PostExtended creates a POST segment at brain iso, with the BR upper PBSTV edge and inferior edge = max(brainIsoZ - usableHalf, CTV bottom - spine inferior margin). It may eliminate lower spine isocenters if it reaches the CTV bottom. Separate uses only lateral BR beams at brain iso plus separate posterior spine isocenters.

For the lower chain, desired first spine upper PBSTV edge = previous target bottom + overlap. Remove superior structure margin to obtain first CTV upper cut. Let L be remaining CTV length, mS/mI the spine SI margins, O the desired PBSTV overlap. Maximum CTV segment length is `2*usableHalf - mS - mI`; overlap between CTV cuts is `c = O - mS - mI`. For k equal-length segments, each CTV segment length is `(L + (k-1)*c)/k`. Choose the first k up to configured MaxSpineFields for which that length fits. Forced count bypasses this search but still checks resulting length. Current name tables cover at most four lower spine segments; config default max is three.

Each lower target is centered at its PBSTV midpoint. If whole-cm rounding is enabled, round the offset from brain iso using AwayFromZero at exact halves. Check that neither target endpoint exceeds hardHalf after rounding. The minimum overlap plan is computed first. If preferred overlap is larger (default 50 mm), retry that preferred amount downward in 1 mm increments while fixing the same number of lower spine fields; return the largest feasible overlap. It never adds another field merely to obtain preferred overlap.

Example of unit conversion: a midpoint 153 mm inferior to brain iso rounds to a -150 mm SI shift, equivalent to -15.0 cm in the corresponding SI display helper. This is not a new clinical placement recommendation.

## StructureRecipes and Boolean vocabulary

Input aliases (`StructureRecipes.cs:25`) use `|` as first nonempty alternative, case insensitive. `PhysicianCtvRegex` and `CouchRegex` use regex syntax. These are different matching methods.

Current anatomical margins (`:39`) are brain R/L/A/P/I/S = 6/6/4/4/3/3 mm; spine = 4/4/4/4/3/3. CropToBody is true with 0 mm inset. InvVbCropMm is 5. SplitSpineOptPerField is true. FieldCodes map geometric LO to structure suffix LOW, so the default is `(D)PBSTV_LOW_COMPO`, not LO. Changing field names with string replacements elsewhere is unnecessary when role lookup is used correctly.

`StructureRecipe.Once()` saves a volume once per step. `PerFieldStructure()` saves one per geometric segment and replaces `{F}` in IDs. `PerFieldVolume()` creates an in-memory intermediate SegmentVolume; it is not a saved structure. `Role` links a recipe to the rest of the program. Use `Role.None` for an additional evaluation structure so it does not displace the sole field-target or optimization role. Recipe order matters: `Get()` reads only a value built earlier during this run; `Find()` looks up an existing structure in the structure set. `Get()` cannot find a future recipe. `null` means no volume; in step C, that can remove an old saved derived structure with that ID.

`And()` is intersection, `Or()` union, `Sub()` difference, `Xor()` symmetric difference. `Margin(mm)` expands for positive mm and shrinks for negative mm; API size constraints should be checked in installed help. `AddMargins()` supports directional R/L/A/P/I/S margins. A SegmentVolume is a geometry value; it becomes a patient structure when assigned to `Structure.SegmentVolume`.

Actual supplied recipes:

1. Step A (`:84`): copy selected AI brain to `(D)CTV_BRAIN` and selected AI canal to `(D)CTV_THECALSAC`. Existing nonempty structures are kept unless overwrite enabled. These are starting contours requiring editing.
2. Draft step C (`:89`): `(D)CTV_CSI_DRAFT = brain OR thecal sac`; it is only built when draft source is used.
3. Per-field intermediate (`:94`): `{F}_CSI = CropToBody(AddMargins(CTV AND field CTV slab, brain margins for BR, spine otherwise))`. Extended POST additionally unions its superior brain part with brain margins.
4. Field target (`:102`): COMPO = CSI working volume OR (involved VBs AND field target slab). The VB addition receives no extra margin. BODY crop is applied to CSI first; the later VB union is not explicitly recropped.
5. Per-field OPT (`:106`): **OPT equals the CSI working volume**, not COMPO minus VBs. Therefore CSI voxels overlapping involved VBs remain in OPT. Brain and POST OPTs are made; lower spine OPTs are made only when split option is true.
6. Combined SPN_OPT (`:112`): union of lower-spine CSI parts, only when split option is false. `SpineFields` excludes extended POST; in that scheme the POST_OPT covers its extended target separately.
7. INV_VB_OPT (`:117`): VBs minus 5 mm expansion of union of all CSI parts. With no involved VB source this recipe returns null and can remove an old `(D)INV_VB_OPT`.

The procedure's text specifying OPT as COMPO cropped away from involved VBs is materially different from item 5. A possible code expression after clinical clarification is `var v = f.Get("{F}_CSI"); return f.Vb == null ? v : v.Sub(f.Vb);`. The single SPN_OPT recipe would need an equivalent crop too. Do not silently change this based only on a code tutorial; physician/physics intent determines the appropriate Boolean definition.

## RecipeEngine and StructureTools internals

`RecipeBook.Validate()` (`Esapi/RecipeEngine.cs:100`) checks field-code shape, uniqueness, `{F}` usage, DICOM types, build delegates, duplicate IDs, a sole per-field FieldTarget role and per-field FieldOpt role. It checks saved IDs against 64 characters, but that local check is not proof every installed API/export path accepts every ID. It does not analyze lambda dependencies or prove build results nonempty.

`RunStepA()` (`:263`) tracks sources read while each lambda executes. If mixed high/default-resolution sources are encountered, it reruns with high-resolution working copies. Step C (`:301`) enables high-resolution handling if any principal CTV/BODY/VB/brain/canal input is high-res; default-resolution inputs are copied to temporary structures and converted as needed. Original contours are not converted by this path. Custom high-resolution structures first accessed by `Find()` may fall outside the initial scan and cause a warning or resolution mismatch; test such recipes explicitly.

`StepC()` first requires feasible geometry and valid recipes, sets source volumes, optionally builds a draft union, creates field contexts, evaluates recipes in order and writes results. A false condition or null result removes obsolete saved step-C structures. It also removes per-field structures for field codes no longer in the current split. These operations change the shared structure set and can affect existing plans using those structures. The script does not clone a structure set for each plan.

`Write()` (`:400`) gets or creates the destination, matches resolution and assigns SegmentVolume. It records both built volumes and saved structures. **An empty field target logs an error but still returns the structure.** `CheckOverlaps()` (`:382`) compares actual contour endpoint ranges of neighboring COMPO structures and adds one CT slice to the plane-centered overlap estimate. **Insufficient overlap logs an error, but StepC still returns true at line 371.** Consequently these logged errors alone do not stop downstream plan creation. A hardening change should return validation success/failure from overlap and empty-target checks and propagate it to the caller; merely adding more log messages does not create a stop gate.

`StructureTools.Find()` (`:24`) selects first nonempty alias; `FindExact()` can return an empty structure; `FindByRegex()` filters nonempty IDs. `PlaneZ()` uses image origin plus k*ZRes*ZDirection.z. `CheckAxial()` (`:49`) rejects nonaxis-aligned axial images. `SliceExtents()` (`:57`) scans every image plane, computes contour bounding boxes and ignores tiny polygons if requested; BODY filtering threshold is 400 mm². This computes extents, not voxel centroids or continuous surfaces.

`GetOrCreate()` (`:96`) respects nonoverwrite for nonempty destinations, checks `CanEditSegmentVolume` for reuse and `CanAddStructure` for creation. It returns null on refusal, so callers must handle null. Existing approved/noneditable structures can block replacement. `MatchResolution()` (`:123`) uses empty high-resolution subtraction or converts an existing editable destination.

`CreateSlab()` (`:138`) creates a temporary CONTROL rectangle on all selected axial planes. The rectangle spans the image minus a two-voxel border on each transverse edge. It snaps each Z boundary outward to a CT plane, so the saved anatomical result can be longer than the continuous planned cut. FieldContext's top/bottom slabs extend another 50 mm toward vertex/sacrum to avoid truncating anatomy at outer ends. Slabs are temporary patient structures, not merely a mathematical interval.

`RemoveTemporaries()` (`:197`) attempts to remove **every** structure whose ID starts with `zzCSI`, not just a tracked list created in the current run. Reserve that prefix exclusively for the script. Cleanup occurs in finally blocks; removal refusal becomes a warning/manual item. Failed runs can still leave previously written final structures, changes, plans or overrides because there is no automatic rollback implemented here.

## ShoulderDetector

`Core/ShoulderDetector.cs:32` restricts the BODY width samples to the 200 mm below brain bottom, orders superior to inferior, requires at least five samples, finds the narrowest neck slice and then maximum width inferior to it. It requires shoulder width >= 1.4*neck width. Threshold = neck width + 0.3*(shoulder width - neck width). First inferior sample meeting threshold is shoulder top. It is a heuristic sensitive to arms, BODY contours, unusual anatomy, truncated scans and artifacts. `Success=false` is returned with an explanation when it cannot determine a level; session substitutes a fallback and asks for manual review.

## RangeMarginCalculator

`Core/RangeMarginCalculator.cs:19` checks finite positive range, plausible range 5–400 mm, finite nonnegative SOBP and SOBP <= range. `DistalMm()` (`:31`) = roundUp(rangeMm * percent/100 + addedMm); `ProximalMm()` (`:37`) = roundUp(max(0,rangeMm - sobpMm) * percent/100 + addedMm). `RoundUp()` (`:43`) uses ceiling to configured step with a small floating-point tolerance; nonpositive step means no rounding. For R=103.3 mm, SOBP=75.5 mm, 3.5% + 3 mm, raw distal=6.6155 mm and proximal=3.973 mm; 0.1-mm rounding gives 6.7 and 4.0 mm. These formulas are implemented source behavior; the referenced clinical spreadsheet itself was not supplied for independent verification.

## AirGapCalculator

`Core/AirGapCalculator.cs:23` evaluates BODY contour edges in a finite rectangular snout footprint. For gantry g (HFS, couch 0), toward-source direction is `(sin g, -cos g, 0)`, transverse snout X axis `(cos g, sin g,0)`. It rejects slices outside halfY in SI, samples each polygon edge about every 2 mm, rejects samples outside halfX projection and returns maximum toward-source depth. If none qualify it returns negative infinity. `SnoutPositionMm()` (`:58`) adds that depth, requested gap and configured snout-to-range-shifter offset. P1/P3 offsets are 85 mm, P4/P5 65 mm; footprint 37 x 45 cm in the supplied factory. This model is not a full collision engine and is not valid for arbitrary couch rotations or orientations without changes.

## PerturbationPlanner

`Build()` (`Core/PerturbationPlanner.cs:29`) makes specifications for individual fields and pairs. With two oblique brain beams and N posterior fields, enabled defaults produce 2 paired brain-Y scenarios + 2N posterior-Y scenarios + 4 individual brain-Y + 4 individual brain-Z = 10+2N field specifications. The supplied builder returns these data; whether they are actually written through ESAPI is handled separately by OptimizationBuilder. `PlanScenarios()` (`:70`) returns 3 axes * 2 shift signs * 2 range signs = 12 combined scenarios. This set does not add a nominal scenario, range-only scenarios, diagonal setup shifts, or all Cartesian combinations. The log and guide should describe exactly the 12 it constructs, rather than calling them every possible robustness scenario.

## RunLog

`RunLog.Info/Warn/Error/Manual` append entries with time, current Step and event notification. `Manual` is a to-do item, not an action carried out by the script. `BuildReport()` (`Core/RunLog.cs:51`) prints counts, errors, manual checklist, warnings and full chronological log. `SaveTo()` (`:84`) writes a text log with script name, sanitized patient ID and timestamp to `%TEMP%\CsiAutoPlan` by default. A save error becomes a warning and returns null. Logs can contain patient identifiers and plan details; use the institution's approved location and handling rather than treating them as anonymous debug files.

## Beginner extension patterns

### Add an input alias

Add an exact recognized ID to the appropriate pipe-separated string in StructureRecipes, e.g. an institution's alternate thecal-sac name. Rebuild, then test that the expected nonempty structure is selected and that a preexisting draft structure does not take precedence unexpectedly.

### Add a simple evaluation structure

Place this after the BR COMPO recipe, because `Get` depends on build order:

```csharp
StructureRecipe.Once("(D)BR_EVAL", "CONTROL", Step.C, Role.None,
    c =>
    {
        var br = c.Get("(D)PBSTV_BR_COMPO");
        return br == null || c.Body == null
            ? null
            : br.And(c.Body.Margin(-3));
    }),
```

The example demonstrates syntax and geometry; the 3 mm crop is illustrative and must not be mistaken for a clinical constraint. Role.None creates an extra structure without changing which target is assigned to a beam. If the structure should have an objective, add its ID to an ObjectiveTemplate too. Merely creating a structure does not optimize it.

### Add a per-field structure

Use `PerFieldStructure("(D)EVAL_{F}", "CONTROL", Role.None, f => ...)`. Use `f.IsSpineField` for a condition if desired. Returning null can remove an old saved structure with that ID on later runs. The name must remain unique after all `{F}` expansions. Keep intermediates first and dependents later.

### Add an entirely new workflow function

Decide whether it reads only or writes. A report-only function may read `CsiSession`, `StructureSet` and plan data without `BeginModifications`. A write function needs the caller's write-enabled context and modification start, validation before changes, explicit return status and integration into App's execution sequence. If selectable, add a session flag and UI control. Keep pure math in Core and actual ESAPI object operations in Esapi. Do not use Task.Run or a background thread for the patient API objects; use the application's existing ESAPI execution thread.

### Make failure a real gate

Prefer a `bool ValidateCreatedTargets(...)` that returns false on empty targets or insufficient overlap, and have StepC return that result before App creates fields. Add validation of the built structures' actual scanning extents as well as the prebuild geometry. Use the log for explanation, but return failure or throw for a condition intended to prevent continued work. Unit-test pure geometry independently and test API writes on the nonclinical Eclipse system with representative cases and failure cases.

## Implementation points to review

1. Procedure says Boolean AND when joining brain and thecal sac; supplied draft uses OR. The anatomical intent must be clarified before treating that line as implementable policy. Two largely separate targets intersecting may not form a CSI union.
2. OPT structure definitions differ around involved VBs; explain the exact code expression.
3. Defaults use PostBrain and split spine OPTs; the procedure's nominal layout and single SPN_OPT differ.
4. Shoulder fallback and insufficient overlap are warnings/errors in a log, not necessarily stop gates.
5. Draft source mode is a real write workflow, despite the phrase dry run.
6. The 1D geometry feasibility flag is not 3D field deliverability, collision clearance or dosimetric acceptability.
7. Sources, aliases and chosen physician CTV require review; matching a name does not establish physician approval.
8. Temporary-prefix cleanup and step-C stale-output deletion affect the shared structure set; repeat runs are not harmless previews.
