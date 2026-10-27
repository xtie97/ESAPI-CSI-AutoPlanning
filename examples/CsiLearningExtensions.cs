// Teaching examples for CsiAutoPlan v1.7 / Eclipse 18.
// These methods read patient data and write to RunLog; they do not modify the patient.
// This file is outside the supplied project and is NOT included in its build.
// Compile against your installed ESAPI 18 DLLs before using it in a test system.
using System;
using System.Linq;
using CsiAutoPlan.Esapi;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Learning
{
    public static class CsiLearningExtensions
    {
        // Call after a plan is selected and before objectives or optimization.
        // A plan ID such as INITIAL can occur in several courses.
        public static bool CheckPlanMatchesSession(CsiSession session, IonPlanSetup plan)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.Log.Step = "Plan identity";
            if (plan == null || session.SS == null)
            {
                session.Log.Error("A selected ion plan and structure set are required.");
                return false;
            }

            bool matches = true;
            if (plan.Course == null || !string.Equals(plan.Course.Id, session.CourseId,
                                                       StringComparison.OrdinalIgnoreCase))
            {
                session.Log.Error("Selected plan course does not match requested course '{0}'.", session.CourseId);
                matches = false;
            }
            if (!string.Equals(plan.Id, session.PlanId, StringComparison.OrdinalIgnoreCase))
            {
                session.Log.Error("Selected plan ID does not match requested plan '{0}'.", session.PlanId);
                matches = false;
            }
            if (plan.StructureSet == null ||
                !string.Equals(plan.StructureSet.UID, session.SS.UID, StringComparison.Ordinal))
            {
                session.Log.Error("Selected plan uses a different structure set from this session.");
                matches = false;
            }
            if (matches)
                session.Log.Info("Selected plan matches course, plan ID and structure set UID.");
            return matches;
        }

        // Good first exercise: collections, sorting, properties and formatted logging.
        public static void ReportStructureInventory(CsiSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.Log.Step = "Structure inventory";
            if (session.SS == null)
            {
                session.Log.Warn("No structure set selected.");
                return;
            }
            foreach (var structure in session.SS.Structures.OrderBy(x => x.Id))
            {
                session.Log.Info("{0}: type {1}; empty {2}; volume {3:F2} cc; high resolution {4}.",
                    structure.Id, structure.DicomType, structure.IsEmpty,
                    structure.Volume, structure.IsHighResolution);
            }
        }

        // Nominal plan DVH reporting only. No clinical thresholds or pass/fail decision.
        public static void ReportSelectedCtvDose(CsiSession session, IonPlanSetup plan)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!CheckPlanMatchesSession(session, plan)) return;
            session.Log.Step = "CTV dose lesson";
            if (plan.Dose == null || !plan.IsDoseValid)
            {
                session.Log.Warn("A valid calculated dose is required for this DVH report.");
                return;
            }
            if (session.UseDraftUnion || session.TargetCtv == null || session.TargetCtv.IsEmpty)
            {
                session.Log.Warn("Select a nonempty clinical CTV for this report.");
                return;
            }

            var target = session.TargetCtv;
            var d98 = plan.GetDoseAtVolume(target, 98.0,
                VolumePresentation.Relative, DoseValuePresentation.Absolute);
            var d2 = plan.GetDoseAtVolume(target, 2.0,
                VolumePresentation.Relative, DoseValuePresentation.Absolute);
            session.Log.Info("{0}: nominal D98 {1:F2} {2}; D2 {3:F2} {4}.",
                target.Id, d98.Dose, d98.Unit, d2.Dose, d2.Unit);
        }
    }
}
