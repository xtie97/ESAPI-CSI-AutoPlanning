using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace CsiAutoPlan.Esapi
{
    /// <summary>
    /// Finds, at run time, how the installed ESAPI exposes the NUPO "RO" (robust) checkbox of an objective, and the
    /// API's uncertainty methods. The ESAPI 16.1 assemblies the plugin is compiled against have no robust-objective
    /// member, and Varian's 18.0 proton sample does not use one, so the member is looked up in the ESAPI that Eclipse
    /// actually loads. Recognised forms (first match wins):
    ///   1. OptimizationSetup.AddPointObjective / AddMeanDoseObjective overload = the usual parameters + one bool
    ///      parameter whose name contains "robust"  -> the robust objective is created in one call
    ///   2. settable bool property on the objective whose name contains "robust"            (obj.IsRobust = true)
    ///   3. method on the objective with one bool parameter, name contains "robust"         (obj.SetRobust(true))
    ///   4. OptimizationSetup method (OptimizationObjective, bool), name contains "robust"
    /// A readable bool "robust" property, if present, is used to verify the result. Every member of the ESAPI assemblies
    /// whose name or parameter names contain "robust" is listed in the log and in tab 5. Never throws: if reflection is
    /// blocked or a call fails, the run falls back to the manual checklist line.
    /// </summary>
    public class ApiCapabilities
    {
        public string EsapiVersion = "";
        public MethodInfo PointRobustOverload, MeanRobustOverload;   // form 1
        public int PointRobustIndex = -1, MeanRobustIndex = -1;      // position of the bool parameter
        public PropertyInfo ObjectiveRobustProperty;                 // form 2
        public MethodInfo ObjectiveRobustSetter;                     // form 3
        public MethodInfo SetupRobustMethod;                         // form 4
        public PropertyInfo ObjectiveRobustGetter;                   // read-back
        public List<string> RobustCandidates = new List<string>();
        public List<string> FieldUncertaintyCandidates = new List<string>();

        public bool ProbeFailed;
        public string ProbeError = "";
        /// <summary>SiteSettings.cs: Optimization.AllowReflectionForRobustFlag. Set false if your approval
        /// process does not allow a script to call API members through .NET reflection (Varian API book, sec. 2.1.2).</summary>
        public bool AllowInvoke;
        /// <summary>Set once a robust call failed - later objectives go straight to the checklist.</summary>
        public string RobustFailure;

        public bool RobustMemberFound
        {
            get { return PointRobustOverload != null || MeanRobustOverload != null || ObjectiveRobustProperty != null || ObjectiveRobustSetter != null || SetupRobustMethod != null; }
        }
        public bool CanSetRobust { get { return AllowInvoke && RobustFailure == null && RobustMemberFound; } }
        public bool CanAddFieldUncertainty { get { return false; } }   // never invoked blindly - see report

        private static bool IsRobustName(string s) { return s != null && s.IndexOf("robust", StringComparison.OrdinalIgnoreCase) >= 0; }

        public static ApiCapabilities Probe(bool allowInvoke)
        {
            var c = new ApiCapabilities { AllowInvoke = allowInvoke };
            try
            {
                c.EsapiVersion = typeof(PlanSetup).Assembly.GetName().Version.ToString();
                const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;

                // ---- full listing of "robust" members in the two ESAPI assemblies (information for the log / tab 5)
                foreach (var asm in new[] { typeof(PlanSetup).Assembly, typeof(VVector).Assembly }.Distinct())
                    foreach (var t in SafeTypes(asm).Where(x => x.IsPublic && x.Namespace != null && x.Namespace.StartsWith("VMS.TPS", StringComparison.Ordinal)))
                    {
                        if (t.IsEnum)
                        {
                            foreach (var n in Enum.GetNames(t).Where(IsRobustName).Where(n => n != "RobustOptimizationUncertainty"))
                                c.RobustCandidates.Add(t.Name + "." + n + " (enum value)");
                            continue;
                        }
                        foreach (var p in t.GetProperties(pub | BindingFlags.DeclaredOnly).Where(p => IsRobustName(p.Name)))
                            c.RobustCandidates.Add(t.Name + "." + p.Name + " : " + p.PropertyType.Name + (p.CanWrite && p.GetSetMethod() != null ? " {get;set;}" : " {get;}"));
                        foreach (var m in t.GetMethods(pub | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName
                                     && (IsRobustName(m.Name) || m.GetParameters().Any(p => IsRobustName(p.Name)))))
                            c.RobustCandidates.Add(t.Name + "." + Sig(m));
                    }

                // ---- form 1: Add...Objective overloads with an extra robust bool
                var os = typeof(OptimizationSetup);
                c.PointRobustOverload = FindOverload(os, "AddPointObjective",
                    new[] { typeof(Structure), typeof(OptimizationObjectiveOperator), typeof(DoseValue), typeof(double), typeof(double) }, out c.PointRobustIndex);
                c.MeanRobustOverload = FindOverload(os, "AddMeanDoseObjective",
                    new[] { typeof(Structure), typeof(DoseValue), typeof(double) }, out c.MeanRobustIndex);

                // ---- forms 2-3 (+ read-back) on the objective types
                var objTypes = new[] { typeof(OptimizationObjective), typeof(OptimizationPointObjective), typeof(OptimizationMeanDoseObjective) };
                foreach (var t in objTypes)
                {
                    foreach (var p in t.GetProperties(pub).Where(p => IsRobustName(p.Name) && p.PropertyType == typeof(bool)))
                    {
                        if (c.ObjectiveRobustGetter == null && p.CanRead) c.ObjectiveRobustGetter = p;
                        if (c.ObjectiveRobustProperty == null && p.CanWrite && p.GetSetMethod() != null) c.ObjectiveRobustProperty = p;
                    }
                    if (c.ObjectiveRobustSetter == null)
                        c.ObjectiveRobustSetter = t.GetMethods(pub).FirstOrDefault(m => !m.IsSpecialName && IsRobustName(m.Name)
                            && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool));
                }

                // ---- form 4 on OptimizationSetup
                c.SetupRobustMethod = os.GetMethods(pub).FirstOrDefault(m => IsRobustName(m.Name) && m.GetParameters().Length == 2
                    && typeof(OptimizationObjective).IsAssignableFrom(m.GetParameters()[0].ParameterType) && m.GetParameters()[1].ParameterType == typeof(bool));

                foreach (var t in new[] { typeof(PlanSetup), typeof(IonPlanSetup), typeof(IonBeam), typeof(PlanUncertainty) })
                    foreach (var m in t.GetMethods(pub | BindingFlags.DeclaredOnly)
                                       .Where(m => m.Name.IndexOf("Uncertaint", StringComparison.OrdinalIgnoreCase) >= 0 && !m.IsSpecialName))
                        c.FieldUncertaintyCandidates.Add(t.Name + "." + Sig(m));
            }
            catch (Exception ex)
            {
                c.ProbeFailed = true;
                c.ProbeError = ex.GetType().Name + ": " + ex.Message;
                c.PointRobustOverload = c.MeanRobustOverload = c.ObjectiveRobustSetter = c.SetupRobustMethod = null;
                c.ObjectiveRobustProperty = c.ObjectiveRobustGetter = null;
            }
            return c;
        }

        /// <summary>Overload of <paramref name="name"/> = baseParams + exactly one bool parameter named *robust*.</summary>
        private static MethodInfo FindOverload(Type t, string name, Type[] baseParams, out int robustIndex)
        {
            robustIndex = -1;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == name))
            {
                var ps = m.GetParameters();
                if (ps.Length != baseParams.Length + 1) continue;
                int idx = Array.FindIndex(ps, p => p.ParameterType == typeof(bool) && IsRobustName(p.Name));
                if (idx < 0) continue;
                var rest = ps.Where((p, i) => i != idx).Select(p => p.ParameterType).ToArray();
                if (rest.SequenceEqual(baseParams)) { robustIndex = idx; return m; }
            }
            return null;
        }

        /// <summary>Creates a robust point / mean objective through an overload (form 1). Null if not available or it failed.</summary>
        public OptimizationObjective TryAddRobust(OptimizationSetup os, bool mean, object[] baseArgs, out string how)
        {
            how = null;
            var m = mean ? MeanRobustOverload : PointRobustOverload;
            int idx = mean ? MeanRobustIndex : PointRobustIndex;
            if (!CanSetRobust || m == null) return null;
            try
            {
                var args = baseArgs.ToList();
                args.Insert(idx, true);
                var obj = (OptimizationObjective)m.Invoke(os, args.ToArray());
                how = "OptimizationSetup." + m.Name + "(..., " + m.GetParameters()[idx].Name + ": true)";
                return obj;
            }
            catch (Exception ex) { Fail(ex); return null; }
        }

        /// <summary>Ticks RO on an existing objective (forms 2-4) and verifies it if the API can read it back.</summary>
        public bool TrySetRobust(OptimizationSetup setup, OptimizationObjective obj, out string how)
        {
            how = null;
            if (!CanSetRobust || obj == null) return false;
            try
            {
                if (ObjectiveRobustProperty != null && ObjectiveRobustProperty.DeclaringType.IsInstanceOfType(obj))
                { ObjectiveRobustProperty.SetValue(obj, true, null); how = ObjectiveRobustProperty.Name + " = true"; }
                else if (ObjectiveRobustSetter != null && ObjectiveRobustSetter.DeclaringType.IsInstanceOfType(obj))
                { ObjectiveRobustSetter.Invoke(obj, new object[] { true }); how = ObjectiveRobustSetter.Name + "(true)"; }
                else if (SetupRobustMethod != null)
                { SetupRobustMethod.Invoke(setup, new object[] { obj, true }); how = "OptimizationSetup." + SetupRobustMethod.Name + "(objective, true)"; }
                else return false;
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        /// <summary>true / false if the API can read the RO flag back, null if it cannot.</summary>
        public bool? ReadRobust(OptimizationObjective obj)
        {
            if (ObjectiveRobustGetter == null || obj == null || !ObjectiveRobustGetter.DeclaringType.IsInstanceOfType(obj)) return null;
            try { return (bool)ObjectiveRobustGetter.GetValue(obj, null); }
            catch (Exception) { return null; }
        }

        private void Fail(Exception ex)
        {
            var e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            RobustFailure = e.GetType().Name + ": " + e.Message;
        }

        /// <summary>One line for the run log.</summary>
        public string RobustSummary()
        {
            if (ProbeFailed) return "RO (robust) checkbox: API probe failed (" + ProbeError + ") - manual checklist.";
            string member = PointRobustOverload != null ? "OptimizationSetup." + Sig(PointRobustOverload)
                : ObjectiveRobustProperty != null ? ObjectiveRobustProperty.DeclaringType.Name + "." + ObjectiveRobustProperty.Name
                : ObjectiveRobustSetter != null ? ObjectiveRobustSetter.DeclaringType.Name + "." + Sig(ObjectiveRobustSetter)
                : SetupRobustMethod != null ? "OptimizationSetup." + Sig(SetupRobustMethod) : null;
            string list = RobustCandidates.Count == 0 ? "none" : string.Join("; ", RobustCandidates);
            if (member == null) return "RO (robust) checkbox: ESAPI " + EsapiVersion + " has no member to set it - manual checklist. Members containing 'robust': " + list + ".";
            return "RO (robust) checkbox: " + (AllowInvoke ? "set automatically via " : "member found but SiteSettings.cs Optimization.AllowReflectionForRobustFlag = false - manual checklist: ")
                   + member + ". Members containing 'robust': " + list + ".";
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ESAPI assembly version: " + EsapiVersion);
            if (ProbeFailed) sb.AppendLine("API probe failed (reflection restricted?): " + ProbeError + " -> all optional features use the manual checklist.");
            sb.AppendLine();
            sb.AppendLine(RobustSummary());
            foreach (var s in RobustCandidates) sb.AppendLine("   member: " + s);
            if (ObjectiveRobustGetter != null) sb.AppendLine("   read-back: " + ObjectiveRobustGetter.DeclaringType.Name + "." + ObjectiveRobustGetter.Name);
            sb.AppendLine();
            sb.AppendLine("Uncertainty-related methods found (field-specific perturbations are listed for manual entry");
            sb.AppendLine("unless one of these clearly creates per-field shifts - send this list to your developer):");
            foreach (var s in FieldUncertaintyCandidates) sb.AppendLine("   " + s);
            return sb.ToString();
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch (Exception) { return new Type[0]; }
        }

        private static string Sig(MethodInfo m)
        {
            return m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ") : " + m.ReturnType.Name;
        }
    }
}
