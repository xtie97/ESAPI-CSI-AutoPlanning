# CSI proton PBS auto planning and ESAPI learning guide

Source code and a beginner walkthrough for extending a craniospinal irradiation planning plugin in Eclipse **v18**. The supplied project version is **AutoPlanv1.7** and targets **.NET Framework 4.8, x64 and C# 7.3**.

## Start here

- [Complete walkthrough](docs/START_HERE.md): ESAPI fundamentals, C# concepts, build instructions, execution order and extension examples.
- [ESAPI beginner notes](docs/esapi_basics.md): object relationships, user interface, debugging and build troubleshooting.
- [Core and structure analysis](docs/core_analysis.md): selection, geometry, recipes and helper behavior.
- [Proton implementation analysis](docs/proton_analysis.md): plan and beam creation, models, margins, uncertainties, optimization and failure handling.
- [Procedure comparison](docs/procedure_matrix.md): differences between the supplied clinical reference and the implemented behavior.
- [Source index](docs/SOURCE_INDEX.md): links to the 22 C# files and 205 method declarations.
- [Review validation](docs/REVIEW_VALIDATION.md): scope and limits of the review.

## Repository contents

| Folder | Contents |
|---|---|
| `CsiAutoPlan/` | The supplied source project and its original documentation, preserved byte for byte. |
| `docs/` | Authored walkthrough and static code/procedure review. |
| `examples/` | Read-only ESAPI teaching methods outside the original plugin build. |
| `verification/` | Reproducible offline checks for ESAPI-independent numerical classes. |

The original clinical Word procedure, screenshots, extracted procedure text, patient data, proprietary reference DLLs, local ZIP package and authoring helpers are not included. Procedure paragraph labels in the comparison refer to the clinical source supplied for the review.

## Build the original plugin

Open `CsiAutoPlan/CsiAutoPlan.sln` in Visual Studio with the .NET desktop tools and the .NET Framework 4.8 targeting pack. Obtain both `VMS.TPS.Common.Model.API.dll` and `VMS.TPS.Common.Model.Types.dll` from the matching Eclipse v18 system. These proprietary libraries are not distributed here.

From the repository root:

```powershell
dotnet build .\CsiAutoPlan\src\CsiAutoPlan\CsiAutoPlan.csproj -c Release -p:EsapiDir="C:\PATH\TO\YOUR\V18\esapi\API"
```

Replace the placeholder with the actual directory containing both reference DLLs. Follow the site's development, validation and script approval process for any deployment.

## Run the offline checks

From the repository root in PowerShell 7:

```powershell
& '.\verification\offline_core_checks.ps1'
```

All **18 offline checks passed** during this review and again after relocating the check script for this repository. They compile five unchanged numerical C# classes and use synthetic inputs. They do not access Eclipse or patient data.

The full plugin and the new ESAPI examples have **not been compiled or executed against Eclipse v18** during this review. No clinical commissioning or live dose validation is established by these checks. The original source documentation contains historical testing claims; the accompanying review distinguishes those claims from work actually performed here.

## Important implementation findings

Field-specific perturbations are generated as a manual checklist. Several logged errors do not stop later operations. Existing-plan selection needs stronger course and structure-set identity checks. Some defaults and structure definitions differ from the supplied procedure. Palliative CSI workflow is not implemented. Read the walkthrough and comparison before extending the planning workflow.

The teaching methods are separate from the original source and are not automatically part of its build. The walkthrough explains where to insert them in a development copy and how to propagate validation failures.
