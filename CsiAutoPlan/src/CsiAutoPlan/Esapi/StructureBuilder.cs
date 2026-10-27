using System;
using System.Collections.Generic;
using System.Linq;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    /// <summary>Structure steps of the procedure. What is built, and how, is defined in StructureRecipes.cs;
    /// this class runs those recipes (steps A and C) and applies the HU overrides (step B).</summary>
    public class StructureBuilder
    {
        private readonly CsiSession S;
        private RunLog Log { get { return S.Log; } }
        private int tmpCounter;

        public StructureBuilder(CsiSession session) { S = session; }

        // ------------------------------------------------------------------ step A
        /// <summary>Step A recipes of StructureRecipes.cs ((D)CTV_BRAIN / (D)CTV_THECALSAC from the AI contours).</summary>
        public void CreateDraftCtvs()
        {
            new RecipeEngine(S).RunStepA();
            string brain = RecipeBook.Id(Role.DraftBrain) ?? "(D) brain", canal = RecipeBook.Id(Role.DraftThecalSac) ?? "(D) thecal sac";
            Log.Manual("Edit {0}: extend to cover base of skull and orbits up to the back of the eye; approve.", brain);
            Log.Manual("Edit {0}: include nerve roots (contour on root slices, interpolate, smooth) from upper C-spine to sacrum; approve.", canal);
            Log.Manual("Physician: create and verify CTV_#### = {0} OR {1} (check orbits, cribriform plate, sacral inferior border).", brain, canal);
            if (S.InvolvedVb == null)
                Log.Manual("If vertebral bodies are involved: contour (D)VBs_INVOLVED (base of skull to CTV end in sacrum), physician copies to VBs_INVOLVED.");
        }

        // ------------------------------------------------------------------ HU overrides
        public void ApplyHuOverrides()
        {
            Log.Step = "HU overrides";
            foreach (var row in S.HuRows)
            {
                var r = row.Rule;
                if (r.Mode == HuMode.ReportOnly || !row.Apply)
                {
                    Log.Manual("HU override for {0}: {1} (current: {2}).", row.Structure.Id, string.IsNullOrEmpty(r.Note) ? "check" : r.Note, row.CurrentHu);
                    continue;
                }
                string err;
                if (!row.Structure.CanSetAssignedHU(out err)) { Log.Error("Cannot assign HU to {0}: {1}", row.Structure.Id, err); continue; }

                double hu = r.Hu;
                if (r.Mode == HuMode.NeighborMean)
                {
                    double? mean = NeighbourMeanHu(row.Structure, r);
                    if (!mean.HasValue) { Log.Manual("Could not sample tissue around {0}; assign HU manually ({1}).", row.Structure.Id, r.Note); continue; }
                    hu = Math.Round(mean.Value);
                }
                row.Structure.SetAssignedHU(hu);
                Log.Info("{0}: assigned {1:F0} HU ({2}).", row.Structure.Id, hu, r.Mode == HuMode.NeighborMean ? "mean of surrounding tissue" : "fixed");
                if (r.Mode == HuMode.NeighborMean) Log.Manual("Verify the {0:F0} HU assigned to {1} against neighbouring tissue.", hu, row.Structure.Id);
            }
        }

        /// <summary>Mean CT number in a shell around the structure, restricted to brain/body and an HU window.</summary>
        private double? NeighbourMeanHu(Structure s, HuOverride r)
        {
            Structure shell = null;
            try
            {
                var restrict = r.RestrictTo == "@BRAIN" ? S.BrainSource() : S.Body;
                shell = StructureTools.GetOrCreate(S.SS, NextTmp(), "CONTROL", true, Log);
                StructureTools.MatchResolution(shell, s, restrict);
                var sv = s.SegmentVolume.Margin(r.NeighborOuterMm).Sub(s.SegmentVolume.Margin(r.NeighborInnerMm));
                if (restrict != null) sv = sv.And(restrict.SegmentVolume);
                shell.SegmentVolume = sv;
                if (shell.IsEmpty) return null;

                // Row-by-row profiles (Varian API book, sec. 2.3.2): GetSegmentProfile tells which samples are
                // inside the shell, GetImageProfile returns the CT numbers at the same sample positions.
                var img = S.Image;
                double dx = img.XRes * img.XDirection.x;
                double dy = img.YRes * img.YDirection.y;
                double sum = 0; int n = 0;
                bool unitChecked = false;
                foreach (var e in StructureTools.SliceExtents(shell, img))
                {
                    int i0 = VoxelIndex(e.XMin, img.Origin.x, dx, img.XSize), i1 = VoxelIndex(e.XMax, img.Origin.x, dx, img.XSize);
                    int j0 = VoxelIndex(e.YMin, img.Origin.y, dy, img.YSize), j1 = VoxelIndex(e.YMax, img.Origin.y, dy, img.YSize);
                    if (i1 < i0) { var t = i0; i0 = i1; i1 = t; }
                    if (j1 < j0) { var t = j0; j0 = j1; j1 = t; }
                    int samples = i1 - i0 + 1;
                    if (samples < 2) continue;
                    for (int j = j0; j <= j1; j++)
                    {
                        double y = img.Origin.y + j * dy;
                        var start = new VVector(img.Origin.x + i0 * dx, y, e.Z);
                        var stop = new VVector(img.Origin.x + i1 * dx, y, e.Z);
                        var inside = shell.GetSegmentProfile(start, stop, new System.Collections.BitArray(samples));
                        var hu = img.GetImageProfile(start, stop, new double[samples]);
                        if (!unitChecked)
                        {
                            unitChecked = true;
                            if (!string.IsNullOrEmpty(hu.Unit) && hu.Unit.IndexOf("HU", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Log.Warn("Image values are in '{0}', not HU - neighbour HU not computed.", hu.Unit);
                                return null;
                            }
                        }
                        int m = Math.Min(inside.Count, hu.Count);
                        for (int k = 0; k < m; k++)
                        {
                            if (!inside[k].Value) continue;
                            double v = hu[k].Value;
                            if (double.IsNaN(v) || v < r.NeighborLoHu || v > r.NeighborHiHu) continue;
                            sum += v; n++;
                        }
                    }
                }
                Log.Info("{0}: sampled {1} voxels around structure.", s.Id, n);
                return n >= 20 ? sum / n : (double?)null;
            }
            finally { StructureTools.SafeRemove(S.SS, shell, Log); }
        }

        private static int VoxelIndex(double pos, double origin, double step, int size)
        {
            int i = (int)Math.Round((pos - origin) / step);
            return Math.Max(0, Math.Min(size - 1, i));
        }

        // ------------------------------------------------------------------ step C
        /// <summary>Step C recipes of StructureRecipes.cs: field targets (COMPO), OPT structures, (D)INV_VB_OPT, ...</summary>
        public bool CreateFieldTargets() { return new RecipeEngine(S).RunStepC(); }

        private string NextTmp() { return string.Format("{0}{1:00}", StructureRecipes.TempPrefix, ++tmpCounter); }
    }
}
