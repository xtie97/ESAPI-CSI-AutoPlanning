using System;
using System.Windows.Forms;
using VMS.TPS.Common.Model.API;

// Write-enabled plugin: must be approved in Eclipse (Script Approvals) before it can run clinically.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>Eclipse entry point: External Beam Planning workspace > Tools > Scripts > AutoPlanv&lt;version&gt;.esapi.dll (name set by ScriptVersion in the csproj).
    /// The Window parameter is optional for binary plug-ins; this plugin shows its own WinForms dialogs.</summary>
    public class Script
    {
        public Script() { }

        public void Execute(ScriptContext context)
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                CsiAutoPlan.App.Run(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), CsiAutoPlan.App.ScriptName() + " - unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
