# Source navigation index

All links point to the unchanged files extracted from your archive. Line numbers refer to this supplied version 1.7.

The method list is a declaration-based navigation aid. Properties, settings fields, nested data objects and recipe lambdas are explained in the companion notes and remain visible in the full source.

[Start with the walkthrough](<START_HERE.md>).

The project contains 22 C# files. The archive contains 30 regular files.

## Project and original documentation

- [CsiAutoPlan.sln](<../CsiAutoPlan/CsiAutoPlan.sln>)
- [src/CsiAutoPlan/CsiAutoPlan.csproj](<../CsiAutoPlan/src/CsiAutoPlan/CsiAutoPlan.csproj>)
- [README.md](<../CsiAutoPlan/README.md>)
- [CHANGING-STRUCTURES.md](<../CsiAutoPlan/CHANGING-STRUCTURES.md>)
- [CHANGELOG.md](<../CsiAutoPlan/CHANGELOG.md>)
- [lib/esapi/README.txt](<../CsiAutoPlan/lib/esapi/README.txt>)
- [tools/DicomRoomSummary/room_summary.py](<../CsiAutoPlan/tools/DicomRoomSummary/room_summary.py>)

## C sharp source files

### App.cs

Read, review, write and report orchestration; phase dependencies.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/App.cs>) — 216 lines.

- [Run — line 17](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L17>)
- [Execute — line 85](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L85>)
- [ScriptName — line 148](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L148>)
- [PluginVersion — line 159](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L159>)
- [FindExistingPlan — line 170](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L170>)
- [Guard — line 178](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L178>)
- [Guard — line 180](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L180>)
- [LoadSettings — line 194](<../CsiAutoPlan/src/CsiAutoPlan/App.cs#L194>)

### Core/AirGapCalculator.cs

Pure contour projection and snout-position arithmetic.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/AirGapCalculator.cs>) — 71 lines.

- [MaxDepthTowardSource — line 23](<../CsiAutoPlan/src/CsiAutoPlan/Core/AirGapCalculator.cs#L23>)
- [SnoutPositionMm — line 58](<../CsiAutoPlan/src/CsiAutoPlan/Core/AirGapCalculator.cs#L58>)
- [Contour — line 69](<../CsiAutoPlan/src/CsiAutoPlan/Core/AirGapCalculator.cs#L69>)

### Core/CsiGeometryPlanner.cs

Pure numeric SI target splitting and isocenter placement.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs>) — 281 lines.

- [Clone — line 48](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L48>)
- [IsoOffsetFromBrainCm — line 61](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L61>)
- [ToString — line 65](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L65>)
- [All — line 82](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L82>)
- [Chain — line 90](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L90>)
- [Plan — line 135](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L135>)
- [PlanCore — line 152](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L152>)
- [ChooseIso — line 261](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L261>)
- [RoundToCm — line 276](<../CsiAutoPlan/src/CsiAutoPlan/Core/CsiGeometryPlanner.cs#L276>)

### Core/PerturbationPlanner.cs

Pure scenario specifications for plan and field shifts.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/PerturbationPlanner.cs>) — 85 lines.

- [ToString — line 13](<../CsiAutoPlan/src/CsiAutoPlan/Core/PerturbationPlanner.cs#L13>)
- [Build — line 29](<../CsiAutoPlan/src/CsiAutoPlan/Core/PerturbationPlanner.cs#L29>)
- [Single — line 62](<../CsiAutoPlan/src/CsiAutoPlan/Core/PerturbationPlanner.cs#L62>)
- [PlanScenarios — line 70](<../CsiAutoPlan/src/CsiAutoPlan/Core/PerturbationPlanner.cs#L70>)

### Core/RangeMarginCalculator.cs

Pure range/SOBP validity checks, formula and rounding.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/RangeMarginCalculator.cs>) — 49 lines.

- [IsUsable — line 19](<../CsiAutoPlan/src/CsiAutoPlan/Core/RangeMarginCalculator.cs#L19>)
- [DistalMm — line 31](<../CsiAutoPlan/src/CsiAutoPlan/Core/RangeMarginCalculator.cs#L31>)
- [ProximalMm — line 37](<../CsiAutoPlan/src/CsiAutoPlan/Core/RangeMarginCalculator.cs#L37>)
- [RoundUp — line 43](<../CsiAutoPlan/src/CsiAutoPlan/Core/RangeMarginCalculator.cs#L43>)

### Core/RunLog.cs

Message levels, events, report formatting and local log writing.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs>) — 101 lines.

- [ToString — line 17](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L17>)
- [Info — line 31](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L31>)
- [Warn — line 32](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L32>)
- [Error — line 33](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L33>)
- [Manual — line 34](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L34>)
- [Info — line 36](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L36>)
- [Warn — line 37](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L37>)
- [Error — line 38](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L38>)
- [Manual — line 39](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L39>)
- [Add — line 41](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L41>)
- [Count — line 49](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L49>)
- [BuildReport — line 51](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L51>)
- [SaveTo — line 84](<../CsiAutoPlan/src/CsiAutoPlan/Core/RunLog.cs#L84>)

### Core/ShoulderDetector.cs

Pure numeric shoulder-top estimation from body widths.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Core/ShoulderDetector.cs>) — 82 lines.

- [WidthSample — line 11](<../CsiAutoPlan/src/CsiAutoPlan/Core/ShoulderDetector.cs#L11>)
- [Detect — line 32](<../CsiAutoPlan/src/CsiAutoPlan/Core/ShoulderDetector.cs#L32>)

### Esapi/ApiCapabilities.cs

Public runtime API member inspection and robust-flag invocation attempts.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs>) — 228 lines.

- [IsRobustName — line 52](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L52>)
- [Probe — line 54](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L54>)
- [FindOverload — line 120](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L120>)
- [TryAddRobust — line 136](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L136>)
- [TrySetRobust — line 154](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L154>)
- [ReadRobust — line 173](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L173>)
- [Fail — line 180](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L180>)
- [RobustSummary — line 187](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L187>)
- [Describe — line 200](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L200>)
- [SafeTypes — line 216](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L216>)
- [Sig — line 223](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ApiCapabilities.cs#L223>)

### Esapi/CsiSession.cs

Selected ESAPI inputs, per-run choices, anatomy and geometry proposal.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs>) — 442 lines.

- [SchemeFor — line 67](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L67>)
- [SchemeDescription — line 84](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L84>)
- [ApplySchemeDefaults — line 94](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L94>)
- [FieldTargetProblems — line 101](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L101>)
- [PlanMachine — line 129](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L129>)
- [UsePlanSettings — line 140](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L140>)
- [SameId — line 167](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L167>)
- [CsiSession — line 184](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L184>)
- [Analyze — line 206](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L206>)
- [PickCsiCtv — line 253](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L253>)
- [BrainSource — line 269](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L269>)
- [CanalSource — line 270](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L270>)
- [Reevaluate — line 273](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L273>)
- [ToGy — line 285](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L285>)
- [MeasureAnatomy — line 290](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L290>)
- [RecomputeCtvExtent — line 335](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L335>)
- [Recompute — line 351](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L351>)
- [ToUserYcm — line 402](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L402>)
- [FromUserYcm — line 403](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L403>)
- [IsoOffsetYcm — line 404](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L404>)
- [IsoOffsetZcm — line 405](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L405>)
- [SpineIso — line 407](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L407>)
- [SuggestCourseId — line 409](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L409>)
- [BuildHuRows — line 427](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/CsiSession.cs#L427>)

### Esapi/ExistingPlanReader.cs

Read technical settings from this patient's existing proton plans.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs>) — 244 lines.

- [Read — line 39](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L39>)
- [RangeNote — line 214](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L214>)
- [Options — line 226](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L226>)
- [AddDistinct — line 233](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L233>)
- [Safe — line 238](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L238>)
- [NullIfEmpty — line 239](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L239>)
- [Empty — line 240](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L240>)
- [First — line 241](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L241>)
- [Median — line 242](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/ExistingPlanReader.cs#L242>)

### Esapi/OptimizationBuilder.cs

Objectives, uncertainty creation/checklist, optimization, dose and DVHs.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs>) — 255 lines.

- [OptimizationBuilder — line 19](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L19>)
- [SetupObjectives — line 22](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L22>)
- [Dose — line 100](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L100>)
- [Resolve — line 107](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L107>)
- [SetupUncertainties — line 133](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L133>)
- [ClassifyBeams — line 173](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L173>)
- [OptimizeAndCalculate — line 188](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L188>)
- [ReportDose — line 225](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/OptimizationBuilder.cs#L225>)

### Esapi/PlanBuilder.cs

Course/ion-plan creation, machine/model/options, fields and snout setting.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs>) — 517 lines.

- [PlanBuilder — line 20](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L20>)
- [CreatePlan — line 23](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L23>)
- [SupportDeviceCandidates — line 110](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L110>)
- [StripMachinePrefix — line 131](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L131>)
- [SetCalculationModels — line 141](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L141>)
- [SafeOptions — line 189](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L189>)
- [TrySetModel — line 195](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L195>)
- [SetOption — line 216](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L216>)
- [LogModulatedScanningOptions — line 230](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L230>)
- [CreateFields — line 237](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L237>)
- [FieldTarget — line 290](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L290>)
- [SpineSuffix — line 301](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L301>)
- [IsoFromStructure — line 307](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L307>)
- [CheckFieldLimit — line 318](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L318>)
- [AddField — line 329](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L329>)
- [NewBeam — line 388](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L388>)
- [ApplyFieldParameters — line 394](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L394>)
- [Pick — line 407](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L407>)
- [PlanAirGap — line 424](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L424>)
- [Outlines — line 451](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L451>)
- [ReportAirGap — line 472](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/PlanBuilder.cs#L472>)

### Esapi/RangeMarginBuilder.cs

Nominal-range readback and beam-margin application/restoration.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs>) — 139 lines.

- [RangeMarginBuilder — line 25](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs#L25>)
- [Apply — line 34](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs#L34>)
- [Read — line 114](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs#L114>)
- [Restore — line 133](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RangeMarginBuilder.cs#L133>)

### Esapi/RecipeEngine.cs

Recipe types, role registry, contexts, evaluation, writes and cleanup.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs>) — 471 lines.

- [Once — line 47](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L47>)
- [PerFieldStructure — line 54](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L54>)
- [PerFieldVolume — line 61](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L61>)
- [IdFor — line 66](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L66>)
- [FieldCode — line 79](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L79>)
- [ByRole — line 88](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L88>)
- [Id — line 91](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L91>)
- [Validate — line 100](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L100>)
- [RecipeContext — line 143](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L143>)
- [Find — line 162](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L162>)
- [Get — line 165](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L165>)
- [UnionOver — line 168](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L168>)
- [AddMargins — line 184](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L184>)
- [CropToBody — line 187](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L187>)
- [Slab — line 196](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L196>)
- [FieldContext — line 202](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L202>)
- [Get — line 242](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L242>)
- [RecipeEngine — line 260](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L260>)
- [RunStepA — line 263](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L263>)
- [RunStepC — line 294](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L294>)
- [StepC — line 301](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L301>)
- [BuildFailed — line 374](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L374>)
- [CheckOverlaps — line 382](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L382>)
- [Write — line 400](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L400>)
- [Vol — line 436](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L436>)
- [Slab — line 462](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L462>)
- [NextTmp — line 469](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/RecipeEngine.cs#L469>)

### Esapi/StructureBuilder.cs

Draft contour steps, field-target entry point and HU overrides/sampling.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs>) — 135 lines.

- [StructureBuilder — line 19](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L19>)
- [CreateDraftCtvs — line 23](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L23>)
- [ApplyHuOverrides — line 35](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L35>)
- [NeighbourMeanHu — line 63](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L63>)
- [VoxelIndex — line 123](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L123>)
- [CreateFieldTargets — line 131](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L131>)
- [NextTmp — line 133](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureBuilder.cs#L133>)

### Esapi/StructureTools.cs

Structure lookup, creation, editing, extents, slabs, resolution and removal.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs>) — 203 lines.

- [Find — line 24](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L24>)
- [FindExact — line 35](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L35>)
- [FindByRegex — line 40](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L40>)
- [PlaneZ — line 47](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L47>)
- [CheckAxial — line 49](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L49>)
- [SliceExtents — line 57](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L57>)
- [PolygonArea — line 83](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L83>)
- [GetOrCreate — line 96](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L96>)
- [MatchResolution — line 123](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L123>)
- [CreateSlab — line 138](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L138>)
- [ApplyMargins — line 172](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L172>)
- [SafeRemove — line 181](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L181>)
- [RemoveIfPresent — line 189](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L189>)
- [RemoveTemporaries — line 197](<../CsiAutoPlan/src/CsiAutoPlan/Esapi/StructureTools.cs#L197>)

### Script.cs

Eclipse entry point and write-enabled metadata.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Script.cs>) — 29 lines.

- [Script — line 14](<../CsiAutoPlan/src/CsiAutoPlan/Script.cs#L14>)
- [Execute — line 16](<../CsiAutoPlan/src/CsiAutoPlan/Script.cs#L16>)

### Settings/SettingsModel.cs

Settings types, validation, named options and objective/HU models.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs>) — 324 lines.

- [Validate — line 31](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L31>)
- [IncompleteMachines — line 58](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L58>)
- [ExpandPath — line 69](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L69>)
- [MissingIds — line 98](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L98>)
- [ToCSharp — line 110](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L110>)
- [ToString — line 123](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L123>)
- [IsValid — line 136](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L136>)
- [All — line 169](<../CsiAutoPlan/src/CsiAutoPlan/Settings/SettingsModel.cs#L169>)

### SiteSettings.cs

Actual compiled values for rooms, plan, beams, uncertainty, objectives and HU rules.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/SiteSettings.cs>) — 235 lines.

- [Create — line 18](<../CsiAutoPlan/src/CsiAutoPlan/SiteSettings.cs#L18>)

### StructureRecipes.cs

Input aliases, structure margins, output IDs and volume-building recipes.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/StructureRecipes.cs>) — 125 lines.

This file primarily declares settings and recipe data; open it to inspect the initializers.

### UI/MainForm.cs

Five review tabs, initial pass selections and OnRun validation.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs>) — 475 lines.

- [MainForm — line 35](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L35>)
- [Build — line 50](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L50>)
- [BuildTargetsTab — line 81](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L81>)
- [BuildGeometryTab — line 99](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L99>)
- [BuildPlanTab — line 165](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L165>)
- [BuildHuTab — line 206](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L206>)
- [BuildInfoTab — line 220](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L220>)
- [LoadValues — line 230](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L230>)
- [OnStructuresChanged — line 279](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L279>)
- [RefreshGeometry — line 295](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L295>)
- [SyncAnatomyControls — line 339](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L339>)
- [UpdateTotal — line 348](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L348>)
- [UpdateRunState — line 350](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L350>)
- [OnRun — line 355](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L355>)
- [Table — line 424](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L424>)
- [Pair — line 431](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L431>)
- [AddCombo — line 437](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L437>)
- [Nud — line 444](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L444>)
- [Clamp — line 449](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L449>)
- [Step — line 451](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L451>)
- [Fill — line 458](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L458>)
- [Pick — line 466](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L466>)
- [F — line 473](<../CsiAutoPlan/src/CsiAutoPlan/UI/MainForm.cs#L473>)

### UI/ReportForm.cs

Live log and final report display.

[Open source](<../CsiAutoPlan/src/CsiAutoPlan/UI/ReportForm.cs>) — 53 lines.

- [ReportForm — line 15](<../CsiAutoPlan/src/CsiAutoPlan/UI/ReportForm.cs#L15>)
- [OnAdded — line 35](<../CsiAutoPlan/src/CsiAutoPlan/UI/ReportForm.cs#L35>)
- [ShowFinal — line 42](<../CsiAutoPlan/src/CsiAutoPlan/UI/ReportForm.cs#L42>)
