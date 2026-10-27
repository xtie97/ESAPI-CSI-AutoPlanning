using System;
using System.Collections.Generic;
using System.Linq;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// The machinery behind StructureRecipes.cs. You normally do not need to edit this file:
// what is built, and how, is defined in StructureRecipes.cs.
namespace CsiAutoPlan.Esapi
{
    /// <summary>When a recipe runs: step A (dosimetrist starting points) or step C (field targets and friends).</summary>
    public enum Step { A, C }

    /// <summary>
    /// What other parts of the script use a structure for. Exactly one per-field recipe must have FieldTarget.
    /// None = only created (e.g. an evaluation structure); use its ID directly in objectives.
    /// </summary>
    public enum Role
    {
        None,
        DraftBrain,      // (D)CTV_BRAIN: preferred over the raw AI brain for geometry and the draft CTV
        DraftThecalSac,  // (D)CTV_THECALSAC
        DraftUnion,      // CTV used when no physician CTV exists (non-clinical dry run); objective token @CTV
        FieldTarget,     // per field: field target of the field (step D), isocenter, overlap check; token @BR_COMPO
        FieldOpt,        // per field: optimization target; tokens @BR_OPT, @POST_OPT, @SPN_OPT
        SpineOpt,        // one OPT for all spine fields; token @SPN_OPT when there are no per-field spine OPTs
        VbOpt,           // involved vertebral bodies outside the CSI targets; token @VB_OPT
    }

    /// <summary>One entry of StructureRecipes.Recipes.</summary>
    public class StructureRecipe
    {
        public string Id;                 // structure ID; per-field recipes contain {F} (StructureRecipes.FieldCodes)
        public string DicomType;          // PTV, CTV, CONTROL, ORGAN, AVOIDANCE, ...
        public Step Step;
        public Role Role;
        public bool PerField;
        public bool Saved = true;         // false = intermediate volume, usable by later recipes but not written to Eclipse
        public Func<RecipeContext, SegmentVolume> Build;
        public Func<FieldContext, SegmentVolume> BuildField;
        public Func<RecipeContext, bool> When;          // once-recipes: build only if true (null = always)
        public Func<FieldContext, bool> AppliesTo;      // per-field recipes: build for this field only if true (null = all)

        /// <summary>A structure built once (not per field).</summary>
        public static StructureRecipe Once(string id, string dicomType, Step step, Role role,
                                           Func<RecipeContext, SegmentVolume> build, Func<RecipeContext, bool> when = null)
        {
            return new StructureRecipe { Id = id, DicomType = dicomType, Step = step, Role = role, Build = build, When = when };
        }

        /// <summary>A structure built for every field of the split ({F} in the ID = field code). Always step C.</summary>
        public static StructureRecipe PerFieldStructure(string id, string dicomType, Role role,
                                                        Func<FieldContext, SegmentVolume> build, Func<FieldContext, bool> appliesTo = null)
        {
            return new StructureRecipe { Id = id, DicomType = dicomType, Step = Step.C, Role = role, PerField = true, BuildField = build, AppliesTo = appliesTo };
        }

        /// <summary>A per-field working volume ({F} in the name) that later recipes read with Get("..."); not written to Eclipse.</summary>
        public static StructureRecipe PerFieldVolume(string name, Func<FieldContext, SegmentVolume> build)
        {
            return new StructureRecipe { Id = name, Step = Step.C, PerField = true, Saved = false, BuildField = build };
        }

        public string IdFor(string fieldCode) { return PerField ? Id.Replace("{F}", fieldCode) : Id; }
    }

    /// <summary>Lookups into StructureRecipes.Recipes for the rest of the script.</summary>
    public static class RecipeBook
    {
        /// <summary>Names of the field-split segments (Core/CsiGeometryPlanner), in the order of StructureRecipes.FieldCodes.</summary>
        public static readonly string[] SegmentNames = { "BR", "POST", "UP", "MID", "MID2", "LO" };

        /// <summary>What {F} becomes, per field (StructureRecipes.FieldCodes).</summary>
        public static string[] AllFieldCodes { get { return StructureRecipes.FieldCodes; } }

        /// <summary>Field-split segment name (BR, POST, UP, MID, MID2, LO) -> field code used in structure IDs.</summary>
        public static string FieldCode(string segmentName)
        {
            int i = Array.IndexOf(SegmentNames, segmentName);
            var codes = StructureRecipes.FieldCodes;
            return i >= 0 && codes != null && i < codes.Length && !string.IsNullOrWhiteSpace(codes[i]) ? codes[i] : segmentName;
        }

        public static IEnumerable<StructureRecipe> All { get { return StructureRecipes.Recipes.Where(r => r != null); } }

        public static StructureRecipe ByRole(Role role) { return All.FirstOrDefault(r => r.Role == role && r.Saved); }

        /// <summary>ID of the structure with this role (for a per-field role: of this field), or null.</summary>
        public static string Id(Role role, string segmentName = null)
        {
            var r = ByRole(role);
            if (r == null) return null;
            if (!r.PerField) return r.Id;
            return segmentName == null ? null : r.IdFor(FieldCode(segmentName));
        }

        /// <summary>Problems with the recipe list itself (shown at start; errors block step C).</summary>
        public static List<string> Validate()
        {
            var p = new List<string>();
            var codes = StructureRecipes.FieldCodes;
            if (codes == null || codes.Length != SegmentNames.Length || codes.Any(string.IsNullOrWhiteSpace))
                p.Add("FieldCodes needs " + SegmentNames.Length + " non-empty codes (brain, POST, upper, mid, mid2, lower).");
            else if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
                p.Add("FieldCodes contains the same code twice.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in All)
            {
                if (string.IsNullOrWhiteSpace(r.Id)) { p.Add("A recipe has no ID."); continue; }
                bool hasF = r.Id.Contains("{F}");
                if (r.PerField && !hasF) p.Add("Per-field recipe '" + r.Id + "' needs {F} in its ID (otherwise every field overwrites the same structure).");
                if (!r.PerField && hasF) p.Add("Recipe '" + r.Id + "' contains {F} but is not per field.");
                if (r.Saved && string.IsNullOrWhiteSpace(r.DicomType)) p.Add("Recipe '" + r.Id + "' has no DICOM type.");
                if ((r.PerField ? r.BuildField == null : r.Build == null)) p.Add("Recipe '" + r.Id + "' has no build function.");
                foreach (var code in r.PerField && hasF ? AllFieldCodes : new[] { "" })
                {
                    var id = r.IdFor(code);
                    if (r.Saved && id.Length > 64) p.Add("Structure ID '" + id + "' is longer than 64 characters.");
                    if (!ids.Add(id)) p.Add("Structure ID '" + id + "' is defined twice.");
                }
            }
            foreach (Role role in Enum.GetValues(typeof(Role)))
            {
                if (role == Role.None) continue;
                int n = All.Count(r => r.Role == role && r.Saved);
                if (n > 1) p.Add("Role " + role + " is used by " + n + " recipes; use it once.");
            }
            var ft = ByRole(Role.FieldTarget);
            if (ft == null) p.Add("No recipe has Role.FieldTarget: fields would have no target.");
            else if (!ft.PerField) p.Add("The Role.FieldTarget recipe must be per field.");
            var fo = ByRole(Role.FieldOpt);
            if (fo != null && !fo.PerField) p.Add("The Role.FieldOpt recipe must be per field.");
            return p;
        }
    }

    /// <summary>What a recipe can use. All volumes are SegmentVolumes (null when the structure does not exist).</summary>
    public class RecipeContext
    {
        internal RecipeEngine E;
        internal RecipeContext(RecipeEngine engine) { E = engine; }

        /// <summary>Physician CTV_#### (or the draft union in a non-clinical dry run). Step C only.</summary>
        public SegmentVolume Ctv { get { return E.CtvVolume; } }
        /// <summary>BODY (null if CropToBody is off is NOT applied here - this is always the BODY).</summary>
        public SegmentVolume Body { get { return E.BodyVolume; } }
        /// <summary>Involved vertebral bodies (null if none selected).</summary>
        public SegmentVolume Vb { get { return E.VbVolume; } }
        /// <summary>(D)CTV_BRAIN if it exists, otherwise the AI brain.</summary>
        public SegmentVolume Brain { get { return E.Vol(E.S.BrainSource()); } }
        /// <summary>(D)CTV_THECALSAC if it exists, otherwise the AI thecal sac.</summary>
        public SegmentVolume ThecalSac { get { return E.Vol(E.S.CanalSource()); } }
        /// <summary>The AI contours as selected in tab 1 (step A copies these).</summary>
        public SegmentVolume AiBrain { get { return E.Vol(E.S.AiBrain); } }
        public SegmentVolume AiThecalSac { get { return E.Vol(E.S.AiThecalSac); } }
        /// <summary>true in a non-clinical dry run without physician CTV.</summary>
        public bool UsesDraftCtv { get { return E.S.UseDraftUnion; } }

        /// <summary>Any structure in the structure set by ID ('|' = alternatives, first non-empty wins), or null.</summary>
        public SegmentVolume Find(string names) { return E.Vol(StructureTools.Find(E.S.SS, names)); }

        /// <summary>A structure or working volume built earlier in this run, by its ID / name (null if not built).</summary>
        public virtual SegmentVolume Get(string name) { SegmentVolume v; return E.Built.TryGetValue(name, out v) ? v : null; }

        /// <summary>Union over fields of a per-field structure / volume ({F} in the name), null if none built.</summary>
        public SegmentVolume UnionOver(IEnumerable<FieldContext> fields, string perFieldName)
        {
            SegmentVolume u = null;
            foreach (var f in fields)
            {
                var v = Get(perFieldName.Replace("{F}", f.Code));
                if (v != null) u = u == null ? v : u.Or(v);
            }
            return u;
        }

        /// <summary>All fields of the current split / only the spine fields (UP, MID, MID2, LOW).</summary>
        public IList<FieldContext> AllFields { get { return E.Fields; } }
        public IList<FieldContext> SpineFields { get { return E.Fields.Where(f => f.IsSpineField).ToList(); } }

        /// <summary>Margins from a MarginSet (R/L/A/P/I/S mm).</summary>
        public SegmentVolume AddMargins(SegmentVolume v, MarginSet m) { return v == null ? null : StructureTools.ApplyMargins(v, m); }

        /// <summary>Cropped to the BODY (minus StructureRecipes.BodyCropInsetMm) if StructureRecipes.CropToBody is true.</summary>
        public SegmentVolume CropToBody(SegmentVolume v)
        {
            if (v == null || !StructureRecipes.CropToBody || E.BodyVolume == null) return v;
            double inset = StructureRecipes.BodyCropInsetMm;
            return v.And(inset > 0 ? E.BodyVolume.AsymmetricMargin(new AxisAlignedMargins(StructureMarginGeometry.Inner, inset, inset, inset, inset, inset, inset))
                                   : E.BodyVolume);
        }

        /// <summary>Everything on the image planes zLo..zHi (DICOM z, mm, +z superior); snapped outward to whole planes.</summary>
        public SegmentVolume Slab(double zLo, double zHi) { return E.Slab(zLo, zHi); }
    }

    /// <summary>What a per-field recipe can use, in addition to RecipeContext.</summary>
    public class FieldContext : RecipeContext
    {
        internal FieldContext(RecipeEngine e, FieldSegment seg, bool isTop, bool isLast) : base(e) { Segment = seg; IsTop = isTop; IsLast = isLast; Code = RecipeBook.FieldCode(seg.Name); }

        /// <summary>The field split segment: CtvCutHiZ/LoZ (CTV range of this field), PbstvHiZ/LoZ (planned field-target range), IsoZ.</summary>
        public readonly FieldSegment Segment;
        /// <summary>What {F} becomes for this field (StructureRecipes.FieldCodes: BR, POST, UP, MID, MID2 or LOW by default).</summary>
        public readonly string Code;
        /// <summary>The field target reaches the vertex (BR, POST) / the sacrum (last field).</summary>
        public readonly bool IsTop, IsLast;
        public bool IsBrainField { get { return Segment.Name == "BR"; } }
        public bool IsPostAtBrainIso { get { return Segment.Name == "POST"; } }
        public bool IsSpineField { get { return !IsBrainField && !IsPostAtBrainIso; } }

        private SegmentVolume ctvSlab, targetSlab, brainPartSlab;
        private bool brainPartDone;

        /// <summary>Planes of this field's part of the CTV (open toward the vertex for BR/POST and toward the sacrum for the last field).</summary>
        public SegmentVolume CtvSlab
        {
            get { return ctvSlab ?? (ctvSlab = Slab(IsLast ? Segment.CtvCutLoZ - 50 : Segment.CtvCutLoZ, IsTop ? Segment.CtvCutHiZ + 50 : Segment.CtvCutHiZ)); }
        }
        /// <summary>Planes of the planned field target (PBSTV range), open at the vertex / sacrum like CtvSlab.</summary>
        public SegmentVolume TargetSlab
        {
            get { return targetSlab ?? (targetSlab = Slab(IsLast ? Segment.PbstvLoZ - 50 : Segment.PbstvLoZ, IsTop ? Segment.PbstvHiZ + 50 : Segment.PbstvHiZ)); }
        }
        /// <summary>POST field extended down the spine (PostExtended layout): planes of its brain part; null for every other field.</summary>
        public SegmentVolume BrainPartSlab
        {
            get
            {
                if (!brainPartDone)
                {
                    brainPartDone = true;
                    if (IsPostAtBrainIso && E.Geometry.Brain != null) brainPartSlab = Slab(E.Geometry.Brain.CtvCutLoZ, Segment.CtvCutHiZ + 50);
                }
                return brainPartSlab;
            }
        }

        /// <summary>Get("(D)PBSTV_{F}_OPT") = that structure of this field; names without {F} as in RecipeContext.</summary>
        public override SegmentVolume Get(string name) { return base.Get(name.Replace("{F}", Code)); }
    }

    /// <summary>Runs the recipes of a step against the open structure set.</summary>
    public class RecipeEngine
    {
        internal readonly CsiSession S;
        private RunLog Log { get { return S.Log; } }
        internal GeometryPlan Geometry;
        internal SegmentVolume CtvVolume, BodyVolume, VbVolume;
        internal readonly Dictionary<string, SegmentVolume> Built = new Dictionary<string, SegmentVolume>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Structure> Written = new Dictionary<string, Structure>(StringComparer.OrdinalIgnoreCase);
        internal List<FieldContext> Fields = new List<FieldContext>();
        private bool hiRes, stepA;
        private Structure hiResRef;
        private readonly List<Structure> touched = new List<Structure>();   // structures read by the recipe being built
        private int tmpCounter;

        public RecipeEngine(CsiSession session) { S = session; }

        // ------------------------------------------------------------------ step A
        public void RunStepA()
        {
            Log.Step = "D-structures";
            try
            {
                var c = new RecipeContext(this);
                foreach (var r in RecipeBook.All.Where(x => x.Step == Step.A))
                {
                    if (r.When != null && !r.When(c)) continue;
                    // Each step-A structure keeps the resolution of its source(s). Only if a recipe combines
                    // high- and default-resolution structures is it rebuilt from high-resolution working copies.
                    stepA = true; hiRes = false; hiResRef = null; volCache.Clear(); touched.Clear();
                    SegmentVolume v = null;
                    Exception buildError = null;
                    try { v = r.Build(c); } catch (Exception ex) { buildError = ex; }
                    bool mixed = touched.Any(x => x.IsHighResolution) && touched.Any(x => !x.IsHighResolution);
                    if (mixed)
                    {
                        hiRes = true; volCache.Clear(); buildError = null;
                        try { v = r.Build(c); } catch (Exception ex) { buildError = ex; }
                    }
                    if (buildError != null) { BuildFailed(r.Id, buildError); continue; }
                    if (v == null) { Log.Warn("{0}: no source structure (see tab 1) - not created.", r.Id); continue; }
                    var sources = touched.Distinct().ToList();
                    Write(r, r.Id, v, S.OverwriteDraftStructures, sources);
                }
            }
            finally { stepA = false; StructureTools.RemoveTemporaries(S.SS, StructureRecipes.TempPrefix, Log); }
        }

        // ------------------------------------------------------------------ step C
        public bool RunStepC()
        {
            Log.Step = "Field targets";
            try { return StepC(); }
            finally { StructureTools.RemoveTemporaries(S.SS, StructureRecipes.TempPrefix, Log); }
        }

        private bool StepC()
        {
            Geometry = S.Geometry;
            if (Geometry == null || !Geometry.IsFeasible) { Log.Error("Geometry split is not feasible - fix the Geometry tab first."); return false; }
            var problems = RecipeBook.Validate();
            if (problems.Count > 0) { foreach (var p in problems) Log.Error("StructureRecipes.cs: " + p); return false; }

            // Inputs. Boolean operations need matching resolution: if any input is high resolution, all inputs are
            // used as high-resolution temporary copies (the originals are never modified).
            Structure ctv = S.UseDraftUnion ? null : S.TargetCtv, body = S.Body, vb = S.InvolvedVb;
            if (!S.UseDraftUnion && ctv == null) { Log.Error("No physician CTV selected."); return false; }
            hiRes = new[] { ctv, body, vb, S.BrainSource(), S.CanalSource() }.Any(x => x != null && x.IsHighResolution);
            if (hiRes) Log.Info("High-resolution input detected - using high-resolution working copies.");
            BodyVolume = Vol(body);
            VbVolume = Vol(vb);
            var c = new RecipeContext(this);

            if (S.UseDraftUnion)
            {
                var du = RecipeBook.ByRole(Role.DraftUnion);
                if (du == null) { Log.Error("No physician CTV and no Role.DraftUnion recipe."); return false; }
                SegmentVolume v;
                try { v = du.Build(c); } catch (Exception ex) { BuildFailed(du.Id, ex); return false; }
                if (v == null) { Log.Error("Draft CTV needs both brain and thecal sac structures."); return false; }
                var st = Write(du, du.Id, v, true);
                if (st == null) return false;
                CtvVolume = Vol(st);
                Log.Warn("NON-CLINICAL: field targets were built from {0} (draft union), not from a physician-approved CTV.", du.Id);
                Log.Manual("Rebuild field targets from the physician CTV_#### before any clinical use.");
            }
            else CtvVolume = Vol(ctv);

            var all = Geometry.All().ToList();
            var lastSpine = Geometry.Spine.LastOrDefault();
            Fields = all.Select(seg => new FieldContext(this, seg, seg.Name == "BR" || seg.Name == "POST",
                                                        seg == lastSpine || (seg.Name == "POST" && Geometry.Spine.Count == 0))).ToList();

            foreach (var r in RecipeBook.All.Where(x => x.Step == Step.C && x.Role != Role.DraftUnion))
            {
                if (r.PerField)
                {
                    foreach (var f in Fields)
                    {
                        string id = r.IdFor(f.Code);
                        if (r.AppliesTo != null && !r.AppliesTo(f)) { if (r.Saved) StructureTools.RemoveIfPresent(S.SS, id, Log, "recipe does not apply to " + f.Code); continue; }
                        SegmentVolume v;
                        try { v = r.BuildField(f); } catch (Exception ex) { BuildFailed(id, ex); return false; }
                        if (v == null) { if (r.Saved) StructureTools.RemoveIfPresent(S.SS, id, Log, "recipe returned nothing"); continue; }
                        if (!r.Saved) { Built[id] = v; continue; }
                        if (Write(r, id, v, true) == null) return false;
                    }
                    // the same structure of fields that are not in this split (e.g. MID from an earlier run)
                    if (r.Saved)
                        foreach (var code in RecipeBook.AllFieldCodes.Where(code => !Fields.Any(f => f.Code == code)))
                            StructureTools.RemoveIfPresent(S.SS, r.IdFor(code), Log, "not in the current split");
                }
                else
                {
                    if (r.When != null && !r.When(c)) { if (r.Saved) StructureTools.RemoveIfPresent(S.SS, r.Id, Log, "recipe condition not met"); continue; }
                    SegmentVolume v;
                    try { v = r.Build(c); } catch (Exception ex) { BuildFailed(r.Id, ex); return false; }
                    if (v == null) { if (r.Saved) StructureTools.RemoveIfPresent(S.SS, r.Id, Log, "recipe returned nothing"); continue; }
                    if (!r.Saved) { Built[r.Id] = v; continue; }
                    if (Write(r, r.Id, v, true) == null) return false;
                }
            }

            CheckOverlaps();
            Log.Manual("Review and approve the (D) field-target and OPT structures (margins: brain {0}, spine {1} mm R/L/A/P/I/S; StructureRecipes.cs).",
                       string.Join("/", StructureRecipes.BrainMargins.All()), string.Join("/", StructureRecipes.SpineMargins.All()));
            return true;
        }

        private void BuildFailed(string id, Exception ex)
        {
            Log.Error("StructureRecipes.cs: building {0} failed: {1}{2}", id, ex.Message,
                      ex is NullReferenceException ? " - a volume the recipe uses is missing (Get() of a structure that is built later in the list " +
                                                     "or not at all, or no involved-VB / BODY structure); return null for 'not needed'." : "");
        }

        /// <summary>Procedure: neighbouring field targets overlap by >= the minimum (measured on the structures actually created).</summary>
        private void CheckOverlaps()
        {
            var chain = Geometry.Chain();
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                Structure a, b;
                if (!Written.TryGetValue(RecipeBook.Id(Role.FieldTarget, chain[i].Name) ?? "", out a) ||
                    !Written.TryGetValue(RecipeBook.Id(Role.FieldTarget, chain[i + 1].Name) ?? "", out b)) continue;
                var upper = StructureTools.SliceExtents(a, S.Image);
                var lower = StructureTools.SliceExtents(b, S.Image);
                if (upper.Count == 0 || lower.Count == 0) continue;
                double ov = lower.Max(e => e.Z) - upper.Min(e => e.Z) + S.Image.ZRes;   // plane-centred extents: add one slice
                if (ov < S.OverlapMm - 0.01) Log.Error("Overlap {0}/{1} is {2:F1} cm (< {3:F1} cm required).", a.Id, b.Id, ov / 10, S.OverlapMm / 10);
                else Log.Info("Overlap {0}/{1}: {2:F1} cm.", a.Id, b.Id, ov / 10);
            }
        }

        // ------------------------------------------------------------------ helpers
        internal Structure Write(StructureRecipe r, string id, SegmentVolume v, bool overwrite, List<Structure> sources = null)
        {
            var dst = StructureTools.GetOrCreate(S.SS, id, r.DicomType, overwrite, Log);
            if (dst == null) return null;
            if (sources != null && !hiRes) StructureTools.MatchResolution(dst, sources.ToArray());   // step A: like its source
            else if (hiRes)
            {
                if (hiResRef == null) hiResRef = Written.Values.FirstOrDefault(x => x.IsHighResolution);
                if (hiResRef != null) StructureTools.MatchResolution(dst, hiResRef);
            }
            dst.SegmentVolume = v;
            Written[id] = dst;
            Built[id] = v;
            if (r.Role == Role.FieldTarget)
            {
                var ext = StructureTools.SliceExtents(dst, S.Image);
                if (ext.Count == 0) Log.Error("{0} is empty.", id);
                else
                {
                    double hi = ext.Max(e => e.Z), lo = ext.Min(e => e.Z);
                    Log.Info("{0}: {1:F1} cc, Y {2:+0.0;-0.0} to {3:+0.0;-0.0} cm (user), length {4:F1} cm", id, dst.Volume, S.ToUserYcm(hi), S.ToUserYcm(lo), (hi - lo) / 10);
                }
            }
            else
            {
                var real = sources == null ? null : sources.Where(x => !x.Id.StartsWith(StructureRecipes.TempPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                if (real != null && real.Count > 0) Log.Info("{0} <- {1} ({2:F1} cc)", id, string.Join(", ", real.Select(x => x.Id)), dst.Volume);
                else Log.Info("{0}: {1:F1} cc", id, dst.Volume);
                if (dst.IsEmpty) Log.Warn("{0} is empty.", id);
            }
            return dst;
        }

        private readonly Dictionary<Structure, SegmentVolume> volCache = new Dictionary<Structure, SegmentVolume>();

        /// <summary>Volume of a structure, as a high-resolution working copy when the run works in high resolution.</summary>
        internal SegmentVolume Vol(Structure s)
        {
            if (s == null) return null;
            if (!touched.Contains(s)) touched.Add(s);
            SegmentVolume cached;
            if (volCache.TryGetValue(s, out cached)) return cached;
            SegmentVolume v;
            if (s.IsHighResolution)
            {
                if (!hiRes && !stepA) Log.Warn("{0} is high resolution but the main inputs are not - combine it with care (convert it or the others).", s.Id);
                else if (hiResRef == null) hiResRef = s;
                v = s.SegmentVolume;
            }
            else if (!hiRes) v = s.SegmentVolume;
            else
            {
                var t = StructureTools.GetOrCreate(S.SS, NextTmp(), "CONTROL", true, Log);
                t.SegmentVolume = s.SegmentVolume;
                if (t.CanConvertToHighResolution()) t.ConvertToHighResolution();
                if (hiResRef == null && t.IsHighResolution) hiResRef = t;
                v = t.SegmentVolume;
            }
            volCache[s] = v;
            return v;
        }

        internal SegmentVolume Slab(double zLo, double zHi)
        {
            var s = StructureTools.CreateSlab(S.SS, NextTmp(), zLo, zHi, Log);
            if (hiRes && hiResRef != null) StructureTools.MatchResolution(s, hiResRef);
            return s.SegmentVolume;
        }

        private string NextTmp() { return string.Format("{0}{1:00}", StructureRecipes.TempPrefix, ++tmpCounter); }
    }
}
