# Understanding and extending your CSI ESAPI script

This guide explains the CsiAutoPlan source supplied in `CsiAutoPlan_11.zip` for your Eclipse v18 environment. It starts with basic ESAPI and C# concepts, follows the actual program, and shows how to add functions. The supplied project identifies itself as **AutoPlanv1.7**, regardless of the ZIP suffix. Its build target is **.NET Framework 4.8, x64, C# 7.3**.

Your project already automates much of **curative proton PBS CSI** setup. Important remaining work includes contour review, patient-specific objectives, field-specific perturbation entry, validation of robust settings, setup fields, collision checks and clinical review. It has no implemented palliative protocol workflow. The existing source has several control-flow weaknesses described below; understanding those is part of understanding the code.

The planning procedure supplies clinical reference requirements. Its instructions to planners, such as deleting an ISO_SETUP plan in the palliative workflow, have not been executed. No patient data was accessed or modified. The extracted original source has been kept unchanged; all teaching additions are outside its project folder.

## How to use this material

Read this guide first. Then use these companions when you want more detail:

- [Beginner ESAPI and C# notes](<esapi_basics.md>) — concepts, UI, build, debugging, and the separate DICOM utility.
- [Detailed core and structure walkthrough](<core_analysis.md>) — session methods, geometry formulas, settings, recipes, HU sampling and helper behavior.
- [Detailed proton implementation review](<proton_analysis.md>) — plan creation, machine fallback, ranges, objectives, uncertainty handling and failure paths.
- [Procedure comparison](<procedure_matrix.md>) — what the procedure says, what this code actually does and what needs local resolution.
- [Source navigation index](<SOURCE_INDEX.md>) — links to every source file and method declarations.
- [Read-only extension examples](<../examples/CsiLearningExtensions.cs>) — three methods you can add to a development copy.

These are teaching and static-review materials. The original API-dependent project and the new ESAPI examples have not been compiled or executed against an Eclipse v18 installation here. The offline checks described at the end exercise only the original numerical C# classes.

## 1 What ESAPI does

ESAPI is the Eclipse Scripting Application Programming Interface. C# code uses objects supplied by Eclipse to read planning data and, where the installed API and license permit, change it. For example, a script can find a structure, read its volume, create a derived structure, create an ion plan or start supported optimization operations.

An **object** represents something you recognize in Eclipse. A **property** reads or sets an attribute of that object. A **method** asks it to perform an operation.

```csharp
string id = structure.Id;      // Read a property.
double cc = structure.Volume;  // Read volume in cubic centimeters.
bool empty = structure.IsEmpty;
```

The object relationships you need most are:

```text
ScriptContext — the workspace Eclipse passes into the plugin
  Patient
    Courses
      Course
        IonPlanSetups
          IonPlanSetup
            IonBeams
              IonBeam
            OptimizationSetup
            PlanUncertainties
            Dose
            StructureSet
              Image
              Structures
                Structure
                  SegmentVolume
```

`ScriptContext` also provides direct references to the current structure set, image-related context and open ion plan. These references can be null. A patient can have many courses and many plans, so **patient + course + plan + structure set** matters more than a plan name alone.

`IonPlanSetup` is the proton planning type used here. Photon examples commonly use `ExternalPlanSetup`; their field-creation and optimization methods are not interchangeable with proton methods. The official [IonPlanSetup reference for API 17.0](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.IonPlanSetup.html) describes the proton methods. Use your installed **v18** help and Visual Studio Object Browser to confirm the exact members, overloads and units available to you.

This project is a **binary plugin**: Visual Studio builds a `.dll`, and Eclipse calls its `Execute` method inside an already open patient workspace. A standalone program creates its own ESAPI application and opens patients itself. You do not need to convert this project to a standalone program to extend it.

## 2 The C# needed to read this code

| C# term | Meaning in this project |
|---|---|
| Namespace | A group of names, such as `CsiAutoPlan.Esapi` or `VMS.TPS`. |
| Class | A type grouping data and methods, such as `PlanBuilder`. |
| Instance | One object of a class, such as `new PlanBuilder(session)`. |
| `static` | A member used on the class itself, such as `SiteSettings.Create()`. |
| `public` / `private` | Accessible to other classes / used only inside its class. |
| `void` | A method returns no value. It can still change data or throw an exception. |
| `bool` | True or false. A returned false matters only if the caller checks it. |
| `var` | Let the compiler infer the variable's type. It remains a definite type. |
| `null` | No object or no result. Check before calling its members. |
| `List<T>` | An ordered collection of objects of type T. |
| `Dictionary<K,V>` | Lookup from a key to a value, such as structure ID to geometry. |
| `readonly` | This field cannot be reassigned after initialization; the referred object's contents may still change. |
| `try/catch/finally` | Attempt work, handle an exception, and run cleanup even if the attempt fails. |
| `Action` / `Func<bool>` | A method passed as data, returning nothing / returning a Boolean. |
| `=>` | A lambda, a small function supplied to another method. |
| Enum | Named choices such as `Role.FieldTarget` or `ObjectiveType.Upper`. |

Consider:

```csharp
var target = ss.Structures.FirstOrDefault(s =>
    string.Equals(s.Id, "CTV_3600", StringComparison.OrdinalIgnoreCase));
```

`ss.Structures` is the collection. `FirstOrDefault` returns the first matching structure, or null. `s => ...` is a function tested on each structure. Case-insensitive comparison avoids requiring a particular capitalization. You still have to decide whether that structure is the correct target and whether it is empty or approved.

Common LINQ methods in the project are `Where` (filter), `Select` (transform), `Any` (does a match exist), `FirstOrDefault` (first match), `ToList` (materialize a list), `OrderBy` (sort), and `Max`/`Min` (find extremes). `Max` and `Min` can throw on empty collections.

For example, `foreach (var o in os.Objectives.ToList()) os.RemoveObjective(o);` first makes a separate list because removing objects while iterating a live collection can be unsafe. This line also means the method deliberately removes the previous objectives.

Microsoft's [C# introduction](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/) and [LINQ introduction](https://learn.microsoft.com/en-us/dotnet/csharp/linq/) are useful alongside the source. Some modern examples use syntax newer than this project's C# 7.3; the teaching examples supplied here avoid that syntax.

## 3 Build the supplied project on your Eclipse v18 development system

Open [CsiAutoPlan.sln](<../CsiAutoPlan/CsiAutoPlan.sln>) in Visual Studio. You need the .NET desktop build tools, the **.NET Framework 4.8 targeting/developer pack**, and both ESAPI reference assemblies from the v18 system you intend to use:

```text
VMS.TPS.Common.Model.API.dll
VMS.TPS.Common.Model.Types.dll
```

A reference assembly tells the compiler which types and members exist. Your source is compiled against those DLLs; Eclipse supplies the runtime implementation. The ZIP includes a placeholder library folder, not the proprietary ESAPI DLLs. The project sets `Private=false` for both references so it does not copy them into the plugin deployment output.

The project searches in this order: explicit `EsapiDir`, environment variable `ESAPI_DIR`, project `lib/esapi`, installed RTM 18.1 path, then installed RTM 18.0 path. An explicit but wrong folder prevents later choices. Installed paths can differ at your site.

From the extracted project root, a build command is:

```powershell
Set-Location -LiteralPath '.\CsiAutoPlan'  # Start from the repository root
dotnet build src\CsiAutoPlan\CsiAutoPlan.csproj -c Release -p:EsapiDir="C:\PATH\TO\YOUR\V18\esapi\API"
```

Replace the placeholder reference directory with the real directory containing both DLLs. Visual Studio's Build Solution is also suitable. Expected original output:

```text
src\CsiAutoPlan\bin\Release\AutoPlanv1.7.esapi.dll
```

The solution label may say Any CPU, but the project explicitly targets **x64**. Keep `net48` for this project; selecting a current .NET runtime such as .NET 8 is a different platform change.

On the site's existing non-clinical Eclipse system, open the intended test patient and planning structure set, then run the plugin using the local Tools > Scripts workflow. A write-enabled plugin follows your institution's script approval and commissioning process. This guide does not ask you to change a clinical database into research mode. The official [script approval documentation](https://docs.developer.varian.com/articles/17.0/11_Approving_Scripts_for_Clinical_Use.html) describes the approval concept; check your installed v18 procedure for its exact workflow.

For each modified release, change `<ScriptVersion>` in [CsiAutoPlan.csproj](<../CsiAutoPlan/src/CsiAutoPlan/CsiAutoPlan.csproj#L24>), for example from `1.7` to `1.8`. That changes the assembly name, version, window title and log prefix together. Rebuild and test the new version before local approval. Merely editing a `.cs` file does not alter an already built DLL.

## 4 Follow one run from start to finish

The overall call sequence is:

```text
Eclipse
  Script.Execute(context)
    App.Run(context)
      SiteSettings.Create / settings validation
      CsiSession + API capability probe + recipe validation
      session.Analyze()
      ExistingPlanReader.Read()
      MainForm.ShowDialog()       <- review and choose steps
      Patient.BeginModifications <- first write-enabled boundary
      App.Execute(session)
        A  draft contours
        B  HU overrides
        C  field targets and OPT structures
        D  course / plan / fields / range margins
        E  uncertainties and objectives
        F  beam line / optimization / final dose / DVH report
      save log and show report
  return to Eclipse for review and saving patient changes
```

[Script.cs](<../CsiAutoPlan/src/CsiAutoPlan/Script.cs#L16>) is the entry point. Its assembly attribute declares `IsWriteable=true`. `Execute` enables WinForms styling, calls `App.Run`, and catches unexpected exceptions for display. An exception dialog does not undo preceding patient changes.

[App.Run](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L17>) loads settings, analyzes the patient and opens the dialog before calling `BeginModifications`. Canceling that initial dialog therefore avoids patient writes. After Run, selected steps can create, overwrite, remove or assign patient data. The plugin leaves saving patient changes to Eclipse; saving its text log is a separate action.

[App.Execute](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L85>) orchestrates builders. `Guard` catches exceptions and logs them. It is **not a transaction or rollback mechanism**. Some failure returns are checked; others are ignored. This distinction is important whenever you add a dependent step.

ESAPI object access belongs on its owning single thread. Do not wrap plan creation or `OptimizeIMPT` in `Task.Run` to make the dialog responsive. The existing code deliberately avoids pumping Eclipse messages with `Application.DoEvents`. See Varian's [script execution and threading guidance](https://docs.developer.varian.com/articles/15.6/08_Creating_Scripts.html); confirm current v18 constraints locally. Pure calculations on copied numbers are a different case from accessing live `Structure` or `IonPlanSetup` objects.

## 5 What each part of the project owns

| File or group | Responsibility | Typical reason to edit |
|---|---|---|
| `Script.cs` | Entry point and top-level exception display | Rarely needed for a new planning feature. |
| `App.cs` | Read/review/write sequence, dependency order, reporting | Add a step or stop dependent steps on failure. |
| `SiteSettings.cs` | Actual compiled room, plan, beam, objective, HU and uncertainty settings | Change configured values or objective templates. |
| `StructureRecipes.cs` | Input aliases, structure margins, derived-volume formulas and names | Add or change derived structures. |
| `Settings/SettingsModel.cs` | Classes describing the available settings | Introduce a new configurable parameter. |
| `Esapi/CsiSession.cs` | Selected patient data and choices for one run | Carry a new per-run option or selection. |
| `UI/MainForm.cs` | Five review tabs, conversions and input validation | Expose an option the planner needs to choose. |
| `UI/ReportForm.cs` | Live and final run report | Change presentation of results. |
| `Core/RunLog.cs` | Error, warning, information and manual messages; log writing | Extend logging or structured output. |
| `Core/CsiGeometryPlanner.cs` | Numeric superior/inferior splitting and isocenter placement | Change splitting policy and test its invariants. |
| `Core/ShoulderDetector.cs` | Estimate shoulder top from BODY width samples | Improve the detection heuristic. |
| `Core/AirGapCalculator.cs` | Numeric source-facing outline depth and snout arithmetic | Change pure air-gap geometry. |
| `Core/RangeMarginCalculator.cs` | Numeric range-margin formula and plausibility checks | Change the validated formula or rounding. |
| `Core/PerturbationPlanner.cs` | Lists of plan and field shift specifications | Change scenario-generation policy. |
| `Esapi/StructureTools.cs` | Find/create/edit/remove structures, slice extents, slabs and resolution | Add a reusable Eclipse structure operation. |
| `Esapi/RecipeEngine.cs` | Evaluate recipes, write results, clean temporary and stale outputs | Change the recipe machinery or validation. |
| `Esapi/StructureBuilder.cs` | Steps A/B/C and HU sampling/application | Add structure preparation or override behavior. |
| `Esapi/PlanBuilder.cs` | Course, ion plan, machine/model/options, fields and snout settings | Add or strengthen plan/field setup. |
| `Esapi/RangeMarginBuilder.cs` | Read Eclipse nominal range/SOBP and apply computed beam margins | Change range readback/application workflow. |
| `Esapi/ExistingPlanReader.cs` | Read technical settings from this patient's existing proton plans | Improve configuration discovery. |
| `Esapi/ApiCapabilities.cs` | Inspect available runtime members and attempt robust-flag calls | Replace a heuristic with a confirmed v18 implementation. |
| `Esapi/OptimizationBuilder.cs` | Objectives, plan scenarios, field checklist, optimization and dose | Add validated optimization/reporting behavior. |
| `tools/DicomRoomSummary/room_summary.py` | Separate offline DICOM-to-settings suggestion utility | Analyze exported room identifiers. |

The [source index](<SOURCE_INDEX.md>) supplies full file links and method locations for this table. The two detailed implementation companions explain the supporting methods and failure paths rather than merely describing filenames.

## 6 Understand session selection and the dialog

`CsiSession` connects configuration to this run: patient, structure set, image, BODY, brain, canal, physician CTV, involved VBs, machine, geometry, prescription and selected steps. Its constructor creates defaults; `Analyze` finds initial inputs; `Reevaluate` and `Recompute` update the proposal when selections change.

The physician CTV is initially selected with the regex `^CTV_\d+`. With multiple matches, the code picks the one with the greatest superior/inferior extent. That helps distinguish a long CSI volume from a boost, but it does not check physician approval or treatment intent. The regex also matches suffixes such as `CTV_3600_copy`. Review the selected target explicitly.

Existing `(D)CTV_BRAIN` and `(D)CTV_THECALSAC` take precedence over selected raw brain/canal sources for geometry. Selecting another raw AI source therefore does not automatically replace an edited draft contour. Step A preserves existing nonempty draft structures unless overwrite is selected.

The five UI tabs are:

1. **Structures:** input selections and draft overwrite.
2. **Isocenters and field split:** room, brain iso source, shoulders, overlap, layout, lower-field count and calculated preview.
3. **Plan and steps:** course/plan IDs, fractionation and A–F choices.
4. **HU overrides:** proposed assignments and which to apply.
5. **API and settings:** compiled settings, existing-plan technical identifiers and runtime API observations.

`OnRun` copies UI controls into the session, validates selected operations, and returns OK. The code can use default pass choices: no physician CTV generally suggests A/B; an open plan with any uncertainty generally suggests F only. Those are workflow conveniences. The presence of any uncertainty does not establish a complete scenario set.

The default prescription is **20 × 1.8 = 36 Gy**, with some values prefilled from an open ion plan. This is a code default, not a patient-specific order. The program labels values Gy(RBE), but its Gy/cGy conversion does not itself apply an RBE model.

## 7 Keep units and coordinate systems separate

| Quantity | Unit or axes used here |
|---|---|
| Structure geometry, DICOM positions, `VVector` isocenter position | mm; patient x left, y posterior, z superior under the implemented HFS assumptions. |
| Directional CTV-to-structure margins | mm in right/left/anterior/posterior/inferior/superior. |
| Beam proximal/distal/lateral margins and nominal range/SOBP | mm. |
| Dose grid and configured spot spacing | cm in this model's option values. |
| Requested air gap | cm in site settings; converted to mm for geometry. |
| Snout position in `AddModulatedScanningBeam` | cm in the documented API; this configuration uses `SnoutPositionApiUnit="cm"`. |
| Plan uncertainty setup shift in the referenced API contract | cm; do not infer its unit from general `VVector` usage. |
| Dialog SI shift | Eclipse display Y in cm, corresponding to internal DICOM z. |
| Dialog AP shift | Eclipse display Z in cm, corresponding to negative internal DICOM y. |
| Prescription | Gy internally, converted to/from cGy where required. |

The official [IonBeam API 17.0 reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.IonBeam.html) documents beam margin/range units and read-only AirGap. The [PlanSetup API reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.PlanSetup.html) documents the setup-error argument in cm. Verify these contracts against your installed v18 assemblies.

For example, a 15 cm inferior UI shift becomes −150 mm in DICOM z. A 2 cm anterior UI shift becomes −20 mm in DICOM y. A whole-centimeter spine shift means rounding relative to the brain isocenter, not rounding an absolute DICOM coordinate to a whole number.

The implementation checks for HFS, axis-aligned axial images. Supporting prone, feet-first or oblique data would require coordinate-transform work throughout geometry, beam directions and perturbations. Removing the orientation check alone does not implement that support.

## 8 How structures are made

There are three separate concepts to keep apart:

- **Structure margins** expand the CTV geometry before writing a PBSTV/OPT.
- **Beam target margins** are properties of the treatment field around its assigned target.
- **Uncertainty scenarios** describe displaced/range-perturbed cases for robustness handling.

Changing one does not automatically change the others.

The supplied structure margins are brain R/L/A/P/I/S = **6/6/4/4/3/3 mm**, and spine = **4/4/4/4/3/3 mm**. They are configured values from this source, and the margin spreadsheet itself was not included for independent comparison.

The Boolean vocabulary is:

```text
A.Or(B)   union             voxels in either A or B
A.And(B)  intersection      voxels in both A and B
A.Sub(B)  subtraction       voxels in A outside B
A.Xor(B)  exclusive union   voxels in exactly one input
A.Margin(d)                expand by d mm; shrink if d is negative
```

`SegmentVolume` represents the geometry. Assigning it to `destination.SegmentVolume` writes it into an Eclipse structure. A volume calculation can be temporary; saving a structure is a separate operation.

The actual recipe sequence is:

1. **Step A:** copy AI brain and canal to the two dosimetrist draft CTVs. These copies do not automatically add the orbit/skull-base/nerve-root edits described by the procedure.
2. **Optional draft source for step C:** brain OR canal becomes `(D)CTV_CSI_DRAFT`. This path still writes patient structures and can proceed to plan creation if selected. Its description as a non-clinical dry run does not mean a no-write preview.
3. **Per-field working volume:** intersect the selected CTV with that field's SI slab, add its directional margins, and crop to BODY. The extended POST layout combines brain and spine portions with their respective margins.
4. **COMPO:** union the CSI working volume with involved VBs in the field's target slab. The VB part receives no additional expansion in this recipe. The final VB union is not explicitly cropped again to BODY.
5. **OPT:** use the CSI working volume. It does **not** subtract involved VBs from that CSI portion.
6. **Spine OPT choice:** default `SplitSpineOptPerField=true` produces UP/MID/LOW OPTs as needed. False makes a combined SPN_OPT for the lower spine fields. Extended POST retains its separate POST_OPT.
7. **INV_VB_OPT:** involved VBs minus the union of CSI parts expanded by 5 mm.

Step 5 differs from the procedure's description of COMPO cropped away from involved VBs. Treat this as a definition to resolve with the responsible physics/clinical team before changing the Boolean operation. The procedure also literally says Boolean AND when combining draft brain/canal to make the physician CTV; intersection and union have very different meanings. The script normally consumes the physician-created CTV and does not resolve that wording for you.

`StructureRecipe.Once` creates one output. `PerFieldStructure` repeats for each segment, replacing `{F}` with BR, POST, UP, MID, MID2 or LOW. `PerFieldVolume` stores a working volume only. `Role` tells fields and objectives which output to use. Keep the unique planning roles intact; use `Role.None` for an extra evaluation structure.

Recipe order matters: `Get()` accesses geometry built **earlier in this run**, while `Find()` reads an existing patient structure. `null` means no result; for a saved step-C recipe it can cause an older output with that ID to be removed. Step C also removes obsolete outputs from an earlier split. Temporary cleanup attempts to remove structures with the `zzCSI` prefix. Reserve that prefix and understand the effects of rerunning C on structures shared by existing plans.

The engine uses high-resolution working copies where needed. A new recipe must still handle absent inputs and mismatched resolution; the current validation cannot prove an arbitrary lambda will succeed.

### HU assignment in step B

`StructureBuilder.ApplyHuOverrides` processes the session's proposed rows. It asks `CanSetAssignedHU` before calling `SetAssignedHU`. This assigns a material CT number to the structure for planning; it does not segment the artifact, edit the original CT pixels or automatically identify a clip material.

| Configured case | Actual default behavior |
|---|---|
| `(D)BB` | Fixed −1000 HU, proposed for application when present. |
| `(D)ARTIFACT_BONE` | NeighborMean within brain, restricted to HU −100 through 150. |
| `(D)ARTIFACT_CLIPS` | NeighborMean within BODY, restricted to HU −200 through 300. |
| Clips, shunt/catheter, air, muscle artifact, fat artifact | ReportOnly rules; no automatic HU is assigned by those rules. |

NeighborMean creates a temporary shell between 2 and 7 mm from the artifact, intersects it with the configured brain or BODY restriction when present, samples image and segment profiles at matching positions, and averages values inside both the shell and HU window. It checks the reported image unit, requires at least 20 accepted samples, rounds the mean to an integer HU and asks for verification. If sampling fails, it leaves a manual task. The sampled mean is sensitive to the chosen restriction, shell and HU window; it is not necessarily the mean of every neighboring voxel.

Existing assigned HU normally unchecks a proposed row unless overwrite is enabled. ReportOnly rows cannot be activated as ordinary automatic rules in the current UI. The procedure's material and saturation conditions for clips still require a human decision; the source merely prints those conditions. A relative electron density value is also not interchangeable with a numeric HU without the site's calibration relationship. To add an override, define the mode, aliases, overwrite policy and any sampling bounds in SiteSettings, then compare the actual assigned value and structure extent in Eclipse.

## 9 How geometry is proposed

The numeric geometry planner is separate from ESAPI. It receives plain numbers and returns BR, optional extended POST and lower spine segments. A feasible SI split is useful evidence, but it does not establish complete 3D deliverability or dose quality.

Shoulder detection measures BODY widths below the brain, finds the narrowest neck slice and then the first inferior slice reaching 30% of the rise toward the broadest shoulder width. Failure produces a warning and a fallback shoulder level 100 mm below brain bottom. That fallback should be replaced by a reviewed level, rather than taken as a confirmed anatomical landmark.

Three layouts are available:

| Layout | Fields at brain iso | Lower fields |
|---|---|---|
| Separate | RPO and LPO | Separate posterior spine targets and isos. |
| PostBrain — all room defaults | RPO, LPO and POST on the BR target | Separate posterior spine targets and isos. |
| PostExtended | RPO/LPO on BR, POST with an extended brain/upper-spine target | Posterior fields for remaining inferior target. |

The default `PostBrain` adds POST even though `Beams.AddPostBrainField=false`: the explicit per-room scheme takes precedence over that legacy/global fallback. Selecting Separate in the dialog clears both POST flags; selecting PostBrain enables the extra brain POST. All four configured rooms use PostBrain; there is no configured ProBeam360 entry.

For lower-field planning, the usual reserved half-length is:

```text
usable half = room half-field limit
              - larger lateral beam margin
              - one CT slice thickness
              - safety reserve
```

Defaults are 170 mm for Proton 1/3, 200 mm for Proton 4/5, 5 mm lateral beam margin and 5 mm safety. The planner also uses structure SI margins and a slice allowance when checking rounded endpoints. Brain coverage has its own superior-edge check; see the detailed core notes for `hardHalf` versus `usableHalf`.

BR ends inferiorly at the more superior of shoulder top + 20 mm or brain iso minus usable half. The remaining target is divided into equal-length lower sections with at least the requested overlap. It tries a preferred 50 mm overlap where feasible without adding a field just for that preference. The required base overlap is 20 mm, with slice handling added by the session.

Lower isos sit near target midpoints, rounded to whole-centimeter shifts. Their LR position matches the brain iso. Their AP position defaults to a mean canal bounding-box center moved 20 mm anteriorly, then rounded relative to the brain iso. This is a heuristic proposal, and the dialog permits an AP override.

A synthetic offline example used a brain iso at z=0 mm, CTV top=100, CTV bottom=−700, shoulder top=−100, 2.5 mm slices, 3 mm SI structure margins and Separate mode. The result was three isocenters with lower shifts −210 and −520 mm and 50 mm planned junction overlaps. These are arithmetic-test inputs and results, not patient planning recommendations.

## 10 How plan and fields are built

`PlanBuilder.CreatePlan` finds or creates the chosen course, rejects a duplicate plan ID, tries configured and harvested patient-support-device IDs, creates an `IonPlanSetup`, sets the target and prescription, selects models/options and requests MFO. It does not approve the plan. A duplicate INITIAL should be handled by intentionally selecting another plan ID or choosing existing-plan operations.

`ExistingPlanReader` reads this patient's existing proton plans. Empty settings can be filled; rejected identifiers may be retried with harvested values. The support-device fallback can try values from other rooms and remove a machine prefix. Acceptance by the API is not evidence that the substituted device/model is the intended commissioned choice. Record what actually worked and make the site configuration explicit after checking it.

Calculation model settings are empty by default. The code may use models from existing proton plans or leave Eclipse defaults. A non-NUPO optimizer produces a manual message; it is not consistently treated as a stop condition. Dose grid is requested at 0.25 cm. Two beam-line spacing values request 0.5 cm, and two overwrite flags enable them. Editing `SpotSpacingCm` alone does not change those populated option strings: keep the actual option strings consistent.

`CreateFields` adds RPO at 265°, LPO at 95° and posterior fields at 180° for the implemented HFS geometry. It assigns the recipe-role target, range shifter and weight 1.0. The default target choice is COMPO. A later range step may replace the fixed proximal/distal margins.

The code computes a snout position for a requested 5 cm air gap from source-facing BODY outline points within the snout footprint. The arithmetic is:

```text
snout position in mm = outermost depth toward source
                      + requested air gap in mm
                      + room-specific snout-to-range-shifter offset
```

The source uses room offsets 85 or 65 mm and a 37 × 45 cm footprint. It can include matching couch outlines in its clearance computation. If geometry cannot be computed or a snout position is rejected, it can create the field at the initial snout position and print a manual task. Review the logged result and actual Eclipse field properties. This calculation does not replace the collision script.

Range margins are enabled by default:

```text
distal   = R × 0.035 + 3 mm
proximal = (R - SOBP) × 0.035 + 3 mm
round each upward to the next 0.1 mm
```

For R=125 mm and SOBP=110 mm, the raw values are 7.375 and 3.525 mm; applied values are **7.4 and 3.6 mm**. These formulas come from the supplied code. The referenced margin spreadsheet was not supplied, so its equivalence and the suitability of `NominalRange`/`NominalSOBPWidth` for that sheet still need independent local confirmation.

`RangeMarginBuilder` saves original margins, sets proximal/distal to zero, reads nominal values, calculates the beam line if needed and then applies computed margins. A `finally` block attempts to restore original margins on uncompleted fields. Unusable ranges keep fixed margins and generate a manual task. The fixed margins are all 5 mm in this source; these differ from the procedure's fixed brain axial and spine proximal wording. If spots were calculated at zero margins, recalculate the beam line before optimization; F does this.

## 11 Objectives and robustness

Objective templates in `SiteSettings` use a structure role token or an alias list. Tokens are resolved to the structures appropriate to the current split:

| Token | Resolves to |
|---|---|
| `@BR_OPT` | Brain OPT. |
| `@POST_OPT` | Extended POST OPT when that segment exists. |
| `@SPN_OPT` | Per-lower-field OPTs, or combined SPN_OPT. |
| `@VB_OPT` | INV_VB_OPT if available. |
| `@BR_COMPO` | Brain COMPO. |
| `@CTV` | Selected CTV, or draft union when using that source. |

The objective dose is `prescription × DosePercentRx / 100`. When updating an existing plan, the code prefers that plan's prescription when available. `VolumePercent` and `DosePercentRx` are separate quantities. A lower objective at 100% volume and 100% Rx asks for a target coverage objective; an upper objective at 0% volume acts as a high-dose objective. Objective achievement must be evaluated in the resulting dose.

Default target templates use 100% lower / 105% upper values and configured priorities. The only OAR templates are example lens constraints at 50% Rx. These are not a complete patient constraint set. `RemoveExistingObjectives=true` deletes prior objectives before replacements are added; rerunning E can remove manually tuned objectives.

Plan scenarios are generated as **6 single-axis shifts × 2 range-error signs = 12 scenarios** with default ±0.3 cm and ±3.5%. These are not eight simultaneous corner shifts. If the plan has any existing uncertainty scenarios, the code skips adding new ones rather than comparing the existing set with the desired set.

Robust target flags are attempted through runtime reflection in `ApiCapabilities`. It searches public members containing "robust" and tries recognized signatures. A matching member name and successful call are weaker evidence than confirmed semantics and readback. Failed or absent support produces a manual RO task. A log phrase about readback should not be treated as universal proof, because the getter can return an unknown/null result in the helper. Check actual v18 objective behavior.

Field-specific shifts are generated as **a checklist only**. For two obliques and three posterior fields, defaults yield:

```text
paired brain Y +/-                2 scenarios
three posterior fields Y +/-      6 scenarios
each brain field Y +/-            4 scenarios
each brain field Z +/-            4 scenarios
total                            16 field scenarios
```

The posterior brain field counts as posterior for this classification. The text and screenshots in the procedure should be read together when confirming the intended set. These 16 are separate from the 12 plan scenarios. The presence of `AddPlanUncertaintyWithParameters` with a field-specific option in documentation does not establish how to apply a distinct vector to a selected beam or a beam pair. This project has no confirmed implementation for that operation.

F calls `CalculateBeamLine`, `OptimizeIMPT` with a restart option, then `PostProcessAndCalculateDose`. It uses **300 maximum iterations**. The time-limit option key is empty, so the stored string "30" does not enforce 30 minutes. This differs from the procedure's blank maximum-iterations box. The code does not explicitly configure the UI Automatic Optimization Mode setting; the API call's internal behavior is not established by this static review. Repeating F restarts optimization; it does not expose a separate continue-from-current option.

After dose, the code reports nominal D98, D2, mean and maximum for selected target/derived structures. `CalculateUncertaintyDoses=false` by default. Nominal DVH numbers do not establish worst-case robustness, junction quality, field-specific uniformity or fulfillment of all clinical constraints.

## 12 Issues to address before adding major automation

These are static findings from the supplied source. They have not been demonstrated against a live Eclipse system.

| Finding | Consequence | Useful development change |
|---|---|---|
| Empty field target and insufficient overlap can log Error but C still returns true. | Plan construction may continue after those errors. | Return validation failure and stop dependent operations. |
| Actual field-size check logs Error but does not return failure. | Field creation can report success despite an oversized target extent. | Check real built targets before creating fields and propagate a failure result. |
| Several `Guard` return values for ranges, uncertainties and objectives are ignored. | F can still run after a preparation failure. | Give each required phase an explicit checked result. |
| E creates manual field-shift tasks but F can be selected in the same run. | Optimization can start before newly requested manual settings are entered. | Separate preparation and optimization or add an explicit verified readiness gate. |
| Existing-plan lookup accepts an open plan with matching ID without checking course. | Another course's INITIAL can be selected. | Match course, plan and structure set identity. |
| Existing-plan structure set is not explicitly compared with session structure set before adding objectives. | Selection and plan can refer to different structure sets. | Check UIDs before E/F. |
| Any existing uncertainty causes generation to be skipped. | Partial or outdated scenarios can pass through. | Compare expected scenario values and report missing/extra scenarios. |
| Missing objective structure is skipped with Info. | A required target or OAR objective can be absent without stopping. | Distinguish required versus optional templates. |
| Draft union path can build a plan. | A draft source is not a clinically approved target merely because the run succeeds. | Make source status and permitted operations explicit. |
| Step C writes into the shared structure set and can remove stale outputs. | Existing plans using those structures can be affected by reruns. | Define structure ownership and rerun behavior before each feature. |

The full [proton review](<proton_analysis.md>) and [core review](<core_analysis.md>) provide source locations. The [procedure matrix](<procedure_matrix.md>) separates procedure differences, missing automation and runtime validation needs.

## 13 A practical way to run the existing workflow while learning

Use non-clinical test patients and inspect the actual results after each pass.

1. **Contour preparation:** A, and B where appropriate. Edit/review the draft targets. Have the intended clinical CTV and involved VBs established through the local workflow.
2. **Planning preparation:** C/D/E with reviewed structures, prescription, machine, geometry and templates. Read errors and all manual tasks. Review actual built volumes, model options, margins, range shifters and isos.
3. **Manual completion:** enter and verify field-specific perturbations, confirm RO settings, confirm optimizer options and patient-specific objectives, and complete required field/setup checks. Existing code does not automatically verify completion of these tasks.
4. **Optimization:** run F only after that preparation is complete. Inspect resulting dose, nominal and required perturbed evaluations, junction gradients and field-specific behavior. Review the final log, then follow local physics and physician checks and save the intended patient changes in Eclipse.

This sequence explains the program's dependencies. It is not a replacement for the planning procedure, EBRT Order or commissioning process.

## 14 Add your first function with the supplied examples

The [CsiLearningExtensions.cs file](<../examples/CsiLearningExtensions.cs>) contains three read-only methods:

- `ReportStructureInventory` demonstrates collections, sorting, properties and logging.
- `CheckPlanMatchesSession` demonstrates preconditions and a Boolean result that the caller must obey.
- `ReportSelectedCtvDose` demonstrates nominal D98/D2 reporting with identity and dose-validity checks.

The file is intentionally outside the supplied project. To try it, copy it into a development copy's source folder, for example `src/CsiAutoPlan/Learning/CsiLearningExtensions.cs`. The SDK project automatically includes ordinary `.cs` files under that project folder. Compile against your v18 DLLs. The examples do not need an external package.

For the inventory lesson, insert this into `App.Run` after the successful SS/Image check and before the review dialog:

```csharp
CsiAutoPlan.Learning.CsiLearningExtensions.ReportStructureInventory(session);
```

It adds log lines and performs no patient writes. Compare the final inventory with the structures visible in Eclipse, including an empty structure and a high-resolution one.

For the identity lesson, in `App.Execute`, immediately after `if (plan == null) return;` and before the `if (s.DoObjectives)` block, add:

```csharp
if (!CsiAutoPlan.Learning.CsiLearningExtensions.CheckPlanMatchesSession(s, plan))
    return;
```

The important idea is `if (!check) return;`: a function that only logs an error cannot prevent the next operation. This example adds a check after plan selection; a production implementation should also fix the lookup itself and ensure correct binding before any writes that depend on the plan.

For the DVH lesson, call after a **successful** F result. One way to express the dependency is:

```csharp
if (s.DoOptimize)
{
    bool doseReady = Guard(log, "Optimize", () => opt.OptimizeAndCalculate(plan));
    if (!doseReady) return;
    Guard(log, "CTV dose lesson", () =>
        CsiAutoPlan.Learning.CsiLearningExtensions.ReportSelectedCtvDose(s, plan));
}
```

This replaces the existing single-line `if (s.DoOptimize) Guard(...)`; do not add a second optimization call. It illustrates one failure dependency only. It does not fix the other preparation failures listed above. The dose report has no clinical thresholds and makes no pass/fail judgment.

## 15 Add a structure through the existing recipe system

Use a recipe for a derived volume before inventing a new builder. For example, this creates a teaching evaluation shell 5 mm outside CTV, limited to BODY:

```csharp
StructureRecipe.Once("(D)CSI_RING5", "CONTROL", Step.C, Role.None,
    c =>
    {
        if (c.Ctv == null || c.Body == null) return null;
        return c.Ctv.Margin(5).Sub(c.Ctv).And(c.Body);
    }),
```

Insert it in the `Recipes` array in `StructureRecipes.cs`, after the main target recipes. The 5 mm value is for learning geometry operations; this example assigns no optimization objective and proposes no clinical criterion. `Role.None` avoids replacing a field-target role. Existing `ReportDose` includes saved step-C evaluation outputs with Role.None.

Review the resulting contours, especially near the skin and image ends. Check rerun behavior: the output can be overwritten on C, and returning null can remove an old output of the same name. A successful recipe build is not proof that the intended Boolean meaning is clinically correct.

If you only want to rename a field target or add input aliases, edit `StructureRecipes.cs`. Role lookup normally carries those names into field creation and objective resolution. If you use literal IDs in your own code or templates, those literals must also be maintained.

## 16 Add or change an objective

For a known structure and an already approved objective definition, add an `ObjectiveTemplate` in `SiteSettings.Objectives`. Existing supported types are Lower, Upper and Mean. The template controls:

```text
Structure       role token or aliases
Type            Lower, Upper, Mean
VolumePercent   DVH volume for point objectives
DosePercentRx   objective dose relative to total prescription
Priority        relative optimizer weighting
Robust          request the RO behavior through current mechanism
```

Do not confuse an upper objective at volume 0% with a validated absolute point maximum. If an OAR constraint is specified as an absolute Gy value, the current percent-of-Rx template will scale when prescription changes. You may need to extend the model with an explicit absolute-dose mode, not hard-code a percentage.

A clean implementation for that change would add an enum or a nullable absolute-dose field to `ObjectiveTemplate`, explicit values in `SiteSettings`, validation that exactly one dose mode is selected, and a conversion in `OptimizationBuilder.SetupObjectives`. Preserve Gy/cGy handling. Test with two different prescriptions to verify that an absolute constraint stays absolute while a relative target objective scales. Any example numeric constraint should remain clearly separate from the actual patient's EBRT Order.

## 17 Design a new function before writing its API calls

For each extension, answer five concrete questions:

1. What are its inputs and their units?
2. What outputs or patient changes should it produce?
3. What must already be true before it runs?
4. What happens on a rerun, a missing input, a partial failure or an existing approved object?
5. Which later operations must stop if it fails?

Then place the work in the existing layers:

| New work | Location |
|---|---|
| Pure math or geometry | Core, with numeric inputs/outputs and offline checks. |
| An Eclipse read/write operation | Esapi builder or helper, with explicit result and readback. |
| A persistent site-configured setting | Appropriate class in SettingsModel plus explicit value in SiteSettings. |
| A patient/run choice | CsiSession, initialized from settings. |
| A planner-facing choice | MainForm control, load/refresh logic, and OnRun validation/transfer. |
| A new workflow phase | App.Execute at the correct dependency point. |
| A derived structure | Prefer StructureRecipes and an appropriate role. |

For example, an optional nominal DVH report could have `bool ReportNominalDvh` in the relevant settings model, a default in SiteSettings, a per-run session field if editable, a checkbox if the planner needs control, and a checked call after successful dose calculation. No checkbox is necessary for a helper always used by an existing step.

Methods that need patient writes belong after the approved modification boundary. Read-only validation should happen before that boundary where possible. Catch exceptions to record useful context, and make required failures prevent dependent work. Do not assume an exception handler erases partially created plans or structures.

Useful next extensions for this project, in increasing scope, are a required-structure report; plan/course/SS identity checks; a complete scenario-comparison report; a nominal plus scenario DVH report; explicit validation gates for built targets and optimizer readiness; and a separately designed palliative mode. Palliative mode needs its own clinical-protocol targets, posterior layout, prescription handling, uncertainties, imaging/setup and workflow rules. Changing 20 fractions to 10 alone would not implement it.

## 18 Debugging and verification

Visual Studio breakpoints let you pause before and after a method, inspect values and see which branch was taken. Start with the pure Core classes and a read-only report. For Eclipse execution, use the site's allowed debugger or official plugin-testing approach on the test system. Attaching a debugger can pause the planning application, so use the established development environment.

When you see an error, distinguish:

- **Compile error:** C# or reference mismatch; no new usable plugin was produced.
- **Runtime exception:** a method failed while running; earlier patient changes may remain.
- **Logged Error:** the code explicitly recorded a problem; subsequent operations stop only if control flow says so.
- **Manual:** a remaining action or review the script expects a human to complete.
- **Calculated dose:** an output for evaluation; not a clinical approval result.

The log is written under `%TEMP%\CsiAutoPlan` by default and includes the patient ID in its filename. Keep real patient logs within the site's approved handling process when sharing debugging material. A failure to write a log does not undo patient changes.

The original README refers to a separate verification package; that package is absent from the supplied ZIP. During this review, [offline_core_checks.ps1](<../verification/offline_core_checks.ps1>) compiled five original ESAPI-independent C# files with PowerShell `Add-Type`. **All 18 checks passed.** They covered worked range values, invalid inputs, rounding, scenario distinctness/counts, paired-field membership, a feasible and an impossible SI split, overlap/extent invariants, source-facing outline depth, snout arithmetic and a synthetic shoulder profile.

To reproduce those checks in PowerShell 7 from the repository root:

```powershell
& '.\verification\offline_core_checks.ps1'
```

These checks do not compile the plugin or evaluate live ESAPI behavior, the commissioned treatment unit, clinical target coverage or plan quality. For a changed feature, compare its actual Eclipse results with independently calculated expectations and representative manual plans. Test the missing-input, duplicate-ID, rerun and failure paths that the change introduces; a success-only test misses the main control-flow risks identified here.

## 19 Suggested learning order

1. Learn variables, methods, null checks, collections and simple LINQ; read Script and App.
2. Build the original using the matching v18 DLLs, then add the read-only inventory in a new version.
3. Trace CsiSession selections and UI-to-session conversions on one non-clinical case.
4. Work the range-margin example and one SI split by hand; compare with the offline checks.
5. Add an evaluation recipe and review the actual Boolean contours and rerun behavior.
6. Add identity/readiness validation that explicitly prevents dependent work on failure.
7. Study PlanBuilder and OptimizationBuilder, then add a narrowly defined proton feature with exact v18 API documentation and local testing.

The [official Varian code samples](https://github.com/VarianAPIs/Varian-Code-Samples) provide introductory and automation projects. The older [Varian API handbook](https://varianapis.github.io/VarianApiBook.pdf) is useful background, particularly its ESAPI basics chapter; it predates v18 and is not the authority for every current proton capability. The installed v18 API help, licensed capabilities and local commissioned settings determine what your completed extension can actually do.
