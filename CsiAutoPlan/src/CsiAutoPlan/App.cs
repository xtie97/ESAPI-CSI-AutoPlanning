using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using CsiAutoPlan.Esapi;
using CsiAutoPlan.UI;
using VMS.TPS.Common.Model.API;

namespace CsiAutoPlan
{
    /// <summary>Plugin orchestration: settings -> analysis -> review dialog -> selected steps -> report.</summary>
    public static class App
    {
        public static void Run(ScriptContext context)
        {
            if (context.Patient == null) { MessageBox.Show("Open a patient first.", ScriptName()); return; }

            var log = new RunLog();
            string settingsInfo;
            var cfg = LoadSettings(log, out settingsInfo);

            log.Step = "Start";
            log.Info("{0} (assembly version {1}); Eclipse '{2}' {3}; user {4}.", ScriptName(), PluginVersion(), context.ApplicationName, context.VersionInfo,
                     context.CurrentUser != null ? context.CurrentUser.Id : "-");
            var session = new CsiSession(context, cfg, log);
            session.Caps = ApiCapabilities.Probe(cfg.Optimization.AllowReflectionForRobustFlag);
            if (session.Caps.ProbeFailed) log.Warn("API probe unavailable ({0}); optional features fall back to the checklist.", session.Caps.ProbeError);
            log.Info(session.Caps.RobustSummary());
            var recipeProblems = RecipeBook.Validate();
            if (recipeProblems.Count > 0)
            {
                // StructureRecipes.cs is compiled into the plugin: a mistake there is fixed in the code, not per patient.
                MessageBox.Show("StructureRecipes.cs has errors - fix them, rebuild and approve the new version:" + Environment.NewLine + Environment.NewLine +
                                string.Join(Environment.NewLine, recipeProblems.Select(p => "- " + p)), ScriptName(), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            session.Analyze();
            if (session.SS == null || session.Image == null)
            {
                MessageBox.Show(string.Join(Environment.NewLine, session.BlockingProblems), ScriptName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Read-only: the IDs this patient's existing proton plans use (fallback if Eclipse rejects a SiteSettings.cs ID).
            try
            {
                log.Step = "Settings";
                var h = ExistingPlanReader.Read(context, cfg, log);
                settingsInfo += Environment.NewLine + Environment.NewLine + h.Report;
                session.UsePlanSettings(h);
            }
            catch (Exception ex) { log.Warn("Could not read settings from existing plans: " + ex.Message); }

            // Analysis and the dialog are read-only; modifications are enabled only once the user confirms
            // (same order as Varian's CreateOptStructures sample), so Cancel leaves the patient untouched.
            using (var dlg = new MainForm(session, settingsInfo))
                if (dlg.ShowDialog() != DialogResult.OK) return;
            try { context.Patient.BeginModifications(); }
            catch (Exception ex)
            {
                MessageBox.Show("Eclipse did not allow modifications for this patient (script not approved on this system, " +
                                "patient opened read-only, or locked by another user):\n\n" + ex.Message,
                                ScriptName(), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var report = new ReportForm(log))
            {
                // No Application.DoEvents(): pumping messages would let the user interact with Eclipse while
                // the script modifies the patient (ESAPI is single-threaded; API book sec. 2.1.2). The log
                // window repaints itself synchronously instead.
                report.Show();
                report.Refresh();
                Execute(session);
                var path = log.SaveTo(CsiSettings.ExpandPath(cfg.LogDirectory), context.Patient.Id, ScriptName());
                report.Hide();
                report.ShowFinal(log, path);
                report.ShowDialog();
            }
        }

        public static void Execute(CsiSession s)
        {
            var log = s.Log;
            var structures = new StructureBuilder(s);
            var plans = new PlanBuilder(s);
            var opt = new OptimizationBuilder(s);
            log.Step = "Start";
            log.Info("Machine {0}; brain iso from {1}; Rx {2} x {3:F2} Gy(RBE); target {4}.", s.Machine, s.UseUserOrigin ? "user origin" : "computed position",
                     s.Fractions, s.DosePerFractionGy, s.UseDraftUnion ? "DRAFT union" : (s.TargetCtv != null ? s.TargetCtv.Id : "-"));
            if (s.Geometry != null)
                log.Info("Field arrangement: {0}; {1} isocenter(s), {2} fields; field-target overlap {3:F1} cm. {4}",
                         CsiSession.SchemeDescription(s.Scheme), s.Geometry.IsocenterCount, s.FieldCount, s.Geometry.OverlapUsedMm / 10, s.SchemeComparison);

            if (s.DoDraftStructures) Guard(log, "D-structures", structures.CreateDraftCtvs);
            if (s.DoHuOverrides) Guard(log, "HU overrides", structures.ApplyHuOverrides);

            bool targetsOk = true;
            if (s.DoFieldTargets) targetsOk = Guard(log, "Field targets", () => structures.CreateFieldTargets());

            IonPlanSetup plan = null;
            if (s.DoPlan)
            {
                if (!targetsOk) { log.Error("Plan not created because field-target creation failed."); return; }
                if (!s.DoFieldTargets)
                {
                    log.Step = "Plan setup";
                    var tp = s.FieldTargetProblems();
                    if (tp.Count > 0)
                    {
                        foreach (var p in tp) log.Error("Field target: " + p + ".");
                        log.Error("Plan not created: run step C (Field targets) for the current split first.");
                        return;
                    }
                }
                Guard(log, "Plan setup", () => { plan = plans.CreatePlan(); return plan != null; });
                if (plan != null && !Guard(log, "Fields", () => plans.CreateFields(plan)))
                {
                    log.Error("Stopping: fields were not created, so objectives / uncertainties / optimization were skipped.");
                    return;
                }
                if (plan != null) Guard(log, "Range margins", () => new RangeMarginBuilder(s).Apply(plan));
            }
            else if (s.DoObjectives || s.DoOptimize)
            {
                plan = FindExistingPlan(s);
                if (plan == null) { log.Error("No ion plan '{0}' in course '{1}' (or open in Eclipse) to work on.", s.PlanId, s.CourseId); return; }
                log.Info("Working on existing plan {0}/{1}.", plan.Course.Id, plan.Id);
            }
            if (plan == null) return;

            if (s.DoObjectives)
            {
                // robust scenarios first, so the objectives' RO flag refers to existing uncertainty scenarios
                Guard(log, "Perturbations", () => opt.SetupUncertainties(plan));
                Guard(log, "Objectives", () => opt.SetupObjectives(plan));
            }
            if (s.DoOptimize) Guard(log, "Optimize", () => opt.OptimizeAndCalculate(plan));

            log.Step = "Done";
            log.Manual("Physics Pre-MD Plan Check, then physician review.");
        }

        /// <summary>Script name as Eclipse lists it, without ".esapi.dll" (e.g. "AutoPlanv1.1"; set by ScriptVersion in the csproj).</summary>
        public static string ScriptName()
        {
            try
            {
                var n = Assembly.GetExecutingAssembly().GetName().Name;
                return n.EndsWith(".esapi", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 6) : n;
            }
            catch (Exception) { return "AutoPlan"; }
        }

        /// <summary>Assembly file version - Script Approval keys on it, so it is logged with every run.</summary>
        public static string PluginVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var fv = asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).OfType<AssemblyFileVersionAttribute>().FirstOrDefault();
                return fv != null ? fv.Version : asm.GetName().Version.ToString();
            }
            catch (Exception) { return "?"; }
        }

        private static IonPlanSetup FindExistingPlan(CsiSession s)
        {
            var open = s.Context.IonPlanSetup;
            if (open != null && (string.IsNullOrEmpty(s.PlanId) || string.Equals(open.Id, s.PlanId, StringComparison.OrdinalIgnoreCase))) return open;
            var course = s.Patient.Courses.FirstOrDefault(c => string.Equals(c.Id, s.CourseId, StringComparison.OrdinalIgnoreCase));
            return course == null ? null : course.IonPlanSetups.FirstOrDefault(p => string.Equals(p.Id, s.PlanId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Guard(RunLog log, string step, Action a) { return Guard(log, step, () => { a(); return true; }); }

        private static bool Guard(RunLog log, string step, Func<bool> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                log.Step = step;
                log.Error("Exception: " + ex.Message);
                log.Info(ex.ToString());
                return false;
            }
        }

        // ------------------------------------------------------------------ settings
        /// <summary>Site settings compiled into the plugin (SiteSettings.cs); problems are logged and shown in tab 5.</summary>
        private static CsiSettings LoadSettings(RunLog log, out string info)
        {
            log.Step = "Settings";
            var cfg = SiteSettings.Create();
            var problems = cfg.Validate();
            foreach (var p in problems) log.Warn("SiteSettings.cs: " + p);
            var incomplete = cfg.IncompleteMachines();
            if (incomplete.Count > 0)
                log.Info("Rooms without all IDs in SiteSettings.cs (fine unless selected): " +
                         string.Join("; ", incomplete.Select(kv => kv.Key + " - " + string.Join(", ", kv.Value))) + ".");
            var r = cfg.RangeMargins;
            info = "Settings: SiteSettings.cs and StructureRecipes.cs, compiled into " + ScriptName() + " (version " + PluginVersion() + ")." + Environment.NewLine +
                   "Rooms: " + string.Join(", ", cfg.Machines.Select(m => m.DisplayName + (m.Enabled ? "" : " (hidden)"))) + Environment.NewLine +
                   (r.Enabled
                       ? string.Format("Range margins: distal = R x {0}% + {1} mm, proximal = (R - SOBP) x {0}% + {1} mm (R, SOBP from Eclipse), rounded up to {2} mm.",
                                       r.RangeUncertaintyPercent, r.AddedMarginMm, r.RoundUpToMm)
                       : "Range margins: off - fixed proximal / distal margins.") +
                   (problems.Count + incomplete.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine,
                       problems.Select(p => "  ! " + p).Concat(incomplete.Select(kv => "  - " + kv.Key + " not set up: " + string.Join(", ", kv.Value)))));
            return cfg;
        }
    }
}
