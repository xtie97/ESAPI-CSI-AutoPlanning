using System;
using System.Drawing;
using System.Windows.Forms;
using CsiAutoPlan.Core;

namespace CsiAutoPlan.UI
{
    /// <summary>Live log while steps run, then the final report (errors, manual checklist, warnings, full log).</summary>
    public class ReportForm : Form
    {
        private readonly TextBox txt;
        private readonly Button btnClose, btnCopy;
        private readonly Label status;

        public ReportForm(RunLog log)
        {
            Text = CsiAutoPlan.App.ScriptName() + " - progress";
            Width = 1000; Height = 700;
            StartPosition = FormStartPosition.CenterScreen;
            txt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
            status = new Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 8, 0), Text = "Running... Eclipse is busy until all steps finish." };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            btnClose = new Button { Text = "Close", Width = 100, Enabled = false };
            btnClose.Click += (s, e) => Close();
            btnCopy = new Button { Text = "Copy report", Width = 120, Enabled = false };
            btnCopy.Click += (s, e) => { try { Clipboard.SetText(txt.Text); } catch (Exception) { } };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnCopy);
            Controls.Add(txt);
            Controls.Add(bottom);
            Controls.Add(status);
            log.Added += OnAdded;
        }

        private void OnAdded(LogEntry e)
        {
            txt.AppendText(e + Environment.NewLine);
            txt.Refresh();      // paints this control only; does not pump Eclipse's message queue
            status.Refresh();
        }

        public void ShowFinal(RunLog log, string logPath)
        {
            Text = CsiAutoPlan.App.ScriptName() + " - report";
            status.Text = string.Format("Finished: {0} error(s), {1} manual step(s). Log: {2}.  Save the patient in Eclipse to keep the changes.",
                                        log.Count(LogLevel.Error), log.Count(LogLevel.Manual), logPath ?? "(not saved)");
            status.ForeColor = log.Count(LogLevel.Error) > 0 ? Color.DarkRed : Color.DarkGreen;
            txt.Text = log.BuildReport().Replace("\n", Environment.NewLine).Replace("\r\r", "\r");
            txt.SelectionStart = 0; txt.ScrollToCaret();
            btnClose.Enabled = btnCopy.Enabled = true;
        }
    }
}
