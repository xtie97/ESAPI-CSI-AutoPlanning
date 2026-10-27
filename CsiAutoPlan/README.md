# CsiAutoPlan: pediatric proton PBS craniospinal auto-planning (ESAPI 18)

An Eclipse binary plugin (`AutoPlanv1.7.esapi.dll`, write-enabled; the name changes with every version, §2) that automates the **curative CSI** section of *Proton PBS Planning – Craniospinal 2026*. Starting from AI auto-segmented brain and thecal-sac contours, it builds the procedure's structures, isocenters, fields (with range-based proximal / distal margins), NUPO objectives and robust scenarios, and can run the optimization.

> **Two files hold every setting**, both compiled into the plugin (there is no settings file):
> - `src/CsiAutoPlan/SiteSettings.cs` – treatment rooms, geometry rules, plan and field settings, range margins, robust optimization, objectives, HU override rules (§4);
> - `src/CsiAutoPlan/StructureRecipes.cs` – everything about structures: input names, PBSTV margins, and the name, type and construction of every structure the plugin creates ([CHANGING-STRUCTURES.md](CHANGING-STRUCTURES.md)).
>
> A change to either is a code change: new version, rebuild, test, approval (§12.2).

> **Commissioning.** Commission the plugin on test patients in a non-clinical Eclipse environment and compare with manual plans before clinical use (§9). The plugin never approves anything; every derived value is shown for review before it is written, and every step the API can't do is listed in a manual checklist.

The unit tests and the offline test harness are in a separate **verification package** (`CsiAutoPlan-verification`, §10); they are not part of this folder or of the plugin.

---

## 1. What is automated

| Procedure step | Plugin | Notes |
|---|---|---|
| (D)CTV_BRAIN / (D)CTV_THECALSAC from AI contours | **Auto** (step A) | Orbit/skull-base extension and nerve-root edits stay manual (checklist) |
| CTV_#### (physician Boolean) | Manual | The plugin pre-selects the `^CTV_\d+` structure with the **largest sup–inf extent**, so a boost CTV (e.g. CTV_5400) listed first is not picked. Without a physician CTV only a **flagged non-clinical** dry run on the union of the (D) structures is possible |
| HU overrides (D)BB, artifacts, clips, shunt, air, catheter | **Auto / checklist** (step B) | BB → −1000 HU. (D)ARTIFACT_BONE / CLIPS → mean HU of the surrounding tissue (sampled). (D)ARTIFACT_muscle / _FAT, (D)AIR, (D)SHUNT / Catheter are checklist items with the clinical RED values because ESAPI 18 sets HU, not RED |
| Brain iso (user origin) + check against field limits | **Auto** | Can switch to a computed iso (brain centre, halfway between vertex and shoulders) |
| Shoulder level | **Auto-detected** from the BODY width profile | Can be overridden in the dialog |
| (D)PBSTV_BR / POST / UP / MID / LOW _COMPO (≥ 2 cm overlap, 5 cm when the field length allows, ≥ 2 cm above the shoulders, inside the field limit, VBs OR'd in) | **Auto** (step C) | Field arrangement per room (§7): RPO/LPO/POST on the brain + separate UP/LO spine isos on all four rooms; POST extended down the spine is a dialog option. Structures defined in `StructureRecipes.cs` |
| (D)PBSTV_*_OPT per field, (D)INV_VB_OPT (0.5 cm crop) | **Auto** (step C) | OPT = the CTV-only field target (CTV part + PBSTV margins, no VB), one per field, as in the clinical plans |
| Course `C#-CSI`, plan `INITIAL`, Rx, MFO | **Auto** (step D) | |
| Dose grid 0.25 cm, spot spacing 0.5 cm | **Auto** | Option keys of the PCS model; a rejected key makes the run list the model's options |
| `01 RPO` 265° / `02 LPO` 95° + `03 POST` 180° at the brain iso, `04 POST UPPER` / `05 POST LOWER` below; field targets, range shifter, weights 1.0 | **Auto** (step D) | Field IDs and angles in `SiteSettings.cs` (`Beams`) |
| Field target margins | **Auto** (step D) | Lateral 0.5 cm. **Proximal / distal per field from its range: distal = R × 3.5 % + 3 mm, proximal = (R − SOBP) × 3.5 % + 3 mm** (§5) |
| Lower iso: mid-level of the field target, whole-cm Y, X = brain X, Z = thecal sac + 2 cm anterior (whole cm) | **Auto** | VB-centre Z is an option (`SpineIsoFromInvolvedVb`) |
| Air gap 5 cm | **Auto** | Set through the snout position, computed before each field is created with Eclipse's definition of the air gap (§7). Field Properties > Snout > Air Gap should read 5.0 cm |
| NUPO objectives (RO on the OPT targets only), NTO off | **Auto** (step E) | The RO (robust) box is ticked through the member Eclipse's ESAPI offers (run-time probe, §8); otherwise the log lists the objectives to tick by hand |
| Plan uncertainties ±0.3 cm × ±3.5 % (U1–U12) | **Auto** (step E) | `AddPlanUncertaintyWithParameters` |
| Field-specific perturbations (brain Y/Z, paired brain Y, each PA Y) | **Checklist table** | No confirmed ESAPI 18 call for per-field shifts; the exact list to enter is printed |
| CalculateBeamLine → OptimizeIMPT → PostProcessAndCalculateDose | **Auto** (step F) | DVH summary printed. Max time / "Automatic optimization mode" aren't in the API |
| Setup fields, collision script, field-dose review, physics check | Manual | Listed in the checklist |

Palliative (30 Gy/10 fx, clinical-protocol based) planning is **not** automated.

## 2. Build

Requirements: Visual Studio 2019/2022 (or the .NET SDK) with the .NET Framework 4.8 targeting pack, plus the two ESAPI 18 reference assemblies `VMS.TPS.Common.Model.API.dll` and `VMS.TPS.Common.Model.Types.dll`.

The project looks for the ESAPI DLLs in this order (first match wins):

1. `-p:EsapiDir="<folder>"` on the command line
2. the environment variable `ESAPI_DIR`
3. `<repo>\lib\esapi\` (see `lib\esapi\README.txt`)
4. `C:\Program Files (x86)\Varian\RTM\18.1\esapi\API`, then `...\18.0\esapi\API`

If none has them, the build stops with an explanatory message (instead of **CS0234 "'Common' does not exist in the namespace 'VMS.TPS'"**). To find the DLLs on an Eclipse workstation:

```
Get-ChildItem 'C:\Program Files (x86)\Varian' -Recurse -Filter VMS.TPS.Common.Model.API.dll -ErrorAction SilentlyContinue
```

Use the DLLs of the Eclipse version you deploy to. A user environment variable survives every new copy of the project (PowerShell, then restart Visual Studio):

```
[Environment]::SetEnvironmentVariable('ESAPI_DIR', '<folder with the two DLLs>', 'User')
```

Build:

```
dotnet build src\CsiAutoPlan\CsiAutoPlan.csproj -c Release
```

The output is **one file**, `src\CsiAutoPlan\bin\Release\AutoPlanv1.7.esapi.dll` (x64, .NET 4.8; the ESAPI DLLs aren't copied).

**New version** (full workflow in §12.2): change one line in `src\CsiAutoPlan\CsiAutoPlan.csproj`:

```xml
<ScriptVersion>1.8</ScriptVersion>   <!-- current: 1.7 -->
```

That sets everything that has to change together:

| What | Example for 1.8 |
|---|---|
| DLL / script name in Eclipse (Tools → Scripts) | `AutoPlanv1.8.esapi.dll` |
| Assembly and file version (Script Approval needs a new one for each modified build) | 1.8 |
| Dialog and report titles, first log line | `AutoPlanv1.8` |
| Log file names | `AutoPlanv1.8_<patient>_<time>.log` |

The version must be 2–4 numbers separated by dots (`1.8`, `1.8.1`); the build stops otherwise.

**Yellow warning triangles in Solution Explorer → Dependencies.** Build once (Ctrl+Shift+B) and read the first error:

| Error | Cause | Fix |
|---|---|---|
| `ESAPI reference assemblies not found` | the two VMS.TPS DLLs aren't in any searched folder (a fresh unzip has an empty `lib\esapi`) | copy them into `lib\esapi\`, or set `ESAPI_DIR` |
| `NU1301` / `NU1101`, `NETSDK1004` or `MSB3644` | the **.NET Framework 4.8 targeting pack** isn't installed, so the SDK tries nuget.org, which the hospital network may block | Visual Studio Installer → Modify → Individual components → **.NET Framework 4.8 targeting pack**; then Restore NuGet Packages and rebuild |

Building from a network share works; if Visual Studio warns that the location isn't trusted, copy the folder to a local drive.

## 3. Install and approve

1. **Develop and test on a non-clinical Eclipse system first.** In RT Administration → System and Facilities → System Properties, tick "Database in Research Mode"; that system runs unapproved write-enabled scripts.
2. Copy `AutoPlanv<version>.esapi.dll` into the published plugin folder. That is the only file: the settings are compiled in. Older versions can stay next to it for comparison (each appears as its own script); remove them when retired.
3. The plugin writes data (`[assembly: ESAPIScript(IsWriteable = true)]`), so on a clinical system it must be approved under **External Beam Planning → Tools → Script Approval**. Approval is tied to the assembly version, so every modified build gets a new `<ScriptVersion>` (§2) and its own approval. The version is shown in the dialog title and written to every log.
4. In **External Beam Planning**, open the patient with the planning CT and structure set (or the ion plan for step F), then **Tools → Scripts → AutoPlanv1.7.esapi.dll**.
5. The analysis and the dialog are read-only. The plugin calls `BeginModifications()` only after you click *Run*; *Cancel* leaves the patient untouched.
6. After a run, **save the patient in Eclipse** to keep the changes.

## 4. Settings (`SiteSettings.cs`)

`SiteSettings.Create()` returns the settings for one run. Every value is written out in that file, with comments; the settings model (`Settings/SettingsModel.cs`) has no default values, so nothing is hidden elsewhere. Problems (an enabled room without IDs, an invalid layout name, …) are logged at start and shown in tab 5.

| Section | What it sets |
|---|---|
| `Machines`, `DefaultMachineId` | one entry per treatment room: Eclipse IDs, half-field limit, snout geometry, default field layout; the room pre-selected in the dialog |
| `Geometry` | brain iso from the user origin, shoulder clearance, field-target overlap, field-limit safety, spine iso rules, shoulder detection |
| `Plan` | course / plan IDs, default Rx, calculation models, dose grid, spot-spacing options |
| `Beams` | gantry angles, field IDs, lateral target margins, fixed proximal / distal margins (fallback), air gaps, range shifter, COMPO or OPT as field target |
| `RangeMargins` | the proximal / distal margin formula (§5) |
| `Optimization` | robust scenarios, field-specific perturbation list, max iterations, RO flag |
| `Objectives` | NUPO objectives by role token (`@BR_OPT`, `@POST_OPT`, `@SPN_OPT`, `@VB_OPT`, `@BR_COMPO`, `@CTV`) or structure ID |
| `HuOverrides` | HU override rules of step B |
| `LogDirectory` | where the run logs go (`%TEMP%\CsiAutoPlan`) |

**Treatment rooms** (from the DICOM exports of your clinical CSI plans, `tools/DicomRoomSummary`, §12.3):

| Room | Plans read | Tolerance | Snout | Range shifter | Patient support | Snout→RS | Half field | Layout | Status |
|---|---|---|---|---|---|---|---|---|---|
| Proton 1 | 3 | `CAM CNS` (3/3) | `PBS Snout` | `RS_Block` (7.37 cm WET) | `Proton_1-Table` | 85 mm | 170 mm (max used 160) | `PostBrain` | plan created in Eclipse |
| Proton 3 | 1 | `CAM Head` (confirm) | `PBS Snout` | `RS_Block1` | `Proton_3-Table` | 85 mm | 170 mm | `PostBrain` | plan created in Eclipse |
| Proton 4 | 3 | `CAM CNS` (also `CAM CNS PBS`, `CAM Head`) | `PBS Snout P5` | `RS_Block` (7.49 cm WET) | `Proton_4-Table` | 65 mm | 200 mm (max used 185) | `PostBrain` | plan and fields created |
| Proton 5 | 2 | `CAM CNS` (also `CAM CNS PBS`) | `PBS Snout P5` | `RS_Block` (7.49 cm WET) | `Proton_5-Table` | 65 mm | 200 mm (max used 175) | `PostBrain` | not yet run in Eclipse |

**IDs Eclipse rejects.** Eclipse's own IDs can differ from DICOM. The plugin:
- fills **empty** room IDs from this patient's existing plans on the same room (warning in the log);
- tries the patient support device in this order and logs the one Eclipse accepted: `SiteSettings.cs` → device of existing plans on the same room → device of other proton plans → each without the machine prefix (`Proton_4-Table` → `Table`);
- if Eclipse rejects the technique / tolerance / snout / range shifter, retries once with the ID existing plans on that room use;
- removes the empty course it created if no patient support device works, and lists every ID it tried.

Tab 5 of the dialog lists the IDs, models, option keys and API units of the patient's existing proton plans **as lines for `SiteSettings.cs`** (read-only; nothing is written). Copy an accepted ID into `SiteSettings.cs` in the next version.

## 5. Range margins (proximal / distal field target margins)

As on the PBSTV margin sheet, each field gets its own margins from its range R and SOBP width:

```
distal margin   = R × 3.5 % + 3 mm
proximal margin = (R − SOBP) × 3.5 % + 3 mm
```

They are set as the field's **proximal and distal target margins** (Field Properties > Target Margin). The lateral margin stays 0.5 cm; the PBSTV structures keep the margins of `StructureRecipes.cs`.

How the plugin does it (step D, right after the fields are created; `Esapi/RangeMarginBuilder.cs`, formula in `Core/RangeMarginCalculator.cs`):

1. The proximal and distal margins of every field are set to 0, so that R and SOBP describe the field target itself.
2. R and SOBP are read from Eclipse (`IonBeam.NominalRange`, `IonBeam.NominalSOBPWidth`). If Eclipse has no values yet, the beam line is calculated once (spot placement) and they are read again.
3. Each field gets its margins, rounded **up** to 0.1 mm. The log shows R, SOBP and both margins per field, e.g. `01 RPO: R 12.50 cm, SOBP 11.00 cm -> distal 0.74 cm, proximal 0.36 cm`.
4. A field without usable values (no range, implausible range, SOBP larger than R) keeps the fixed margins of `SiteSettings.cs` (`Beams`, 0.5 cm) and the checklist asks you to set them by hand. Nothing is ever left at 0.
5. If the beam line was calculated in step 2, step F recalculates it with the margins (or the checklist reminds you, if F isn't ticked).

Per patient, the box *Proximal / distal margins from each field's R and SOBP* in tab 3 switches this off (fixed margins). The percentage, the added 3 mm, the rounding and whether a beam-line calculation may be used are in `SiteSettings.cs` → `RangeMargins`.

**Check on the non-clinical system before clinical use** (the API documents `NominalRange` as "the nominal range of the beam line"; that it equals the R your dosimetrists enter in the sheet must be confirmed once):
- on a test patient, compare the R and SOBP in the log with Field Properties and with what you would enter in the margin sheet for the same fields;
- open a patient with a clinical CSI plan: tab 5 lists, for each existing field, Eclipse's R and SOBP and the margins the sheet formula gives (`| R ... SOBP ... -> sheet distal ... proximal ...`), next to the margins that plan used.

## 6. Typical workflow (three passes)

| Pass | When | Tick | Result |
|---|---|---|---|
| 1 | AI contours available, before MD | A (+B) | (D)CTV_BRAIN / (D)CTV_THECALSAC (+ HU overrides). The dosimetrist edits and approves them; the MD creates CTV_#### |
| 2 | CTV_#### (and VBs_INVOLVED) approved | B–E | Field targets, OPT structures, plan, fields with range margins, objectives, plan uncertainties. **Then in Eclipse:** enter the printed field perturbations and any RO flags the API couldn't set |
| 3 | Plan open in Eclipse | F only | Beam line, NUPO optimization (Eclipse is busy until it finishes), dose, DVH summary |

The dialog picks defaults for each pass: with no physician CTV it ticks only A and B; with an ion plan open that already has uncertainty scenarios, only F.

Step D builds the fields on the field targets of the split shown in tab 2. If you run D without C, the plugin first checks that every field target of that split exists and matches its planned extent (within a few mm); otherwise it stops **before** creating a course or plan and asks you to tick C.

## 7. How the geometry works

All internal positions are DICOM patient coordinates in mm (+x = patient left, +y = posterior, +z = superior). The dialog shows Eclipse user coordinates in cm for HFS: Y = sup/inf, Z = ant(+)/post(−), relative to the brain iso.

- **Shoulders.** Left–right width of the BODY on each slice below the brain; the narrowest slice is the neck; the shoulder top is the first slice below it where the width has risen by 30 % of the neck-to-shoulder difference.
- **Usable half-field.** `H = limit − lateral spot margin − one CT slice − safety (0.5 cm)`. The slice is reserved because a built structure can be one plane longer at each end than planned; the safety absorbs whole-cm iso rounding.
- **Brain field target.** Vertex down to `max(shoulders + 2 cm, brain iso − H)`.
- **Field arrangement** (room default `SpineScheme`, changeable in the dialog):
  - `PostBrain` – default for all four rooms, as in all eight Proton 1/4/5 plans: RPO/LPO/POST at the brain iso on the brain field target; spine fields `POST UPPER` / (`MID`) / `LOWER` at their own isocenters.
  - `PostExtended` – as in the one older Proton 3 plan: the POST field at the brain iso covers the CTV from the vertex down to `brain iso − H`; the rest of the spine gets the fewest equal-length lower fields.
  - `Separate` – procedure text: RPO/LPO only at the brain iso, separate spine isocenters.
  The dialog shows how many isocenters the other layout would need.
- **Overlap.** Adjacent field targets overlap by at least 2 cm plus one slice; with spare field length the overlap is raised toward 5 cm **without ever adding a field**. The real overlaps are measured after creation; under 2 cm is an error.
- **Lower iso.** Y = mid-level of the actual field target, whole-cm shift from the brain iso; X = brain iso X; Z = thecal-sac centre + 2 cm anterior, whole cm, shared by all lower isos.
- **Field-limit check.** Each field target's real extent plus the lateral margin is checked against the room limit.
- **Air gap.** Eclipse's air gap is the smallest distance, along the beam, from the range-shifter face to the BODY anywhere inside the snout footprint (Max X × Max Y, 37 × 45 cm). The plugin (`Core/AirGapCalculator.cs`) finds the outermost BODY (or couch / insert) point toward the source inside that footprint and sets snout position = that depth + air gap + `SnoutToRangeShifterMm` (85 mm `PBS Snout`, 65 mm `PBS Snout P5`), passed to `AddModulatedScanningBeam`. Against the clinical Proton 3 plan the computed snout positions were within 0.3 mm of the planned ones for all four fields.

**Comparison with the clinical plans** (checked by the unit tests of the verification package):

| Plan(s) | Clinical | Plugin |
|---|---|---|
| Proton 3 (1 plan, CT and structures available) | 2 isos (POST extended), lower iso −25 cm, overlap 4.8 cm | with `PostExtended`: 2 isos, −26 cm, 5.0 cm; with the default `PostBrain`: 3 isos |
| Proton 1, 3 plans: spine isos UP / LOW (Y from brain iso) | −20 / −40, −20 / −43, −17 / −38 cm | −17 / −38, −17 / −40, −16 / −37 cm |
| Proton 4, 3 plans | −21 / −48, −20 / −50, −25 / −45 cm | −21 / −47, −20 / −48, −18 / −39 cm |
| Proton 5, 2 plans | −25 / −50, −23 / −48 cm | −22 / −49, −21 / −47 cm |

The plugin centres each spine isocenter on its field target; planners often put the UP isocenter 1–3 cm lower (once 5 cm). All are inside the field limits. Anatomy for the Proton 1/4/5 comparison was estimated from the spot maps.

## 8. API verification and first-run checks

| Item | Basis | First-run check |
|---|---|---|
| Plan, beam, structure, optimization and calculation calls | Compiled against real ESAPI assemblies; the 18.0-only calls (`AddPlanUncertaintyWithParameters`, `CalculatePlanUncertaintyDoses`) follow Varian's 18.0 *ProtonFeaturesDemo* | Builds against your 18.x DLLs |
| `IonBeam.NominalRange`, `NominalSOBPWidth` (range margins) | ESAPI members (mm) | R / SOBP in the log match Field Properties and the margin sheet (§5) |
| Snout position unit (`SnoutPositionApiUnit = cm`) | Varian 18.0 demo uses cm-scale values | Field Properties show a 5.0 cm air gap |
| Uncertainty shift unit (`UncertaintyShiftApiUnit = cm`) | Varian 18.0 demo | Plan Uncertainty dialog shows 0.3 cm / 3.5 % |
| Per-objective RO (robust) flag | Not in the ESAPI assemblies the plugin compiles against; found at run time in Eclipse's ESAPI (an `Add...Objective` overload with a *robust* `bool`, a *robust* property / setter, or an `OptimizationSetup` *robust* method), verified by read-back when possible | First log line `RO (robust) checkbox: ...` (also tab 5); objectives show `[RO]` in the log |
| Field-specific uncertainty creation | **Not confirmed**; never invoked blindly, the list is printed for manual entry | Tab 5 lists candidate methods |
| Gantry angles 265 / 95 / 180 | IEC 61217, as in the procedure | Field list shows RPO 265, LPO 95, PA 180 |

**Reflection.** The RO member is called through .NET reflection because it isn't in the assemblies the plugin compiles against. Eclipse restricts reflection for approved scripts (Varian API book §2.1.2); calling a public API member is what the script would do directly, but check that your approval process accepts it. The probe only reads member lists unless `Optimization.AllowReflectionForRobustFlag = true` (`SiteSettings.cs`), and stops after the first failed call. Once your Eclipse log names the member, it can be called directly in code instead.

## 9. Commissioning checklist (suggested)

1. Run the verification package (§10): unit tests 0 failures, harness without errors.
2. On 5–10 retrospective pediatric CSI patients in a non-clinical environment, run passes 1–3 and compare with the clinical plans: shoulder level, iso count and coordinates, field-target extents and overlaps, field limits, air gaps (then the collision check), **range margins against the margin sheet (§5)**, uncertainty scenarios, objectives, target coverage and junction robustness.
3. Confirm the unit assumptions (§8).
4. Have physics review the log/report. The report is saved to `%TEMP%\CsiAutoPlan`, contains the patient ID and stays local.

## 10. Verification package (separate)

`CsiAutoPlan-verification` is kept next to this folder and is **not** deployed or needed to build the plugin:

- `tests/` – unit tests of the plugin's ESAPI-free code: field split (clinical cases, 2,000 random patients), shoulders, perturbations, air gap, **range margins against the margin-sheet rows**, and `SiteSettings.cs` (validates, every setting written out).
- `harness/` – runs the dialog and steps A–F on fake (stub) Eclipse objects and a synthetic phantom; checks control flow and names, never dosimetry.

Its README explains how to run both. Varian's *PluginTester* (Varian-Code-Samples → `Eclipse Scripting API/projects/PluginTester`) is the standard way to debug the plugin in Visual Studio against real data on a non-clinical system.

## 11. Known limitations

- HFS only (other orientations are blocked). Lateral/AP field-size limits aren't checked, only sup–inf.
- No setup fields, reference-point management, collision check or couch insertion (checklist items).
- The POST field at the brain iso gets Y-only perturbations like the lower POST fields; RPO/LPO get Y and Z. Compare with your clinical perturbation set on the first case.
- RED overrides that ESAPI cannot set (muscle, fat, air, catheter) stay on the checklist.
- The optimization time limit and "Automatic Optimization Mode" aren't exposed by `OptimizationOptionsIMPT`; use `MaxIterations` or a NUPO option key.
- ESAPI is single-threaded: the plugin runs on Eclipse's UI thread and a long optimization freezes Eclipse until it finishes (no `DoEvents`, by design).

## 12. Updating and maintaining the script

Every change to the plugin's behaviour is a code change in this folder: rebuild, new version, test, approval.

### 12.1 What lives where

| Path | What it is | Edit it to change |
|---|---|---|
| `src/CsiAutoPlan/SiteSettings.cs` | **all site settings** (§4) | rooms, geometry rules, plan / field settings, range margins, objectives, robust scenarios, HU rules |
| `src/CsiAutoPlan/StructureRecipes.cs` | **all structure settings** | input names, PBSTV margins, created structures – `CHANGING-STRUCTURES.md` |
| `src/CsiAutoPlan/CsiAutoPlan.csproj` | build file | `<ScriptVersion>`, ESAPI folder search |
| `src/CsiAutoPlan/Settings/SettingsModel.cs` | the shape of the settings (no values) | only to add a new setting |
| `src/CsiAutoPlan/Core/` | ESAPI-free logic: field split (`CsiGeometryPlanner`), shoulders, perturbations, air gap, range margin formula, log | those rules (unit-tested) |
| `src/CsiAutoPlan/Esapi/CsiSession.cs` | analysis of the open patient, dialog selections | how structures / isocenters are detected |
| `src/CsiAutoPlan/Esapi/RecipeEngine.cs`, `StructureBuilder.cs`, `StructureTools.cs` | run the structure recipes; HU overrides | normally nothing |
| `src/CsiAutoPlan/Esapi/PlanBuilder.cs` | course / plan, Rx, models, options, fields, range shifter, air gap | plan and field creation |
| `src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs` | proximal / distal margins from R and SOBP (§5) | how R / SOBP are obtained |
| `src/CsiAutoPlan/Esapi/OptimizationBuilder.cs` | objectives, plan uncertainties, NUPO optimization, dose | optimization |
| `src/CsiAutoPlan/Esapi/ExistingPlanReader.cs` | reads IDs from existing proton plans (fallbacks, tab 5) | what is read |
| `src/CsiAutoPlan/Esapi/ApiCapabilities.cs` | run-time probe for the RO member | RO handling |
| `src/CsiAutoPlan/UI/` | review dialog, progress / report window | the dialog |
| `src/CsiAutoPlan/App.cs`, `Script.cs` | run order of steps A–F, Eclipse entry point | step order |
| `tools/DicomRoomSummary/room_summary.py` | reads RN*.dcm exports, prints room lines for `SiteSettings.cs` | adding / checking rooms |
| `CHANGELOG.md` | what changed in which version | every deployed change |

### 12.2 Release workflow (every change)

1. **Work in a copy of the project** (better: a git repository) on the development PC. Set `ESAPI_DIR` once (§2).
2. **Make the change** (12.4).
3. **Raise `<ScriptVersion>`** in `src/CsiAutoPlan/CsiAutoPlan.csproj` (1.7 → 1.8).
4. **Build** Release. The build output line `Script: AutoPlanv1.8.esapi.dll (version 1.8)` confirms the name.
5. **Run the verification package** (§10): unit tests `0 failures`; for code changes also the harness.
6. **Test on the non-clinical Eclipse system** (Research Mode): copy the DLL to the test plugin folder, run the checklist in 12.7, keep the log (`%TEMP%\CsiAutoPlan\AutoPlanv1.8_<patient>_<time>.log`).
7. **Physics review** of the change, the test results and the log.
8. **Deploy**: copy the DLL into the clinical published-scripts folder. Leave the previous DLL until the new one is approved, then remove it.
9. **Approve** the new version (External Beam Planning → Tools → Script Approval) and record it in `CHANGELOG.md`.

Because all settings are compiled in, Script Approval covers them: nobody can change the plugin's behaviour without a new, approved version.

### 12.3 Add or update a treatment room

1. Export one to three approved CSI plans of that room from ARIA as DICOM RT Ion Plan (RN*.dcm), de-identified, into one folder.
2. Run `python tools\DicomRoomSummary\room_summary.py <folder>` (Python 3 with `pydicom` and `numpy`). It prints, per room, the tolerance table, snout, range shifter, patient support, snout-to-range-shifter offset, farthest spot from the isocenter, field arrangement, and a ready-made `new MachineOption { ... },` line. Only technical plan content is printed.
3. Paste the line into `Machines` in `SiteSettings.cs` (replace the room's old line). Resolve anything marked `<- tie: choose one`. Keep `HalfFieldLimitMm` at the room's commissioned limit (170 mm Proton 1/3, 200 mm Proton 4/5).
4. Alternative inside Eclipse: open a patient with a proton plan in that room and run the plugin; tab 5 shows the IDs exactly as Eclipse reports them, as lines for `SiteSettings.cs`.
5. `Enabled = true` shows the room in the dialog; `false` hides it but keeps its settings.
6. New version, build, unit tests, then a first run on a test patient in that room: check the `Patient support device '...'` line, `Created ion plan`, each field's snout / air-gap line and the range margins, and that nothing was `rejected`.

| `MachineOption` field | DICOM source (for checking by hand) |
|---|---|
| `MachineId` | Treatment Machine Name (300A,00B2) of the treatment fields |
| `ToleranceId` | Tolerance Table Label (300A,0043) |
| `SnoutId` | Snout ID (300A,030F) |
| `RangeShifterId` | Range Shifter ID (300A,0318) |
| `PatientSupportDeviceId` | Patient Support ID (300A,0352) – confirm in Eclipse |
| `SnoutToRangeShifterMm` | Snout Position (300A,030D) minus Isocenter to Range Shifter Distance (300A,0364) |
| `SnoutSizeXcm`, `SnoutSizeYcm` | Field Properties > Snout: Max X / Max Y (37 / 45 cm) |
| `HalfFieldLimitMm` | commissioning; check against Scan Spot Position Map (300A,0394) |
| `SpineScheme` | `SpineSchemes.PostBrain`, `PostExtended` or `Separate` (§7) |

### 12.4 Common changes

| Change | Where |
|---|---|
| Room IDs, add / hide a room, half-field limit, snout offset, default layout | `SiteSettings.cs` → `Machines` (12.3) |
| Room pre-selected in the dialog | `SiteSettings.cs` → `DefaultMachineId` |
| Range margin formula (3.5 %, 3 mm), rounding, beam-line use | `SiteSettings.cs` → `RangeMargins` |
| Lateral margins; fallback proximal / distal margins | `SiteSettings.cs` → `Beams` |
| Overlap, shoulder clearance, field-limit safety, max spine fields, spine iso depth | `SiteSettings.cs` → `Geometry` |
| Field IDs, gantry angles, air gaps, range-shifter setting | `SiteSettings.cs` → `Beams` |
| Course / plan ID, default Rx, models, dose grid, spot spacing | `SiteSettings.cs` → `Plan` |
| Objectives, RO flag per objective, priorities | `SiteSettings.cs` → `Objectives` |
| Robust scenarios, perturbation list, max iterations | `SiteSettings.cs` → `Optimization` |
| HU override rules | `SiteSettings.cs` → `HuOverrides` |
| **Structures**: PBSTV margins, input names, created structures | `StructureRecipes.cs` – `CHANGING-STRUCTURES.md` |
| New geometry rule, new step, new dialog control | code, 12.6 |

### 12.5 New Eclipse version, new calculation models

1. Put the new `VMS.TPS.Common.Model.API.dll` and `...Types.dll` in `lib\esapi\` or point `ESAPI_DIR` at them. If Varian changes the .NET Framework version, change `<TargetFramework>`.
2. Build. Compile errors mean an API change; fix them in `Esapi/` (`Core/` has no ESAPI dependency).
3. New version, verification package.
4. On the non-clinical system, run once and read in the log: model names, whether the dose-grid and spot-spacing options were accepted, the air-gap lines, **the range-margin lines (R / SOBP still reported?)**, and tab 5 (RO member, field-specific uncertainties).
5. Update `SiteSettings.cs` → `Plan` (model names, option keys) if needed; re-approve.

### 12.6 Changing the planning logic (code)

1. Find the place in 12.1. Keep calculations in `Core/` so they stay unit-testable.
2. New setting? Add the field to the options class in `Settings/SettingsModel.cs` (no default value) and its value, with a comment, to `SiteSettings.cs`. Add or adjust a unit test in the verification package; it fails if a setting is not written out in `SiteSettings.cs`.
3. Rules that keep the plugin safe in Eclipse – keep them:
   - nothing is written before the user clicks *Run* (`BeginModifications` only after the dialog);
   - no `Application.DoEvents`, no background threads (ESAPI is single-threaded);
   - every step reports through the log (`Info` / `Warn` / `Error` / `Manual`); anything the API can't do becomes a `Manual` checklist line;
   - reflection only for the RO member (`AllowReflectionForRobustFlag`); otherwise member lists are only read.
4. New version, build, verification package, non-clinical test (12.2).

### 12.7 Test checklist on the non-clinical system

For each room you changed, on 1–3 patients whose clinical plan you know:

- [ ] dialog: right room pre-selected, field arrangement as expected, shoulder level plausible, isocenters whole cm
- [ ] steps A–C: (D) structures, HU overrides, field targets and OPT structures created; overlaps ≥ 2 cm in the log
- [ ] step D: course/plan created, `Patient support device '...'` accepted, 4–6 fields with the right IDs, gantry angles, targets and range shifter
- [ ] air gaps: Field Properties > Snout > Air Gap reads 5.0 cm on one lateral and one PA field
- [ ] target margins: lateral 0.50 cm; proximal / distal per field as in the log, and as the margin sheet gives for that field's R and SOBP
- [ ] step E: objectives, robust scenarios, perturbation list
- [ ] step F: optimization finishes, dose calculated, DVH summary sensible
- [ ] log: no `Error`; every `Manual` line understood
- [ ] compare isocenters, overlaps and coverage with the clinical plan

### 12.8 Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Build: `CS0234 'Common' does not exist`, or `ESAPI reference assemblies not found` | ESAPI DLLs not found | §2: `lib\esapi\` or `ESAPI_DIR` |
| All references show yellow triangles, `NU1301` / `NETSDK1004` / `MSB3644` | .NET Framework 4.8 targeting pack missing, NuGet blocked | §2 table |
| Script not listed in Tools → Scripts | wrong folder, or file name doesn't end in `.esapi.dll` | copy `bin\Release\AutoPlanv*.esapi.dll` into the plugin folder |
| `SiteSettings.cs: ...` warnings at start (tab 5) | a setting is invalid or a room has no IDs | fix in `SiteSettings.cs`, new version |
| `Eclipse did not allow modifications` | script not approved, patient read-only or locked | approve, or use Research Mode on the test system |
| `Patient support device could not be found` | ID differs between DICOM and Eclipse | the plugin tries fallbacks and logs the one accepted; copy it into `SiteSettings.cs` (tab 5 lists the IDs) |
| `... was rejected` (technique / tolerance / snout / range shifter) | ID not in Eclipse | same as above |
| `Field target ... does not exist / spans ...`, plan not created | step D run without step C after changing room or layout | tick step C |
| Air Gap in Field Properties is not 5.0 cm | snout footprint, `SnoutToRangeShifterMm` or `SnoutPositionApiUnit` wrong for that room, or no BODY | fix in `SiteSettings.cs` (12.3); the field's log line shows the depth and the limiting structure |
| `... keeps the fixed margins` (range margins) | Eclipse reported no usable R / SOBP for that field | set the margins by hand from Field Properties (checklist); check §5 on the test system |
| `tick the RO (robust) box ONLY for: ...` | no robust-objective member, the call failed, or `AllowReflectionForRobustFlag = false` | read the `RO (robust) checkbox:` log line; tick RO by hand |
| `Optimization algorithm should be NUPO` | no NUPO model set or found on existing plans | `SiteSettings.cs` → `Plan.OptimizationModel` |
| `Calc option '...' was rejected` | option key changed (new model version) | copy the key from the option list printed below it |
| `Image has no user origin` | CT without user origin | set the user origin (brain iso) in Eclipse, or accept the computed iso |
| Unexpected number of spine fields | *Spine fields* in tab 2 not on *Auto* | set *Auto* |
| `StructureRecipes.cs has errors` at start, or `building ... failed` | a mistake in a structure recipe | the message names it; `CHANGING-STRUCTURES.md` §7 |

### 12.9 Change log

Add an entry to `CHANGELOG.md` for every deployed version: version, date, what changed and why, who reviewed / approved, and which test patients were run.
