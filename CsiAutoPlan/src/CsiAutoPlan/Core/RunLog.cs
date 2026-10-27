using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CsiAutoPlan.Core
{
    public enum LogLevel { Info, Warn, Error, Manual }

    public class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Step;
        public string Message;
        public override string ToString()
        {
            return string.Format("{0:HH:mm:ss} [{1,-6}] {2,-14} {3}", Time, Level, Step, Message);
        }
    }

    /// <summary>Collects messages for the on-screen report and the log file. "Manual" entries form the
    /// to-do checklist the planner must complete by hand.</summary>
    public class RunLog
    {
        public readonly List<LogEntry> Entries = new List<LogEntry>();
        public string Step = "";
        public event Action<LogEntry> Added;

        public void Info(string msg) { Add(LogLevel.Info, msg); }
        public void Warn(string msg) { Add(LogLevel.Warn, msg); }
        public void Error(string msg) { Add(LogLevel.Error, msg); }
        public void Manual(string msg) { Add(LogLevel.Manual, msg); }

        public void Info(string fmt, params object[] a) { Add(LogLevel.Info, string.Format(fmt, a)); }
        public void Warn(string fmt, params object[] a) { Add(LogLevel.Warn, string.Format(fmt, a)); }
        public void Error(string fmt, params object[] a) { Add(LogLevel.Error, string.Format(fmt, a)); }
        public void Manual(string fmt, params object[] a) { Add(LogLevel.Manual, string.Format(fmt, a)); }

        private void Add(LogLevel lvl, string msg)
        {
            var e = new LogEntry { Time = DateTime.Now, Level = lvl, Step = Step, Message = msg };
            Entries.Add(e);
            var h = Added;
            if (h != null) h(e);
        }

        public int Count(LogLevel lvl) { return Entries.Count(e => e.Level == lvl); }

        public string BuildReport()
        {
            var sb = new StringBuilder();
            var manual = Entries.Where(e => e.Level == LogLevel.Manual).ToList();
            var errors = Entries.Where(e => e.Level == LogLevel.Error).ToList();
            var warns = Entries.Where(e => e.Level == LogLevel.Warn).ToList();

            sb.AppendLine(string.Format("Errors: {0}   Warnings: {1}   Manual steps: {2}", errors.Count, warns.Count, manual.Count));
            sb.AppendLine();
            if (errors.Count > 0)
            {
                sb.AppendLine("=== ERRORS ===");
                foreach (var e in errors) sb.AppendLine("  [" + e.Step + "] " + e.Message);
                sb.AppendLine();
            }
            if (manual.Count > 0)
            {
                sb.AppendLine("=== MANUAL CHECKLIST (complete before physics check) ===");
                int i = 1;
                foreach (var e in manual) sb.AppendLine(string.Format("  {0,2}. [{1}] {2}", i++, e.Step, e.Message));
                sb.AppendLine();
            }
            if (warns.Count > 0)
            {
                sb.AppendLine("=== WARNINGS ===");
                foreach (var e in warns) sb.AppendLine("  [" + e.Step + "] " + e.Message);
                sb.AppendLine();
            }
            sb.AppendLine("=== FULL LOG ===");
            foreach (var e in Entries) sb.AppendLine(e.ToString());
            return sb.ToString();
        }

        public string SaveTo(string directory, string patientId, string prefix = "AutoPlan")
        {
            try
            {
                Directory.CreateDirectory(directory);
                var safeId = new string((patientId ?? "patient").Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
                var path = Path.Combine(directory, string.Format("{0}_{1}_{2:yyyyMMdd_HHmmss}.log", prefix, safeId, DateTime.Now));
                File.WriteAllText(path, BuildReport());
                return path;
            }
            catch (Exception ex)
            {
                Warn("Could not write log file: " + ex.Message);
                return null;
            }
        }
    }
}
