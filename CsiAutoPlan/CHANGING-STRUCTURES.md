# Changing structures: `StructureRecipes.cs`

Since version 1.6 everything about structures is in **one file**:

```
src/CsiAutoPlan/StructureRecipes.cs
```

That file holds:

- which structures the script reads from the patient (BODY, AI brain and thecal sac, involved VBs, the physician CTV and couch patterns);
- the PBSTV margins and the other structure options;
- every structure the script creates, as a list of **recipes**: ID, DICOM type, and how it is built.

You don't need to touch any other file to rename, reshape, add or remove a structure. The rest of the script (fields, isocenters, objectives, the step-D check, the DVH summary) finds the structures through the recipes.

All other site settings (rooms, fields, objectives, HU rules) are in `SiteSettings.cs` (README §4). Both files are compiled into the plugin; there is no settings file.

Every change to `StructureRecipes.cs` is a code change: raise `<ScriptVersion>`, build, test, approve (section 8 and README §12.2). The examples in section 4 were compiled against ESAPI and run in the offline harness of the verification package, so you can copy them as they are.

---

## 1. The file at a glance

| Part | What is there | Typical edit |
|---|---|---|
| 1. Structures read from the patient | `Body`, `AiBrain`, `AiThecalSac`, `InvolvedVb` (IDs separated by `\|`, first non-empty match wins); `PhysicianCtvRegex`, `CouchRegex` | add the name your AI tool produces |
| 2. Margins and options | `BrainMargins`, `SpineMargins` (R/L/A/P/I/S mm), `CropToBody`, `BodyCropInsetMm`, `InvVbCropMm`, `SplitSpineOptPerField`, `TempPrefix`, `FieldCodes` | margins from the PBS margin spreadsheet |
| 3. Recipes | the structures the script creates, in the order they are built | rename, reshape, add, remove |

The default recipes reproduce the clinical CSI plans:

| # | Recipe | Step | DICOM type | Role | Built as |
|---|---|---|---|---|---|
| 1 | `(D)CTV_BRAIN` | A | CTV | `DraftBrain` | copy of the AI brain (not overwritten if it exists, unless ticked in tab 1) |
| 2 | `(D)CTV_THECALSAC` | A | CTV | `DraftThecalSac` | copy of the AI thecal sac |
| 3 | `(D)CTV_CSI_DRAFT` | C | CONTROL | `DraftUnion` | brain OR thecal sac; only for a **non-clinical** dry run without physician CTV |
| 4 | `{F}_CSI` | C | – | (working volume, not saved) | this field's part of the CTV + PBSTV margins (brain margins for BR, spine margins otherwise; extended POST field: + its brain part with brain margins), cropped to the BODY |
| 5 | `(D)PBSTV_{F}_COMPO` | C | PTV | `FieldTarget` | `{F}_CSI` OR the involved VBs in the planes of the field target |
| 6 | `(D)PBSTV_{F}_OPT` | C | CONTROL | `FieldOpt` | `{F}_CSI` (spine fields only if `SplitSpineOptPerField`) |
| 7 | `(D)PBSTV_SPN_OPT` | C | CONTROL | `SpineOpt` | union of the spine `{F}_CSI` (only if `SplitSpineOptPerField = false`) |
| 8 | `(D)INV_VB_OPT` | C | CONTROL | `VbOpt` | involved VBs minus (all `{F}_CSI` + `InvVbCropMm`) |

`{F}` is the field code, taken from `FieldCodes`: `BR` (brain field target, used by RPO/LPO and, in the PostBrain layout, by POST), `POST` (POST extended down the spine), `UP`, `MID`, `MID2`, `LOW`. A per-field recipe makes one structure for each field of the current split, e.g. `(D)PBSTV_BR_COMPO`, `(D)PBSTV_UP_COMPO`, `(D)PBSTV_LOW_COMPO`.

Where one field's target stops and the next starts (the sup-inf split) is **not** in this file. It comes from the field split in `Core/CsiGeometryPlanner.cs` (section 5), which uses the S/I values of `BrainMargins` / `SpineMargins`.

---

## 2. What do I change?

| I want to … | Change in `StructureRecipes.cs` | Example |
|---|---|---|
| recognise another AI / BODY / VB name | `AiBrain`, `AiThecalSac`, `Body`, `InvolvedVb` | 4.2 |
| pick the physician CTV by another pattern | `PhysicianCtvRegex` | – |
| change the PBSTV margins | `BrainMargins`, `SpineMargins` | 4.3 |
| different margins for one field (e.g. POST) | a new `MarginSet` + the `{F}_CSI` recipe | 4.4 |
| rename a created structure | the ID in its recipe, or `FieldCodes` for `{F}` | 4.1 |
| change the VB part of the COMPO | the COMPO recipe | 4.5 |
| change what an OPT is (e.g. minus lenses) | the OPT recipe | 4.6 |
| one `(D)PBSTV_SPN_OPT` instead of one OPT per spine field | `SplitSpineOptPerField = false` | – |
| not crop to the BODY, or crop a few mm inside it | `CropToBody`, `BodyCropInsetMm` | – |
| change the 0.5 cm crop of `(D)INV_VB_OPT` | `InvVbCropMm` | – |
| add a structure | a new recipe at the end of the list | 4.7, 4.8 |
| change the (D)CTV copies of step A | recipes 1 and 2 | 4.9 |
| stop creating a structure | remove its recipe (or let it return `null`) | 4.10 |

Not in `StructureRecipes.cs`:

| I want to … | Where |
|---|---|
| fields aimed at the OPT instead of the COMPO | `SiteSettings.cs` → `Beams.FieldTargetIsCompo = false` |
| objectives on a structure, RO flag | `SiteSettings.cs` → `Objectives` (`Structure = "(D)PBSTV_BR_EVAL"` or a token, section 3.3) |
| HU override rules | `SiteSettings.cs` → `HuOverrides` |
| 2 cm overlap, shoulder clearance, field limit | `SiteSettings.cs` → `Geometry`, rooms in `Machines` |
| proximal / distal **field** target margins (R × 3.5 % + 3 mm) | computed per field in step D, `SiteSettings.cs` → `RangeMargins` (README §5) |
| where one field target ends and the next starts | code: `Core/CsiGeometryPlanner.cs` (section 5) |

---

## 3. How recipes work

### 3.1 The three kinds

```csharp
StructureRecipe.Once(id, dicomType, step, role, c => volume)                 // one structure
StructureRecipe.Once(id, dicomType, step, role, c => volume, c => condition) // ... only if condition is true
StructureRecipe.PerFieldStructure(id, dicomType, role, f => volume)          // one per field, id contains {F}
StructureRecipe.PerFieldStructure(id, dicomType, role, f => volume, f => appliesToThisField)
StructureRecipe.PerFieldVolume(name, f => volume)                            // per-field working volume, not saved
```

- `id`: the structure ID (up to 64 characters). Per-field recipes need `{F}` in the ID.
- `dicomType`: `"PTV"`, `"CTV"`, `"GTV"`, `"ORGAN"`, `"AVOIDANCE"`, `"CONTROL"`, …
- `step`: `Step.A` (with the (D) starting structures, tab 3 step A) or `Step.C` (with the field targets, step C). Per-field recipes are always step C.
- `role`: what the rest of the script uses it for (3.3). `Role.None` = only created.
- `c => …` / `f => …`: how the volume is built (3.2). Return `null` for "don't create it this time"; an existing structure with that ID from an earlier run is then removed (step C). The same happens when the condition (`when` / `appliesTo`) is false.

**Order matters.** Recipes run top to bottom. A recipe can use (`Get`) only what was built above it.

**What the script does for you** (no code needed):

- An existing structure is overwritten in step C. In step A it is kept unless *Overwrite existing (D)CTV_BRAIN / (D)CTV_THECALSAC* is ticked in tab 1, because the dosimetrist edits those.
- Approved or locked structures are never edited; the log says so.
- Input structures (AI contours, CTV, BODY, VBs) are only read.
- High-resolution inputs: Boolean operations need matching resolution, so when any input is high resolution, every input is used as a high-resolution temporary copy. Step A structures keep the resolution of their source.
- Temporary structures (`zzCSI…`) are deleted at the end of each step, even after an error.
- Per-field structures of fields that are not in the current split (e.g. `MID` from an earlier run) are removed.
- Field targets: extent logged, neighbour overlaps checked (≥ 2 cm), checked again before step D.
- Every structure you add appears in the DVH summary after step F.

### 3.2 What a recipe can use

In `c => …` (every recipe) and `f => …` (per-field recipes, which can also use everything in `c`):

| Name | What it is |
|---|---|
| `c.Ctv` | physician CTV_#### selected in tab 1 (in a non-clinical dry run: the draft union). Step C only |
| `c.Body` | BODY |
| `c.Vb` | involved vertebral bodies, `null` if there are none |
| `c.Brain`, `c.ThecalSac` | `(D)CTV_BRAIN` / `(D)CTV_THECALSAC` if they exist, otherwise the AI contours |
| `c.AiBrain`, `c.AiThecalSac` | the AI contours selected in tab 1 |
| `c.Find("Lens_L\|LENS_L")` | any structure by ID (`\|` = alternatives), `null` if missing or empty |
| `c.Get("(D)PBSTV_BR_COMPO")` | a structure or working volume built **above** in this run, `null` if not built |
| `c.UnionOver(c.AllFields, "{F}_CSI")` | union over fields of a per-field structure / volume (`c.SpineFields` = UP, MID, MID2, LOW only) |
| `c.AddMargins(v, BrainMargins)` | R/L/A/P/I/S margins from a `MarginSet` |
| `c.CropToBody(v)` | `v` AND BODY (minus `BodyCropInsetMm`) if `CropToBody` is true |
| `c.Slab(zLo, zHi)` | everything on the image planes zLo ≤ z ≤ zHi (mm) |
| `c.UsesDraftCtv` | true in a non-clinical dry run without physician CTV |
| `f.CtvSlab` | the planes of this field's part of the CTV (open toward the vertex for BR / POST, toward the sacrum for the last field) |
| `f.TargetSlab` | the planes of this field's planned field target |
| `f.BrainPartSlab` | extended POST field only: the planes of its brain part; `null` for every other field |
| `f.Get("{F}_CSI")` | `{F}` is replaced by this field's code |
| `f.Code` | this field's code (`BR`, `UP`, …) |
| `f.IsBrainField`, `f.IsPostAtBrainIso`, `f.IsSpineField` | which field this is |
| `f.IsTop`, `f.IsLast` | the field target reaches the vertex / the sacrum |
| `f.Segment` | the planned field split for this field: `CtvCutHiZ`, `CtvCutLoZ`, `PbstvHiZ`, `PbstvLoZ`, `IsoZ` (mm) |

Volume operations (ESAPI `SegmentVolume`, each returns a new volume):

| Operation | Meaning |
|---|---|
| `a.And(b)`, `a.Or(b)`, `a.Sub(b)`, `a.Xor(b)` | intersection, union, a without b, either but not both |
| `a.Margin(5)` | uniform 5 mm margin; negative = shrink; at most 50 mm per call |
| `c.AddMargins(a, set)` | different margin per direction (`MarginSet`) |

Coordinates are DICOM patient coordinates in mm: +x = patient left, +y = posterior, **+z = superior**. "Y" in the dialog and log is the Eclipse user coordinate in cm.

Recipes are C# lambdas. `c => expression` returns the expression. For several statements use braces and `return`:

```csharp
f =>
{
    var part = f.Get("{F}_CSI");
    if (!f.IsBrainField) return part;
    return part.Sub(something);
}
```

Use `x == null ? … : …` whenever something can be missing (`c.Vb`, `c.Find(...)`): an operation on `null` stops step C with an error naming the recipe.

### 3.3 Roles

A role tells the rest of the script what a structure is for. Each role (except `None`) is used by **one** recipe.

| Role | Used by | Objective token |
|---|---|---|
| `FieldTarget` (per field, required) | field target of each field (unless `FieldTargetIsCompo = false`), spine isocenter (middle of the structure), field-limit check, overlap check, step-D check | `@BR_COMPO` |
| `FieldOpt` (per field) | optimization targets; field target if `FieldTargetIsCompo = false` | `@BR_OPT`, `@POST_OPT`, `@SPN_OPT` (the spine fields' OPTs) |
| `SpineOpt` | one OPT for all spine fields | `@SPN_OPT` when there are no per-field spine OPTs |
| `VbOpt` | involved VBs outside the CSI targets | `@VB_OPT` |
| `DraftBrain`, `DraftThecalSac` | preferred over the AI contours for geometry (shoulders, spine iso depth) and the draft CTV | – |
| `DraftUnion` | CTV of a non-clinical dry run without physician CTV | `@CTV` |
| `None` | only created; use its ID directly in `Objectives` (`SiteSettings.cs`) | – |

Objective tokens are resolved by role, so renaming a structure doesn't require changing `Objectives` in `SiteSettings.cs`.

---

## 4. Examples (tested)

Find the place by the quoted code; line numbers change between versions.

### 4.1 Rename a structure

Change the ID in its recipe, e.g. the INV_VB OPT:

```csharp
StructureRecipe.Once("(D)VB_INVOLVED_OPT", "CONTROL", Step.C, Role.VbOpt,
```

To change what `{F}` becomes (here `LO` instead of `LOW` for the lower spine field):

```csharp
public static readonly string[] FieldCodes = { "BR", "POST", "UP", "MID", "MID2", "LO" };
```

This renames `(D)PBSTV_LOW_COMPO` and `(D)PBSTV_LOW_OPT` to `(D)PBSTV_LO_COMPO` and `(D)PBSTV_LO_OPT`. Fields, objectives and checks follow automatically. The script no longer recognises structures created earlier under the old name: delete them in Contouring and run step C again.

### 4.2 Recognise other input names

```csharp
public const string AiBrain = "Brain|BRAIN|Brain_AI|AI_Brain|Brain_MIM";
public const string InvolvedVb = "VBs_INVOLVED|VB_INVOLVED|INVOLVED_VBs|VBs_Involved|Vertebrae_Involved";
```

The first name that exists and is not empty wins. The planner can still pick another structure in tab 1.

### 4.3 Change the PBSTV margins

```csharp
public static readonly MarginSet BrainMargins = new MarginSet { RightMm = 7, LeftMm = 7, AntMm = 5, PostMm = 5, InfMm = 3, SupMm = 3 };
```

The S/I values (`InfMm`, `SupMm`) also go into the field split, so the planned field-target extents in tab 2 follow them.

### 4.4 Different margins for one field (POST)

Add a margin set in part 2:

```csharp
public static readonly MarginSet PostMargins = new MarginSet { RightMm = 5, LeftMm = 5, AntMm = 4, PostMm = 4, InfMm = 3, SupMm = 3 };
```

and use it in the `{F}_CSI` recipe:

```csharp
StructureRecipe.PerFieldVolume("{F}_CSI", f =>
{
    var m = f.IsBrainField ? BrainMargins : f.IsPostAtBrainIso ? PostMargins : SpineMargins;
    var part = f.AddMargins(f.Ctv.And(f.CtvSlab), m);
    if (f.BrainPartSlab != null) part = part.Or(f.AddMargins(f.Ctv.And(f.BrainPartSlab), BrainMargins));
    return f.CropToBody(part);
}),
```

Keep the S/I margins of a new set equal to `SpineMargins` (or `BrainMargins` for a brain-field set). The field split only knows those two, and step D refuses a field target whose ends differ from the plan by more than 5 mm + 2 slices.

### 4.5 Change the VB part of the field target

With a 3 mm margin on the VBs:

```csharp
StructureRecipe.PerFieldStructure("(D)PBSTV_{F}_COMPO", "PTV", Role.FieldTarget,
    f => f.Vb == null ? f.Get("{F}_CSI") : f.Get("{F}_CSI").Or(f.Vb.Margin(3).And(f.TargetSlab))),
```

Without the VBs in the field target:

```csharp
StructureRecipe.PerFieldStructure("(D)PBSTV_{F}_COMPO", "PTV", Role.FieldTarget,
    f => f.Get("{F}_CSI")),
```

### 4.6 Brain OPT minus the lenses + 3 mm

```csharp
StructureRecipe.PerFieldStructure("(D)PBSTV_{F}_OPT", "CONTROL", Role.FieldOpt,
    f =>
    {
        var opt = f.Get("{F}_CSI");
        if (!f.IsBrainField) return opt;
        foreach (var lens in new[] { f.Find("Lens_L|LENS_L|Lt_Lens"), f.Find("Lens_R|LENS_R|Rt_Lens") })
            if (lens != null) opt = opt.Sub(lens.Margin(3));
        return opt;
    },
    f => !f.IsSpineField || SplitSpineOptPerField),
```

The `(D)INV_VB_OPT` crop uses `{F}_CSI`, not the OPT, so it doesn't change.

### 4.7 Add a structure

`(D)PBSTV_BR_EVAL` = brain field target 3 mm inside the BODY, for evaluation. At the end of the list (the commented example in the file):

```csharp
StructureRecipe.Once("(D)PBSTV_BR_EVAL", "CONTROL", Step.C, Role.None,
    c => c.Get("(D)PBSTV_BR_COMPO").And(c.Body.Margin(-3))),
```

It is created in step C, appears in the DVH summary, and can get an objective by its ID in `SiteSettings.cs` → `Objectives`:

```csharp
new ObjectiveTemplate { Structure = "(D)PBSTV_BR_EVAL", Type = ObjectiveType.Lower, VolumePercent = 95, DosePercentRx = 100, Priority = 100, Robust = false },
```

### 4.8 Add a structure per field

A 3–10 mm ring around each spine field target:

```csharp
StructureRecipe.PerFieldStructure("(D)RING_{F}", "CONTROL", Role.None,
    f => f.Get("(D)PBSTV_{F}_COMPO").Margin(10).Sub(f.Get("(D)PBSTV_{F}_COMPO").Margin(3)),
    f => f.IsSpineField),
```

Gives `(D)RING_UP` and `(D)RING_LOW` (and `(D)RING_MID` when there are three spine fields).

### 4.9 Change the step-A copies

Grow the brain copy by 2 mm:

```csharp
StructureRecipe.Once("(D)CTV_BRAIN", "CTV", Step.A, Role.DraftBrain, c => c.AiBrain == null ? null : c.AiBrain.Margin(2)),
```

Another starting structure in step A, e.g. a copy of the physician CTV for editing:

```csharp
StructureRecipe.Once("(D)CTV_EVAL", "CTV", Step.A, Role.None, c => c.Find("CTV_3600|CTV_2340")),
```

### 4.10 Stop creating a structure

Delete its line(s) or put `//` in front. Two exceptions: the `Role.FieldTarget` recipe is required, and removing a recipe that a later one uses with `Get` makes that one fail. A removed role makes its objective token find nothing (logged as "Objective skipped").

To skip a structure only in some cases, return `null` or add a condition, e.g. `c => c.UsesDraftCtv ? null : …` (not in a dry run without physician CTV) or `c => c.Vb == null ? null : …` (only with involved VBs).

---

## 5. Outside `StructureRecipes.cs` (rarely needed)

| Change | File |
|---|---|
| where one field target ends and the next starts | `Core/CsiGeometryPlanner.cs` → `PlanCore` (no ESAPI, unit-tested in the verification package on the clinical cases and 2,000 random patients) |
| a new helper for recipes (e.g. `f.Something`) | `Esapi/RecipeEngine.cs`, classes `RecipeContext` / `FieldContext` |
| a new role (a structure another part of the script must find) | `Esapi/RecipeEngine.cs` (enum `Role`) and the part that uses it (`PlanBuilder.FieldTarget`, `OptimizationBuilder.Resolve`, …) |
| a separate step with its own checkbox | `App.cs` (`Execute`), `UI/MainForm.cs`, `Esapi/CsiSession.cs` |
| HU overrides | `SiteSettings.cs` → `HuOverrides`; code `StructureBuilder.ApplyHuOverrides` |

---

## 6. Things that depend on the structures

| If you change … | keep in mind |
|---|---|
| the sup-inf extent of the field target | step D stops if a field target's ends differ from the planned split by more than 5 mm + 2 slices; spine isocenters go to the middle of the field target and are checked against the field limit |
| the shape of the field target | the overlap of neighbouring field targets must stay ≥ 2 cm (logged; error below) |
| the S/I margins | they drive the field split (tab 2) |
| an OPT structure | objectives find it by role (`@BR_OPT`, …); `Robust="true"` objectives get the RO box |
| the ID of a structure | structures from earlier runs with the old ID are no longer recognised |

---

## 7. Errors and what they mean

| Message | Meaning | Fix |
|---|---|---|
| Visual Studio build error, e.g. `CS1002 ; expected`, `CS1513 } expected` | syntax in the recipe list | each recipe ends with `),` ; braces `{ }` of a multi-line lambda balanced; a `return` in every path |
| `CS0103 The name '…' does not exist` | typo, or a name used outside its lambda (`f.` in a `c =>` recipe) | per-field members (`f.CtvSlab`, `f.IsBrainField`, …) only in per-field recipes |
| `StructureRecipes.cs has errors` (message box at start; nothing is done) | the recipe list itself is wrong: an ID used twice, a per-field ID without `{F}`, a role used twice, no `FieldTarget`, bad `FieldCodes` | the message lists each problem |
| `StructureRecipes.cs: building X failed: Object reference not set …` | the recipe used something that is `null`: `Get()` of a structure built later or not at all, no VB structure, a `Find` that found nothing | reorder, or guard with `== null ? null : …` |
| `X: no source structure (see tab 1) - not created` | a step-A recipe returned `null` (e.g. no AI brain) | select the structure in tab 1 |
| `'X' already exists and was left unchanged` | step A keeps edited (D) structures | tick *Overwrite existing …* in tab 1 if you want them rebuilt |
| `'X' exists but cannot be edited` | approved or locked structure | unapprove it in Contouring, or rename the recipe |
| `Field target … does not exist / spans …` before step D | field targets missing or from another layout or margin | tick step C |

The offline harness (verification package) runs the same start-up check and stops with exit code 3 if the recipe list is wrong.

---

## 8. Testing a structure change

1. **Build** in Release (`<ScriptVersion>` raised first, README §12.2). Fix compile errors.
2. **Unit tests** (verification package, `tests/`): `dotnet run -c Release` → `0 failures`.
3. **Offline harness** (verification package, `harness/`: `dotnet run` or `dotnet run -- --dialog`; options in its README). The run must end without `Error` lines, and each new structure must appear in the log (`(D)… : … cc`) and in the DVH summary. The harness phantom has simplified Boolean operations, so it checks order, names and control flow, not shapes. Run it with `--vb`, `--post-extended`, `--separate` and `--hires` as well if you changed a per-field recipe or anything involving the VBs.
4. **Non-clinical Eclipse** (Research Mode), 2–3 patients you know:
   - run with only steps A–C ticked;
   - in Contouring, check each new or changed structure: axial, sagittal and coronal views, volume against the log, sup-inf ends against tab 2, the `Overlap …` lines;
   - no `zzCSI…` structures left;
   - then run step D and check that the fields target the right structures and the isocenters are unchanged (or changed as intended).
5. `CHANGELOG.md` line (what changed, test patients), physics review, deploy, approve (README §12.2).

---

## 9. File map

```
src/CsiAutoPlan/
  StructureRecipes.cs          ALL structure settings and recipes (this guide)
  Esapi/RecipeEngine.cs        runs the recipes: roles, contexts, slabs, resolution, clean-up, checks
  Esapi/StructureBuilder.cs    step A / C entry points, HU overrides (step B)
  Esapi/StructureTools.cs      Find, GetOrCreate, CreateSlab, ApplyMargins, MatchResolution, SliceExtents
  Esapi/CsiSession.cs          finds the input structures, step-D check
  Esapi/PlanBuilder.cs         fields and isocenters on the FieldTarget / FieldOpt structures
  Esapi/OptimizationBuilder.cs objective tokens (@BR_OPT, …) -> structures by role, DVH summary
  Core/CsiGeometryPlanner.cs   field split: sup-inf range of every field target
  SiteSettings.cs              rooms, fields, range margins, objectives, HU rules (no structure settings)

CsiAutoPlan-verification/      separate package, not deployed
  tests/                       unit tests (geometry, range margins, SiteSettings.cs)
  harness/                     runs everything on stub ESAPI; checks StructureRecipes.cs first
```
