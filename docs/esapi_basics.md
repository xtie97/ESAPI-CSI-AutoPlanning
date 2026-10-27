# ESAPI beginner notes for this CSI project

These notes describe the supplied source, whose internal version is **1.7** even though the archive is named `CsiAutoPlan_11.zip`. The user has confirmed Eclipse **v18**. No Eclipse execution or build against the site's DLLs was performed for these notes. The public Varian references available for checking fundamentals are mainly v17 and v15.6; use the installed v18 help and the site's v18 assemblies for exact proton API declarations.

## What ESAPI is

ESAPI is Varian's .NET library for accessing Eclipse objects. Reading structures, plans and doses is one capability; changing them requires Eclipse Automation and appropriate system permissions. This project is a **binary plug-in**, a compiled DLL that Eclipse loads for the currently open patient. It is not a stand-alone executable and does not connect to ARIA by starting its own `Application` object. [Varian overview](https://docs.developer.varian.com/articles/17.0/04_About_the_Eclipse_Scripting_API.html), [creating plug-ins](https://docs.developer.varian.com/articles/17.0/08_Creating_Scripts.html).

Separate three layers when reading the source:

1. **C#** is the programming language: `if`, `foreach`, classes, methods, variables and exceptions.
2. **.NET Framework** supplies general libraries, such as collections, file writing and WinForms dialogs.
3. **ESAPI** supplies Eclipse objects such as `Patient`, `StructureSet`, `IonPlanSetup`, `Structure` and dose calculations.

`VMS.TPS.Common.Model.API.dll` exposes the main Eclipse object classes. `VMS.TPS.Common.Model.Types.dll` supplies supporting types such as `VVector` and `DoseValue`. A DLL reference makes types available to the compiler; it does not provide an Eclipse database or calculation engine on a development computer. The project sets `Private=false` for both Varian references, so these reference DLLs are not copied into its build output. Evidence: `src/CsiAutoPlan/CsiAutoPlan.csproj:50-58`.

## How the Eclipse objects relate

```text
ScriptContext supplied by Eclipse
  Patient
    Courses
      Course
        IonPlanSetups
          IonPlanSetup
            Beams / IonBeams
            OptimizationSetup
            Dose
            StructureSet reference
    StructureSets
      StructureSet
        Image reference
        Structures
          Structure
            SegmentVolume
            MeshGeometry
```

This is an access map, not a claim that each displayed object is an independent database record or that a structure set belongs exclusively to one plan. Several plans can refer to the same structure set, which is why changing a structure can affect more than the plan currently under construction.

`ScriptContext` contains the active Eclipse selection. `context.Patient`, `context.StructureSet`, and `context.IonPlanSetup` can be null; the last is null when no proton plan is active. The script receives its context from Eclipse rather than creating it. [ScriptContext reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.ScriptContext.html).

`Patient.Courses` and `Patient.StructureSets` are enumerable collections. They do not mean one current course or one current structure set. `FirstOrDefault` searches such a collection and returns null if nothing matches. [Patient reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.Patient.html).

`Structure` represents a contour object. Its `Id` is the structure name used for matching; `IsEmpty` concerns available geometry, while `IsApproved` concerns approval. `SegmentVolume` provides contour Boolean operations. `Or` combines regions, `And` retains their intersection, and `Sub` removes one region from another. `Margin` is specified in millimeters. [Structure reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.Structure.html).

`IonPlanSetup` represents a proton plan and inherits common plan functionality. Do not copy photon `ExternalPlanSetup` beam-creation examples into this project without checking the proton equivalents. [IonPlanSetup reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.IonPlanSetup.html).

`DoseValue` stores both a number and its unit. Reading `.Dose` alone drops the unit information; a value of 180 could mean cGy rather than Gy. An undefined dose is also possible. [DoseValue reference](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.Types.DoseValue.html). In this project's UI the prescription is labeled Gy RBE; this label does not itself establish a site's dose convention.

## Essential C# vocabulary in the actual source

| Syntax | Meaning here |
|---|---|
| `using CsiAutoPlan.Esapi;` | Lets this file use names in that namespace without typing the full namespace each time. It does not execute the imported code. |
| `namespace CsiAutoPlan` | Groups the project's classes by name. |
| `class CsiSession` | A type that groups related data and behavior. |
| `new RunLog()` | Creates one instance of a class. |
| `var log = new RunLog();` | The compiler infers `RunLog`; `var` does not mean that the variable can change to any unrelated type. |
| `public` / `private` | Controls which code can access a class member. |
| `static` | Member belongs to the type rather than a particular instance; `App.Run` can be called without `new App()`. |
| `readonly` | A field reference can be assigned at construction, but not reassigned later; the object it references may still change. |
| `void` | Method returns no value. |
| `bool` | True or false, often used for step success. |
| `string`, `int`, `double` | Text, integer, and floating point number. |
| `double?` | Nullable number, used for an optional override. This is C# nullable value type syntax. |
| `null` | No referenced object; check it before using an object's members. |
| `if (...) return;` | Stops the current method early when a prerequisite fails. |
| `foreach` | Repeats an operation for each item in a collection. |
| `List<string>` | A growable list of text values. |
| `s => !s.IsEmpty` | A lambda: a small function receiving `s` and returning whether it is not empty. |
| `.Where(...).Select(...)` | LINQ filtering followed by projection into another value. |
| `try` / `catch` | Attempts work and handles an exception if one is thrown. It does not automatically undo preceding work. |
| `out string info` | Method supplies an additional result through the caller's variable. |
| `Action` / `Func<bool>` | A stored operation with no result / an operation returning success or failure. |
| `using (var dlg = ...)` | Disposes of the dialog when the block ends. This differs from a namespace `using` directive. |
| `Added += OnAdded` | Subscribes a method to an event, used to update the progress log. |
| `"{0:F1}"` | Formatting placeholder, showing argument 0 to one decimal place. |
| `[assembly: ESAPIScript(...)]` | Assembly metadata for Eclipse; this is not an ordinary function call. |

Use [Microsoft's C# methods guide](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/methods) to learn declarations and parameters, [LINQ features](https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/features-that-support-linq) for `var`, initializers and query expressions, and [lambda expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/lambda-expressions) for `=>`. Current tutorials show newer C# features; this project deliberately sets C# 7.3, so examples using records, file-scoped namespaces, nullable reference annotations such as `string?`, or top-level statements need adaptation.

For example, `S.SS.Structures.Where(s => !s.IsEmpty).Select(s => s.Id).OrderBy(x => x).ToArray()` in `UI/MainForm.cs:232` means: start with every structure in the selected structure set, keep the non-empty ones, take their IDs, sort the IDs, and materialize an array for the drop-down menus.

## Exact entry point and run order

`Script.cs:6` marks the entire plug-in write-enabled. Eclipse looks for `VMS.TPS.Script.Execute(ScriptContext context)` (`Script.cs:8-21`). That method enables WinForms styling and hands control to `CsiAutoPlan.App.Run`; its catch block displays unexpected errors (`Script.cs:23-25`). The optional WPF `Window` parameter is omitted because this project shows WinForms dialogs.

`App.Run` does the following:

1. Rejects a missing patient (`App.cs:19`).
2. Creates the run log and compiled site settings, then a `CsiSession` (`21-28`).
3. Probes API capabilities and validates the compiled recipe book (`29-39`).
4. Analyzes the active data; missing structure set or image stops the run (`40-45`).
5. Reads settings from existing proton plans as possible fallback IDs (`47-55`).
6. Shows the review dialog; Cancel returns before write access (`57-60`).
7. Calls `Patient.BeginModifications()` and reports failure to gain write access (`61-68`).
8. Shows progress, executes selected steps, writes the report file, and displays the final report (`70-81`).

The distinction between preview and execution is important for extensions: keep new read-only analysis before the dialog, and put actual data changes after the existing `BeginModifications` boundary.

`App.Execute` is the workflow controller:

| Step | Actual call | Main purpose |
|---|---|---|
| A | `structures.CreateDraftCtvs` at `App.cs:98` | Starting structures from supplied contours. |
| B | `structures.ApplyHuOverrides` at `99` | Selected HU overrides and manual checklist items. |
| C | `structures.CreateFieldTargets` at `102` | Field targets and optimization contours from recipes. |
| D | `plans.CreatePlan`, `plans.CreateFields`, `RangeMarginBuilder.Apply` at `119-125` | Course/plan/fields and optional range-derived margins. |
| E | `opt.SetupUncertainties`, `opt.SetupObjectives` at `138-139` | Uncertainty scenarios, objectives and manual fallbacks. |
| F | `opt.OptimizeAndCalculate` at `141` | Optimization and dose. |

A step not selected does not run. If D is skipped but E or F is selected, `FindExistingPlan` first considers the open proton plan and then searches the specified course (`App.cs:127-131,170-175`). If an open plan ID matches, it is chosen without also comparing the entered course ID. A future validation feature should verify the selected plan's course, image and structure set rather than trusting an ID alone.

`Guard` catches exceptions, records an error, and returns false (`App.cs:178-189`). Only some callers check that false result. For example, failure during field creation stops the remaining steps, but range margin, uncertainty and objective failures are logged without necessarily preventing F. Do not interpret a report's `Done` message as proof that every requested step succeeded. The catch also does not roll back structures or a partly created plan.

## Reading and modifying through ESAPI

Write access requires both the assembly declaration and a successful `BeginModifications()` for that patient. Declaring write intent alone does not grant permission, and `BeginModifications` can fail for site/system conditions. [Patient.BeginModifications](https://docs.developer.varian.com/api/17.0/VMS.TPS.Common.Model.API.Patient.html#VMS_TPS_Common_Model_API_Patient_BeginModifications).

This plug-in modifies the current Eclipse editing context. The existing report explicitly tells the user to save in Eclipse (`UI/ReportForm.cs:45-46`); there is no call to `Application.SaveModifications` here. Stand-alone examples that create an `Application` and call `SaveModifications` are a different architecture. Saving or discarding plug-in changes belongs to the Eclipse UI. [Varian creating scripts, save behavior](https://docs.developer.varian.com/articles/17.0/08_Creating_Scripts.html).

Clinical write-enabled scripts require local validation and script approval. Changing compiled settings changes behavior, so it needs the same versioned release treatment as changing an algorithm. Script approval is separate from physician approval of targets or treatment approval of a plan. [Varian script approval](https://docs.developer.varian.com/articles/17.0/11_Approving_Scripts_for_Clinical_Use.html).

Keep ESAPI object access on its owning single thread. Moving `IonPlanSetup`, `Structure`, or optimization calls into `Task.Run`, a `BackgroundWorker`, or `Parallel.ForEach` is not a simple way to make the UI responsive. The current code intentionally avoids `Application.DoEvents` (`App.cs:72-74`) and paints the log synchronously (`UI/ReportForm.cs:35-39`). For later architecture work, copy plain numeric/string data out first and run only pure calculations elsewhere. [Varian threading requirement](https://docs.developer.varian.com/articles/15.6/08_Creating_Scripts.html).

## What each dialog does

`UI/MainForm.cs` creates its controls in C# instead of using a designer file. Constructor (`35-47`) builds controls, loads values, then computes the preview.

| Tab | Build method and line | What the user is reviewing |
|---|---|---|
| Structures | `BuildTargetsTab:81` | BODY, brain, thecal sac, clinical target and involved VBs; overwrite option for draft structures. |
| Isocenters and field split | `BuildGeometryTab:99` | Room, origin/computed brain iso, shoulder level, overlap, scheme and spine field count; geometry table is read-only. |
| Plan and steps | `BuildPlanTab:165` | Course, plan, fractionation and A-F checkboxes. |
| HU overrides | `BuildHuTab:206` | Which proposed override rows to apply; report-only rows cannot be enabled. |
| API and settings | `BuildInfoTab:220` | Compiled settings, inferred existing-plan IDs and API capability probe. |

`LoadValues:230` selects defaults. It regards an open ion plan with any `PlanUncertainties` as pass 3 and checks F only (`257-266`). This is a convenience heuristic, not a verification that the uncertainty set or RO flags are complete. `OnStructuresChanged:279` updates session selections and re-analyzes; `RefreshGeometry:295` copies UI values into the session, recomputes, and redraws. Most displayed lengths are centimeters, with multiplication or division by 10 at the UI boundary (`301-305,312-329`). Never substitute displayed UI axes for raw DICOM axes without consulting `CsiSession` conversions.

`OnRun:355` transfers the chosen values and step flags. It checks required machine IDs (`375-391`), verifies existing field targets if D runs without C (`392-404`), checks geometry for C/D (`405-410`), and asks explicit confirmation for using a draft union and for optimization (`411-418`). It then returns `DialogResult.OK` to `App.Run`. Those confirmations are part of the application being reviewed, not instructions to this analysis agent.

`UI/ReportForm.cs` subscribes to the run log, shows live entries, and later displays errors/manual steps/warnings/full log. `Core/RunLog.cs:84-99` saves a text report and returns null on a file-writing failure. The filename contains the patient ID (`89-91`), so it is not an anonymous artifact by default.

## Building this exact project

The source configures `net48`, a library, `x64`, C# 7.3, and `ScriptVersion=1.7` (`CsiAutoPlan.csproj:20-34`). The solution's visible configuration is called Any CPU, but the project still explicitly sets its binary platform target to x64; do not assume the solution label overrides that setting.

On an authorized development workstation:

1. Open `CsiAutoPlan.sln` in Visual Studio. Install the .NET desktop development tools and .NET Framework 4.8 developer/targeting pack if missing. A runtime alone does not provide all reference assemblies needed to compile. [Microsoft developer pack guide](https://learn.microsoft.com/en-us/dotnet/framework/install/guide-for-developers).
2. Obtain both ESAPI reference DLLs from the actual Eclipse version being targeted. Put them in `CsiAutoPlan/lib/esapi`, or pass their directory explicitly. These libraries are not bundled in the archive.
3. From the `CsiAutoPlan` directory, run `dotnet build src\CsiAutoPlan\CsiAutoPlan.csproj -c Release -p:EsapiDir="C:\path\to\your\esapi\API"`. The quoted directory is a placeholder to replace, not a directory known to exist here.
4. Expect `src\CsiAutoPlan\bin\Release\AutoPlanv1.7.esapi.dll`. Its filename and assembly versions come from `ScriptVersion`. The archive filename does not set the plug-in version.
5. Test on an existing approved non-clinical system through the site's process; do not turn a clinical database into a research database just to try the script.
6. In Eclipse External Beam Planning, open the intended test patient/image/structure set and use the site's Tools > Scripts folder selection. The DLL name must end in `.esapi.dll`.
7. After reviewing the result, save or discard in Eclipse. Record the exact DLL version and log with the test results.

Reference probing is in this order: explicit `EsapiDir`, `ESAPI_DIR` environment value, `lib/esapi`, installed RTM 18.1 path, installed RTM 18.0 path (`csproj:43-47`). An explicitly configured but incorrect directory prevents later fallbacks. The build's `CheckEsapiReferences` target provides the missing-DLL error (`79-86`). C# 7.3 and .NET Framework 4.8 are independent choices: changing language syntax does not add new Eclipse functionality.

For a changed release, increase `<ScriptVersion>` (for example 1.7 to 1.8), rebuild, test, review, and follow the site's approval/replacement process. Do not edit the original 1.7 DLL under an old version label. The build accepts 2-4 dotted numeric components, but its regex does not itself check every underlying assembly-version numeric limit (`csproj:67-73`).

The README references a separate `CsiAutoPlan-verification` package (`README.md:227-234`). It is absent from the supplied archive. Do not report those tests as run or assume that a fake-object harness demonstrates real dose correctness. Official [Varian code samples](https://github.com/VarianAPIs/Varian-Code-Samples) include a PluginTester; a local real-ESAPI test environment is still needed.

## First function to add for practice

Start with a read-only structure inventory. It teaches methods, LINQ, null checks, logging and integration without changing geometry or dose. Add `Esapi/StructureInventory.cs` to a development copy:

```csharp
using System;
using System.Linq;
using System.Text;
using VMS.TPS.Common.Model.API;

namespace CsiAutoPlan.Esapi
{
    public static class StructureInventory
    {
        public static string Build(StructureSet structureSet)
        {
            if (structureSet == null)
                return "No structure set is selected.";

            var report = new StringBuilder();
            report.AppendLine("Structure inventory");
            foreach (var structure in structureSet.Structures.OrderBy(x => x.Id))
            {
                report.AppendLine(string.Format(
                    "{0}: type={1}; empty={2}; approved={3}",
                    structure.Id, structure.DicomType,
                    structure.IsEmpty, structure.IsApproved));
            }
            return report.ToString();
        }
    }
}
```

In `App.Run`, after the successful image/structure-set checks and before the dialog, add `log.Info(StructureInventory.Build(session.SS));`. This is an illustrative source addition, not an edit already made to the clinical plug-in. SDK-style projects automatically include normal `.cs` files under the project directory unless exclusions are configured; this project shows no such exclusions. Check the final log against Eclipse's structure list, including an empty structure and an unapproved structure. Use the site's actual reference assemblies to compile the example.

To add a function that writes data, specify its input, expected output, preconditions and behavior on existing objects. Place numerical logic in `Core`, Eclipse reads/writes in `Esapi`, persistent configurable values in `SettingsModel` plus `SiteSettings`, and per-run choices in `CsiSession`. Expose an optional step in `MainForm`, then call it from `App.Execute` at the dependency-correct point and explicitly stop dependent steps on failure. Use `RunLog.Info`, `Warn`, `Error` and `Manual` to make its actual result visible. Add a new checkbox only if the user needs a new choice; a helper used by an existing step needs no new checkbox.

Before adding an advanced API call, open the site's ESAPI help or Visual Studio Object Browser for the exact DLL, confirm its type/overload/units/license requirements, and try it on a small test case. Finding a same-named member with reflection is not proof that it has the required clinical meaning. The public online references used here are mostly 17.0 and 15.6; they support stable fundamentals and do not prove every 18.x proton overload in this project.

## A learning sequence tailored to this code

1. **Read and display:** build the original, inspect one test structure set, learn `ScriptContext`, null checks and collections. Run only read-only exercises first.
2. **Trace the program:** step through `Script.Execute`, `App.Run`, `CsiSession.Analyze` and `MainForm.OnRun`; identify the exact write boundary.
3. **Change one compiled setting:** adjust a harmless display/configuration value in a copy, raise the version, rebuild, and verify the expected difference. Then learn recipe evaluation with a small synthetic structure set.
4. **Understand geometry separately:** work through millimeter/centimeter and DICOM/user-axis conversions, then calculate one field split by hand and compare with the preview.
5. **Add one read-only report:** implement the inventory above and then required-structure checks; learn informative errors before automating new writes.
6. **Extend one write operation:** add a simple derived helper structure through the existing recipe mechanism, including behavior on rerun and on a missing source structure. Review its contours independently.
7. **Study proton planning APIs:** only after the earlier steps, follow plan/field creation, objectives, uncertainty scenarios and optimization. Test the entire dependency chain and evaluate the resulting plan through the local physics process.

## Troubleshooting by symptom

| Symptom | First thing to inspect |
|---|---|
| Missing `VMS.TPS.Common.Model.API` / namespace error | Correct ESAPI reference DLLs and `EsapiDir`; read the first build error. |
| MSB3644 / .NET reference assemblies missing | Framework 4.8 targeting pack, not merely the installed runtime. |
| NU1301 / restore errors | Network/package source access as well as installed targeting pack; the error alone does not establish one cause. |
| New C# example fails to compile | Project uses C# 7.3, so remove newer syntax or intentionally assess a compiler-version change. |
| DLL not listed in Eclipse | The selected scripts folder and `.esapi.dll` suffix. |
| DLL loads but `BeginModifications` fails | Approval status, licensed capability, patient locks and write permission; inspect the exception. |
| Room unavailable or plan creation blocked | Room settings in `SiteSettings`; Tab 5 and `ExistingPlanReader` may help identify real IDs. |
| The UI preview looks right but plan differs | Confirm selected target/room/scheme and inspect actual applied plan properties; computed intent and API readback are different evidence. |
| Eclipse appears frozen during F | Current synchronous design; the dialog warns about this. Do not move ESAPI calls to a worker thread as a quick fix. |
| Report says finished but lists manual items | Complete and verify those items; `Manual` indicates remaining work, not automated success. |
| Changes disappear after closing | Plug-in changes need saving in Eclipse. |

## The separate DICOM room helper

`tools/DicomRoomSummary/room_summary.py` is an offline Python utility, not ESAPI and not loaded by `Script.cs`. It requires Python, NumPy and pydicom (module header, `26-29`). It recursively finds `.dcm` inputs (`32-40`), reads RTPLAN files with an ion beam sequence (`72-79`), extracts treatment fields and technical identifiers (`84-99`), aggregates observed values by the first treatment field's machine (`102-113`), and prints suggested `MachineOption` code (`117-141`). No settings file or C# source is written.

The arrangement classification is heuristic (`54-65`): it treats fields farther than 30 degrees from posterior 180 degrees as the lateral set, finds a posterior field at the same superior-inferior isocenter, and calls it PostExtended if its lowest spot level exceeds the lateral field's inferior extent by more than 30 mm. It does not inspect clinical intent, and its coordinate calculation does not account for every possible rotated geometry.

It selects the most frequent identifiers and highlights ties (`119,126-131`). For known IBA room names it uses fixed field limits; for an unknown room it rounds the largest observed spot extent up to a centimeter (`121-123`). An observed extent is not a commissioned machine limit. Suggested lines hardcode an IBA display suffix, scanning technique, 30 cm initial snout position and 37 by 45 cm snout footprint (`134-141`), so the output is material for local checking rather than ready-to-apply universal configuration.

The header says it reads approved exports, but the implementation does not check `ApprovalStatus`. It flags files not marked `PatientIdentityRemoved=YES` and prints their basenames (`80-81,115-116`); it does not anonymize files, and a basename can itself contain patient information. Malformed beam sequences can raise exceptions after the guarded `dcmread` block. These are useful limits to understand before extending the utility.
