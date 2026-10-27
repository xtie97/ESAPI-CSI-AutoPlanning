using CsiAutoPlan.Settings;
using CsiAutoPlan.Esapi;
using VMS.TPS.Common.Model.API;

namespace CsiAutoPlan
{
    /// <summary>
    /// ===================================================================================================
    ///  THE ONE PLACE to change the structures of the CSI script:
    ///    1. which structures it reads from the patient,
    ///    2. margins and options,
    ///    3. which structures it creates - name, DICOM type, and how each one is built.
    ///  Nothing about structures needs to be changed anywhere else. After a change: raise ScriptVersion in
    ///  CsiAutoPlan.csproj, build, run the verification checks, test on the non-clinical system
    ///  (README 12.2, CHANGING-STRUCTURES.md).
    /// ===================================================================================================
    /// </summary>
    public static class StructureRecipes
    {
        // ------------------------------------------------------------------------------------------------
        // 1. STRUCTURES READ FROM THE PATIENT
        //    '|' separates alternative IDs (case-insensitive, first non-empty match wins). The planner can
        //    still pick a different structure in tab 1 of the dialog.
        // ------------------------------------------------------------------------------------------------
        public const string Body = "BODY|Body|External|EXTERNAL|Skin";
        public const string AiBrain = "Brain|BRAIN|Brain_AI|AI_Brain";
        public const string AiThecalSac = "ThecalSac|Thecal_Sac|THECALSAC|Thecal Sac|SpinalCanal|Spinal_Canal|Canal_Spinal";
        public const string InvolvedVb = "VBs_INVOLVED|VB_INVOLVED|INVOLVED_VBs|VBs_Involved";
        /// <summary>Physician CTV (regular expression). Several matches: the one with the largest sup-inf extent.</summary>
        public const string PhysicianCtvRegex = @"^CTV_\d+";
        /// <summary>Couch / insert structures (regular expression): "couch inserted" check and air-gap geometry.</summary>
        public const string CouchRegex = @"couch|kvue|bos_insert|table";

        // ------------------------------------------------------------------------------------------------
        // 2. MARGINS AND OPTIONS (mm)
        //    PBSTV margins from the PBS margin spreadsheet. Clinical CSI example: brain R/L 6, A/P 4, S/I 3;
        //    spine 4/4/4/4/3/3. The S/I values also drive the field split (where one field target ends).
        // ------------------------------------------------------------------------------------------------
        public static readonly MarginSet BrainMargins = new MarginSet { RightMm = 6, LeftMm = 6, AntMm = 4, PostMm = 4, InfMm = 3, SupMm = 3 };
        public static readonly MarginSet SpineMargins = new MarginSet { RightMm = 4, LeftMm = 4, AntMm = 4, PostMm = 4, InfMm = 3, SupMm = 3 };
        /// <summary>Crop field targets / OPTs to the BODY (CropToBody), optionally this far inside it.</summary>
        public const bool CropToBody = true;
        public const double BodyCropInsetMm = 0;
        /// <summary>(D)INV_VB_OPT = involved VBs cropped away from the CSI targets by this margin (procedure: 0.5 cm).</summary>
        public const double InvVbCropMm = 5;
        /// <summary>true: one OPT per spine field ((D)PBSTV_UP_OPT, (D)PBSTV_LOW_OPT, as in the clinical plans);
        /// false: one (D)PBSTV_SPN_OPT for the whole spine (procedure text).</summary>
        public const bool SplitSpineOptPerField = true;
        /// <summary>Prefix of temporary structures; everything starting with it is deleted at the end of a step.</summary>
        public const string TempPrefix = "zzCSI";
        /// <summary>What {F} becomes in per-field structure IDs, for the fields: brain (RPO/LPO, and POST in the PostBrain
        /// layout), POST extended down the spine, upper / middle / second middle / lower spine.</summary>
        public static readonly string[] FieldCodes = { "BR", "POST", "UP", "MID", "MID2", "LOW" };

        // ------------------------------------------------------------------------------------------------
        // 3. STRUCTURES THE SCRIPT CREATES, in the order they are built.
        //
        //    StructureRecipe.Once(id, dicomType, step, role, c => volume [, when])        one structure
        //    StructureRecipe.PerFieldStructure(id, dicomType, role, f => volume [, appliesTo])
        //                                                   one structure per field, {F} in the ID becomes
        //                                                   the field's code from FieldCodes (BR, POST, UP, MID, MID2, LOW)
        //    StructureRecipe.PerFieldVolume(name, f => volume)  per-field working volume, not saved
        //
        //    Returning null means "don't create it" (an existing one from an earlier run is removed).
        //    Role tells the rest of the script what the structure is for (fields, objectives, checks); see
        //    the Role enum in Esapi/RecipeEngine.cs. Role.None = only created.
        //
        //    In c => ... (and f => ...):
        //      c.Ctv, c.Body, c.Vb (involved VBs), c.Brain / c.ThecalSac ((D) structure if present, else AI),
        //      c.AiBrain, c.AiThecalSac, c.Find("ID|ID2"), c.Get("(D)PBSTV_BR_OPT") (built earlier this run),
        //      c.UnionOver(c.AllFields or c.SpineFields, "name with {F}"), c.AddMargins(v, BrainMargins),
        //      c.CropToBody(v), c.Slab(zLo, zHi).
        //    Only in f => ...: f.CtvSlab (this field's part of the CTV), f.TargetSlab (planned field-target
        //      planes), f.BrainPartSlab (extended POST field only, else null), f.Code (what {F} became), f.IsBrainField,
        //      f.IsPostAtBrainIso, f.IsSpineField, f.IsTop, f.IsLast, f.Segment (planned z ranges, mm),
        //      f.Get("{F}_CSI") (this field's volume).
        //    Volumes: v.And(w), v.Or(w), v.Sub(w), v.Xor(w), v.Margin(mm) (negative = shrink, |mm| <= 50),
        //      c.AddMargins(v, marginSet) for R/L/A/P/I/S margins. DICOM mm, +z = superior.
        // ------------------------------------------------------------------------------------------------
        public static readonly StructureRecipe[] Recipes =
        {
            // ---- step A: dosimetrist starting points, copies of the AI contours. Not overwritten if they exist,
            //      unless "Overwrite existing (D)CTV_BRAIN / (D)CTV_THECALSAC" is ticked in tab 1.
            StructureRecipe.Once("(D)CTV_BRAIN", "CTV", Step.A, Role.DraftBrain, c => c.AiBrain),
            StructureRecipe.Once("(D)CTV_THECALSAC", "CTV", Step.A, Role.DraftThecalSac, c => c.AiThecalSac),

            // ---- step C, only without a physician CTV (NON-CLINICAL dry run): brain OR thecal sac
            StructureRecipe.Once("(D)CTV_CSI_DRAFT", "CONTROL", Step.C, Role.DraftUnion,
                c => c.Brain == null || c.ThecalSac == null ? null : c.Brain.Or(c.ThecalSac)),

            // ---- step C, per field: the CSI part of the field target =
            //      this field's part of the CTV + PBSTV margins (brain margins for BR, spine margins otherwise),
            //      for the extended POST field also its brain part with the brain margins, cropped to the BODY.
            StructureRecipe.PerFieldVolume("{F}_CSI", f =>
            {
                var part = f.AddMargins(f.Ctv.And(f.CtvSlab), f.IsBrainField ? BrainMargins : SpineMargins);
                if (f.BrainPartSlab != null) part = part.Or(f.AddMargins(f.Ctv.And(f.BrainPartSlab), BrainMargins));
                return f.CropToBody(part);
            }),

            // COMPO (field target) = CSI part OR the involved VBs in the planes of the field target (no margin)
            StructureRecipe.PerFieldStructure("(D)PBSTV_{F}_COMPO", "PTV", Role.FieldTarget,
                f => f.Vb == null ? f.Get("{F}_CSI") : f.Get("{F}_CSI").Or(f.Vb.And(f.TargetSlab))),

            // OPT = the CSI part (one per spine field, or none for spine fields if SplitSpineOptPerField = false)
            StructureRecipe.PerFieldStructure("(D)PBSTV_{F}_OPT", "CONTROL", Role.FieldOpt,
                f => f.Get("{F}_CSI"),
                f => !f.IsSpineField || SplitSpineOptPerField),

            // ---- step C, once
            // one spine OPT for all spine fields (only if SplitSpineOptPerField = false)
            StructureRecipe.Once("(D)PBSTV_SPN_OPT", "CONTROL", Step.C, Role.SpineOpt,
                c => c.UnionOver(c.SpineFields, "{F}_CSI"),
                c => !SplitSpineOptPerField),

            // involved VBs outside the CSI targets (cropped by InvVbCropMm); none without a VB structure
            StructureRecipe.Once("(D)INV_VB_OPT", "CONTROL", Step.C, Role.VbOpt,
                c => c.Vb == null ? null : c.Vb.Sub(c.UnionOver(c.AllFields, "{F}_CSI").Margin(InvVbCropMm))),

            // ---- add your own structures here, for example (remove the // to use it):
            // StructureRecipe.Once("(D)PBSTV_BR_EVAL", "CONTROL", Step.C, Role.None,
            //     c => c.Get("(D)PBSTV_BR_COMPO").And(c.Body.Margin(-3))),
        };
    }
}
