# Procedure and implementation comparison

This matrix compares the supplied 2026 craniospinal procedure with the actual version 1.7 source. Procedure paragraph identifiers refer to the supplied source procedure, not Word page numbers. The original clinical procedure, its extracted text and its screenshots are not included in this repository. Text and screenshot differences should be resolved by the local clinical owner; this review does not silently choose a new clinical rule. A source code setting is not evidence of successful Eclipse execution.

| Procedure requirement or topic | Actual source behavior | Status and evidence |
|---|---|---|
| Curative and palliative pathways (P026, P052, P152) | Implements a curative-oriented path; no explicit treatment-intent mode or posterior-only palliative beam workflow | Gap. `PlanBuilder.CreateFields` always adds RPO/LPO, lines 247–248 |
| Palliative course intent plus 30 Gy in 10 fractions (P026) | Default is 20 × 1.8 Gy; dialog can change Rx but does not establish palliative workflow | Gap. `SiteSettings.cs` 91–92; `PlanBuilder.cs` 95 |
| CT simulation positioning and scan checks (P028–P046) | Requires axis-aligned axial CT and HFS, reads structures, and estimates geometry | Partial. `StructureTools.cs` 49–53; `CsiSession.cs` 276–277; image quality/positioning assessment remains human |
| Brain iso at user origin or appropriate position (P043–P049, P104) | Uses user origin by default, computed position when absent or selected; dialog allows review | Partial automated geometry. `SiteSettings.cs` 69; `CsiSession.cs` 215–227 |
| Field size ~17 cm P1/P3, ~20 cm P4/P5, ~12.5 cm ProBeam360 half extent (P046) | Configures P1/P3 at 170 mm, P4/P5 at 200 mm; no ProBeam360 room supplied | Partial. `SiteSettings.cs` 45–60; ProBeam360 needs its own commissioned configuration |
| Insert kVue couch and simulation overlay (P056–P057) | Detects structures matching a regex and logs reminder if none; support device selection is separate | Manual insertion/verification. `CsiSession.cs` 225–226; `PlanBuilder.cs` 54 |
| Draft brain/thecal sac copying, manual extensions and approvals (P059–P066) | Draft recipes provide starting structures; clinical anatomical editing and approval remain outside automation | Partial, inspect structure modules and recipe notes |
| Physician combines brain and thecal sac with stated Boolean AND (P062) | Development path explicitly calls its operation a draft union | Procedure ambiguity for local clarification. `CsiSession.cs` 36; do not infer that AND is an instruction to change the algorithm |
| Physician approves CTV and involved VBs (P062–P065) | Selects physician-named CTVs; no automated clinical contour approval | Human responsibility |
| At least 2 cm adjacent field target overlap (P068–P072) | Configured minimum 20 mm, preference 50 mm when possible | Automated scalar split, verify actual structures. `SiteSettings.cs` 71–72 |
| Brain field target at least 2 cm above shoulders (P069) | Configured 20 mm clearance and shoulder detection/review | Partial automated geometry. `SiteSettings.cs` 70 |
| Apply PBS margin spreadsheet and include involved VBs in same plane (P068) | Structure recipes handle geometry; range builder uses R×3.5%+3 mm and (R−SOBP)×3.5%+3 mm for beam margins | Partial and interpretation to validate. Actual spreadsheet not supplied; range and structure margins must not be conflated |
| Brain/spine OPTs cropped from VBs, VB OPT cropped 0.5 cm from CSI volume (P073–P076) | OPT equals the CSI working volume and retains overlap with VBs; spine OPTs are split by field by default. VB OPT subtracts a 5 mm expansion of the CSI union | Definition differs from the procedure around VBs. `StructureRecipes.cs` 106–118; these roles drive objective resolution in `OptimizationBuilder.cs` 107–129 |
| BB, artifacts, clips, shunt HU decisions (P078–P090) | Configurable HU override rules and manual/report-only paths | See structure analysis; saturation and material decisions need local review |
| Other OAR contours and constraints from EBRT Order (P091–P092, P198) | Only example lens objectives are supplied plus a manual reminder | Partial. `SiteSettings.cs` 195–209; `OptimizationBuilder.cs` 96 |
| Heterogeneity correction (P096) | Selects proton models; no explicit heterogeneity option is set or read back in planning builder | Verify locally; `PlanBuilder.cs` 141–186 |
| Dose grid 0.25 cm (P097) | Attempts configured model option, otherwise checklist | Partial. `PlanBuilder.cs` 159–164 |
| Course C#-CSI, plan INITIAL, Rx from EBRT Order (P098–P100) | Uses course format and plan ID; dialog Rx defaults; writes prescription; verification reminder | Automated setup with human Rx verification. `SiteSettings.cs` 89–92; `PlanBuilder.cs` 91–95 |
| MFO enabled (P100, P183) | Directly calls MFO setter | Automated. `PlanBuilder.cs` 98 |
| Spot spacing overwrite True and 0.5 cm in both directions (P101) | Four model option keys with values True/0.5; rejects log errors | Partial, model dependent. `SiteSettings.cs` 104–109; `PlanBuilder.cs` 166–184 |
| LPO/RPO ~95/265 degrees (P104–P105) | Exactly 95/265 by default | Automated. `SiteSettings.cs` 120–121; `PlanBuilder.cs` 247–248 |
| Brain target COMPO, lateral 0.5 cm, axial 0 (P106–P107) | COMPO default and 5 mm lateral; fixed brain proximal and distal are both 5 mm, then range-derived replacement enabled | Discrepancy needing local decision. `SiteSettings.cs` 128–130, 141, 161 |
| Optional posterior brain if needed (P108) | All supplied rooms default PostBrain, which adds posterior brain routinely | Different default workflow. `SiteSettings.cs` 47–59; `CsiSession.cs` 67–81 |
| Spine iso at target midpoint, whole-number shifts (P110–P111, P115–P116) | Uses actual target contour midpoint and whole-cm offset from brain; x follows brain and common AP spine position | Automated with coordinate assumptions. `PlanBuilder.cs` 273–283, 307–315 |
| Spine lateral 0.5 cm, distal 0.5 cm, proximal 0 (P112, P117) | Fixed margins all 5 mm, then range-derived proximal/distal replacement enabled | Discrepancy needing local decision. `SiteSettings.cs` 131–133, 159–165 |
| Air gap 5 cm (P104, P112, P117, P186) | Estimates from contour/snout geometry; writes snout position; manual fallback and collision reminder | Partial. `PlanBuilder.cs` 424–513 |
| Range shifter on all treatment fields (P118, P185) | Uses room RS ID and setting IN through editable beam parameters | Automated attempt with ID fallback. `PlanBuilder.cs` 365–374, 394–404 |
| Field weights 1.0 (P119) | Sets editable WeightFactor 1.0 | Automated. `PlanBuilder.cs` 403 |
| NUPO optimizer (P121) | Configured/harvested model; prefer NUPO when harvesting; if absent leaves current default and checklist | Partial, not an enforced requirement. `PlanBuilder.cs` 145–157, 195–209 |
| RO only on named target objectives (P122–P125) | Templates request RO on BR/POST/SPN/VB OPTs; runtime reflection tries setter; otherwise manual | Partial and version dependent. `OptimizationBuilder.cs` 64–95; `ApiCapabilities.cs` 54–177 |
| Individual RPO/LPO Y and Z +/-0.3 cm: eight scenarios (P126–P128) | Generates eight descriptions by default, never writes them | Manual. `OptimizationBuilder.cs` 160–166; `PerturbationPlanner.cs` Build |
| Brain pair Y and each POST Y +/-0.3 cm (P129–P131) | Generates paired/individual descriptions, posterior brain included with POST category | Manual. `OptimizationBuilder.cs` 173–184; `PerturbationPlanner.cs` Build |
| Curative perturbation screenshots | Embedded images show paired brain Y, posterior Y and individual brain Z screenshots; text additionally specifies individual brain Y | Text/screenshot difference to clarify rather than silently simplify |
| NTO off (P132) | Attempts removal of NTO parameters, setting is false | Automated attempt; swallowed removal errors mean verify state. `OptimizationBuilder.cs` 29–34 |
| Maximum iterations blank, maximum optimization time 30 minutes (P133) | Sets 300 max iterations; time key empty so no 30 minute option is written | Discrepancy. `SiteSettings.cs` 111–112, 175; `OptimizationBuilder.cs` 191–204 |
| Automatic Optimization Mode and start from zero (P134) | Calls `OptimizeIMPT` with `RestartOptimization`; no explicit UI Automatic Mode checkbox call | Partial API mapping. `OptimizationBuilder.cs` 204 |
| Allow full optimization, additional iterations as needed (P135–P136) | One optimize call; repeated F starts from zero each time | Current limitation. `OptimizationBuilder.cs` 204 |
| Split spine OPT / increase margins / reduce spot spacing if needed (P147–P150) | Configurable recipes and spacing; logs troubleshooting reminder | Available modification path, clinical adjustment remains human |
| Palliative couch copying, delete ISO_SETUP, export CT, insert protocol (P155–P168) | No such export/deletion/protocol insertion pipeline | Not implemented; document instructions were reference content, not commands to execute |
| Palliative three isocenter groups with treatment/setup fields; RadA/RadB setup units/angles (P176–P182) | Creates treatment fields from geometry; no setup field generation | Not implemented. `PlanBuilder.cs` 285 |
| Palliative plan +/-0.3 cm and +/-3.5% (P191 and screenshot) | Adds default twelve plan-specific RO scenarios when plan has none | Automated for all workflows, not only palliative. `OptimizationBuilder.cs` 139–157 |
| Palliative field Y/Z shifts and CTV RO (P192–P195) | Field scenarios remain manual; default objectives use OPTs, no default `@CTV` objective | Palliative-specific configuration not implemented |
| Calculate dose and collision check before optimizing (P187–P188) | F calculates beam line first, then optimizes, then final dose; collision check stays manual | Sequence differs from palliative procedure. `OptimizationBuilder.cs` 199–210; `PlanBuilder.cs` 285 |
| EBRT goal checks, field gradients and brain uniformity (P197–P202) | Nominal DVH summary of selected targets; field/junction assessment reminder | Partial report, no automatic clinical acceptance. `OptimizationBuilder.cs` 225–252 |
| Physics pre-MD check and physician review (P199–P203) | Logs final manual task; does not approve anything | Human review. `App.cs` 143–144 |

## Highest priority workflow cautions

The code can proceed into optimization immediately after printing manual RO or field perturbation instructions. It also can continue after exceptions in margin, uncertainty or objective setup because the orchestrator ignores those failure returns. For a learning walkthrough, separate plan/objective creation from optimization and explicitly inspect the resulting settings before step F. This is an explanation of current behavior, not evidence of completed commissioning.

The phrase `Auto` should be read as a code path that attempts a write. Accepted model options, correct structures, correct devices, required uncertainties, acceptable dose and clinical approval are distinct states that the present code does not collectively prove.
