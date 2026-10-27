using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CsiAutoPlan.Settings;
using CsiAutoPlan.Core;
using CsiAutoPlan.Esapi;
using VMS.TPS.Common.Model.API;

namespace CsiAutoPlan.UI
{
    /// <summary>Review dialog: every automatically derived value can be checked and overridden before anything is written.</summary>
    public class MainForm : Form
    {
        private const string DraftUnionItem = "<draft: (D)CTV_BRAIN + (D)CTV_THECALSAC  - NON-CLINICAL>";
        private const string NoneItem = "<none>";

        private readonly CsiSession S;
        private readonly string settingsInfo;
        private bool loading = true;
        private bool shoulderEdited;   // only a user edit (never a clamped display value) is written back

        private ComboBox cbBody, cbBrain, cbThecal, cbCtv, cbVb, cbMachine, cbSpineCount, cbScheme;
        private static readonly string[] SchemeOrder = { SpineSchemes.PostExtended, SpineSchemes.PostBrain, SpineSchemes.Separate };
        private CheckBox chkOverwriteD, chkSpineZOverride;
        private RadioButton rbUserOrigin, rbComputed;
        private Label lblIso, lblTotal, lblProblems, lblShoulderInfo;
        private NumericUpDown nudShoulder, nudOverlap, nudPrefOverlap, nudClearance, nudSpineZ, nudDosePerFx, nudFractions;
        private TextBox txtCourse, txtPlan, txtGeoMessages;
        private DataGridView gridGeo, gridHu;
        private CheckBox chkDraft, chkHu, chkTargets, chkPlan, chkRangeMargins, chkObjectives, chkOptimize;
        private Button btnRun;

        public MainForm(CsiSession session, string settingsInfo)
        {
            S = session;
            this.settingsInfo = settingsInfo;
            Text = CsiAutoPlan.App.ScriptName() + "  -  CSI proton PBS auto-planning";
            Width = 980; Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            Build();
            LoadValues();
            loading = false;
            RefreshGeometry();
        }

        // ================================================================ layout
        private void Build()
        {
            var header = new Label
            {
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0),
                Text = string.Format("Patient {0}   |   Structure set {1}   |   Image {2}\n{3} (DICOM mm): {4:F1}, {5:F1}, {6:F1}",
                    S.Patient.Id, S.SS.Id, S.Image.Id,
                    S.Image.HasUserOrigin ? "User origin" : "NO USER ORIGIN - dialog coordinates relative to brain centre",
                    S.UserOrigin.x, S.UserOrigin.y, S.UserOrigin.z)
            };

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildTargetsTab());
            tabs.TabPages.Add(BuildGeometryTab());
            tabs.TabPages.Add(BuildPlanTab());
            tabs.TabPages.Add(BuildHuTab());
            tabs.TabPages.Add(BuildInfoTab());

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var btnCancel = new Button { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
            btnRun = new Button { Text = "Run selected steps", Width = 160 };
            btnRun.Click += OnRun;
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnRun);
            CancelButton = btnCancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(header);
        }

        private TabPage BuildTargetsTab()
        {
            var page = new TabPage("1  Structures");
            var t = Table(2);
            cbBody = AddCombo(t, "BODY");
            cbBrain = AddCombo(t, "AI brain");
            cbThecal = AddCombo(t, "AI thecal sac");
            cbCtv = AddCombo(t, "Target CTV (physician)");
            cbVb = AddCombo(t, "Involved vertebral bodies");
            chkOverwriteD = new CheckBox { Text = "Overwrite existing (D)CTV_BRAIN / (D)CTV_THECALSAC (loses manual edits)", AutoSize = true };
            t.Controls.Add(new Label()); t.Controls.Add(chkOverwriteD);
            lblProblems = new Label { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(880, 0) };
            t.Controls.Add(new Label()); t.Controls.Add(lblProblems);
            foreach (var cb in new[] { cbBody, cbBrain, cbThecal, cbCtv, cbVb }) cb.SelectedIndexChanged += (s, e) => OnStructuresChanged();
            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildGeometryTab()
        {
            var page = new TabPage("2  Isocenters && field split");
            var top = Table(4);
            top.Dock = DockStyle.Top; top.Height = 250;

            cbMachine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            Pair(top, "Treatment room", cbMachine);
            rbUserOrigin = new RadioButton { Text = "User origin", AutoSize = true };
            rbComputed = new RadioButton { Text = "Computed (brain centre, mid vertex-shoulders)", AutoSize = true };
            var rbPanel = new FlowLayoutPanel { AutoSize = true }; rbPanel.Controls.Add(rbUserOrigin); rbPanel.Controls.Add(rbComputed);
            Pair(top, "Brain isocenter", rbPanel);

            nudShoulder = Nud(-300, 300, 0.1m, 1); Pair(top, "Shoulder top Y (cm, user)", nudShoulder);
            lblIso = new Label { AutoSize = true }; Pair(top, "Brain iso", lblIso);
            nudClearance = Nud(0, 10, 0.5m, 1); Pair(top, "Brain target >= shoulders + (cm)", nudClearance);
            nudOverlap = Nud(0, 10, 0.5m, 1); Pair(top, "Min field-target overlap (cm)", nudOverlap);
            nudPrefOverlap = Nud(0, 10, 0.5m, 1); Pair(top, "Preferred overlap if room (cm)", nudPrefOverlap);
            cbScheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            cbScheme.Items.AddRange(new object[]
            {
                "POST extended down spine + lower isos (P3 example)",
                "POST on brain + separate spine isos (P4 examples)",
                "RPO/LPO only + separate spine isos (procedure)",
            });   // same order as SchemeOrder
            Pair(top, "Spine scheme", cbScheme);
            cbSpineCount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
            cbSpineCount.Items.AddRange(new object[] { "Auto", "1", "2", "3" });
            Pair(top, "Spine fields", cbSpineCount);
            var zPanel = new FlowLayoutPanel { AutoSize = true };
            chkSpineZOverride = new CheckBox { Text = "override", AutoSize = true };
            nudSpineZ = Nud(-30, 10, 1m, 0);
            zPanel.Controls.Add(nudSpineZ); zPanel.Controls.Add(chkSpineZOverride);
            Pair(top, "Spine iso Z (cm from brain iso)", zPanel);
            lblShoulderInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(900, 0) };
            top.Controls.Add(lblShoulderInfo); top.SetColumnSpan(lblShoulderInfo, 4);

            gridGeo = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            foreach (var c in new[] { "Field target", "Sup edge Y (cm)", "Inf edge Y (cm)", "Length (cm)", "Iso Y (cm)", "Iso Z (cm)", "Iso->sup (cm)", "Iso->inf (cm)" })
                gridGeo.Columns.Add(c.Replace(" ", ""), c);
            gridGeo.Columns[0].FillWeight = 260;   // "(D)PBSTV_MID2_COMPO" must stay readable
            txtGeoMessages = new TextBox { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            nudShoulder.ValueChanged += (s, e) => { if (!loading) shoulderEdited = true; };
            foreach (var n in new[] { nudShoulder, nudOverlap, nudPrefOverlap, nudClearance, nudSpineZ }) n.ValueChanged += (s, e) => RefreshGeometry();
            cbScheme.SelectedIndexChanged += (s, e) => RefreshGeometry();
            cbMachine.SelectedIndexChanged += (s, e) =>
            {
                // each room has its own default field arrangement (MachineOption.SpineScheme)
                if (!loading) { loading = true; cbScheme.SelectedIndex = Array.IndexOf(SchemeOrder, S.SchemeFor(cbMachine.SelectedItem as MachineOption)); loading = false; }
                RefreshGeometry();
            };
            cbSpineCount.SelectedIndexChanged += (s, e) => RefreshGeometry();
            rbUserOrigin.CheckedChanged += (s, e) => RefreshGeometry();
            chkSpineZOverride.CheckedChanged += (s, e) => RefreshGeometry();

            page.Controls.Add(gridGeo);
            page.Controls.Add(txtGeoMessages);
            page.Controls.Add(top);
            return page;
        }

        private TabPage BuildPlanTab()
        {
            var page = new TabPage("3  Plan && steps");
            var t = Table(2);
            txtCourse = new TextBox { Width = 160 }; Pair(t, "Course ID", txtCourse);
            txtPlan = new TextBox { Width = 160, MaxLength = 13 }; Pair(t, "Plan ID", txtPlan);
            nudDosePerFx = Nud(0.1m, 10, 0.05m, 2); Pair(t, "Dose / fraction (Gy RBE)", nudDosePerFx);
            nudFractions = Nud(1, 60, 1, 0); Pair(t, "Fractions", nudFractions);
            lblTotal = new Label { AutoSize = true }; Pair(t, "Total", lblTotal);
            nudDosePerFx.ValueChanged += (s, e) => UpdateTotal();
            nudFractions.ValueChanged += (s, e) => UpdateTotal();

            var steps = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
            chkDraft = Step(steps, "A. (D) starting structures from the AI contours (StructureRecipes.cs)");
            chkHu = Step(steps, "B. HU overrides (tab 4)");
            chkTargets = Step(steps, "C. Field targets, OPT and other step-C structures (StructureRecipes.cs)");
            chkPlan = Step(steps, "D. Course, plan, Rx, MFO, calc options, brain + spine fields, range shifter, air gap, target margins");
            var rm = S.Cfg.RangeMargins;
            chkRangeMargins = new CheckBox
            {
                AutoSize = true, Margin = new Padding(24, 0, 3, 3),
                Text = string.Format("Proximal / distal margins from each field's R and SOBP:  distal = R x {0}% + {1} mm,  proximal = (R - SOBP) x {0}% + {1} mm",
                                     rm.RangeUncertaintyPercent, rm.AddedMarginMm)
            };
            steps.Controls.Add(chkRangeMargins);
            chkPlan.CheckedChanged += (s, e) => chkRangeMargins.Enabled = chkPlan.Checked;
            chkObjectives = Step(steps, "E. NUPO objectives, robust flags, plan uncertainty scenarios, perturbation list");
            chkOptimize = Step(steps, "F. Beam line + NUPO optimization + dose (long - Eclipse is busy until done)");
            var note = new Label
            {
                AutoSize = true, MaximumSize = new Size(740, 0), ForeColor = Color.DimGray,
                Text = "Typical use: pass 1 = A (pre-MD).  Pass 2 after physician CTV = B-E, then add the listed field perturbations / RO flags " +
                       "in Eclipse.  Pass 3 with the plan open = F only."
            };
            steps.Controls.Add(note);
            t.Controls.Add(new Label { Text = "Steps", AutoSize = true });
            t.Controls.Add(steps);
            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildHuTab()
        {
            var page = new TabPage("4  HU overrides");
            gridHu = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            gridHu.Columns.Add("Structure", "Structure");
            gridHu.Columns.Add("Rule", "Rule");
            gridHu.Columns.Add("Current", "Current HU");
            gridHu.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "Apply" });
            gridHu.Columns[0].ReadOnly = gridHu.Columns[1].ReadOnly = gridHu.Columns[2].ReadOnly = true;
            foreach (DataGridViewColumn c in gridHu.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            page.Controls.Add(gridHu);
            return page;
        }

        private TabPage BuildInfoTab()
        {
            var page = new TabPage("5  API / settings");
            var txt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9f), WordWrap = false };
            txt.Text = (settingsInfo + Environment.NewLine + Environment.NewLine + (S.Caps != null ? S.Caps.Describe() : "")).Replace("\n", Environment.NewLine);
            page.Controls.Add(txt);
            return page;
        }

        // ================================================================ values
        private void LoadValues()
        {
            var ids = S.SS.Structures.Where(s => !s.IsEmpty).Select(s => s.Id).OrderBy(x => x).ToArray();
            Fill(cbBody, ids, S.Body, false);
            Fill(cbBrain, ids, S.AiBrain, false);
            Fill(cbThecal, ids, S.AiThecalSac, false);
            cbCtv.Items.Add(DraftUnionItem); cbCtv.Items.AddRange(ids);
            cbCtv.SelectedItem = S.UseDraftUnion || S.TargetCtv == null ? (object)DraftUnionItem : S.TargetCtv.Id;
            Fill(cbVb, ids, S.InvolvedVb, true);

            foreach (var m in S.Cfg.Machines.Where(x => x.Enabled || x == S.Machine)) cbMachine.Items.Add(m);   // disabled rooms (Enabled="false") are hidden
            if (S.Machine != null) cbMachine.SelectedItem = S.Machine;
            rbUserOrigin.Enabled = S.Image.HasUserOrigin;
            rbUserOrigin.Checked = S.UseUserOrigin; rbComputed.Checked = !S.UseUserOrigin;
            SyncAnatomyControls();
            nudOverlap.Value = Clamp(nudOverlap, (decimal)(S.OverlapMm / 10));
            nudPrefOverlap.Value = Clamp(nudPrefOverlap, (decimal)(S.PreferredOverlapMm / 10));
            cbScheme.SelectedIndex = Array.IndexOf(SchemeOrder, S.Scheme);
            nudClearance.Value = Clamp(nudClearance, (decimal)(S.ShoulderClearanceMm / 10));
            cbSpineCount.SelectedIndex = 0;

            txtCourse.Text = S.CourseId;
            txtPlan.Text = S.PlanId;
            nudDosePerFx.Value = Clamp(nudDosePerFx, (decimal)S.DosePerFractionGy);
            nudFractions.Value = Clamp(nudFractions, S.Fractions);
            UpdateTotal();

            // Two-pass defaults: with an ion plan open that already has uncertainty scenarios, only optimize.
            var open = S.Context.IonPlanSetup;
            bool pass3 = open != null && open.PlanUncertainties.Any();
            chkDraft.Checked = !pass3 && S.UseDraftUnion;
            chkHu.Checked = !pass3 && S.HuRows.Count > 0;
            chkTargets.Checked = chkPlan.Checked = chkObjectives.Checked = !pass3 && !S.UseDraftUnion;
            chkOptimize.Checked = pass3;
            chkRangeMargins.Checked = S.UseRangeMargins;
            chkRangeMargins.Enabled = chkPlan.Checked;
            if (pass3) { txtCourse.Text = open.Course.Id; txtPlan.Text = open.Id; }

            foreach (var r in S.HuRows)
            {
                string rule = r.Rule.Mode == HuMode.Fixed ? r.Rule.Hu.ToString("F0") + " HU" :
                              r.Rule.Mode == HuMode.NeighborMean ? "mean of surrounding tissue" : "manual: " + r.Rule.Note;
                int idx = gridHu.Rows.Add(r.Structure.Id, rule, r.CurrentHu, r.Apply);
                gridHu.Rows[idx].Tag = r;
                if (r.Rule.Mode == HuMode.ReportOnly) gridHu.Rows[idx].Cells[3].ReadOnly = true;
            }
            lblProblems.Text = string.Join(Environment.NewLine, S.BlockingProblems);
        }

        private void OnStructuresChanged()
        {
            if (loading) return;
            S.Body = Pick(cbBody);
            S.AiBrain = Pick(cbBrain);
            S.AiThecalSac = Pick(cbThecal);
            S.InvolvedVb = Pick(cbVb);
            S.UseDraftUnion = (cbCtv.SelectedItem as string) == DraftUnionItem;
            S.TargetCtv = S.UseDraftUnion ? null : Pick(cbCtv);
            try { S.Reevaluate(); } catch (Exception ex) { S.BlockingProblems.Add(ex.Message); }
            lblProblems.Text = string.Join(Environment.NewLine, S.BlockingProblems);
            shoulderEdited = false;
            SyncAnatomyControls();
            RefreshGeometry();
        }

        private void RefreshGeometry()
        {
            if (loading || S.BlockingProblems.Count > 0 && S.Geometry == null) { UpdateRunState(); return; }
            S.Machine = cbMachine.SelectedItem as MachineOption;
            S.UseUserOrigin = rbUserOrigin.Checked;
            if (shoulderEdited) S.ShoulderTopZ = S.FromUserYcm((double)nudShoulder.Value);
            S.OverlapMm = (double)nudOverlap.Value * 10;
            S.PreferredOverlapMm = (double)nudPrefOverlap.Value * 10;
            S.Scheme = SchemeOrder[Math.Max(0, cbScheme.SelectedIndex)];
            S.ShoulderClearanceMm = (double)nudClearance.Value * 10;
            S.ForcedSpineFieldCount = cbSpineCount.SelectedIndex <= 0 ? 0 : cbSpineCount.SelectedIndex;
            S.SpineIsoZcmOverride = chkSpineZOverride.Checked ? (double?)(double)nudSpineZ.Value : null;
            nudSpineZ.Enabled = chkSpineZOverride.Checked;
            S.Recompute();
            if (!chkSpineZOverride.Checked) { loading = true; nudSpineZ.Value = Clamp(nudSpineZ, (decimal)S.IsoOffsetZcm(S.SpineIsoY)); loading = false; }

            var b = S.BrainIso;
            lblIso.Text = string.Format("user coords X {0:F1}  Y {1:F1}  Z {2:F1} cm", (b.x - S.UserOrigin.x) / 10, S.ToUserYcm(b.z), -(b.y - S.UserOrigin.y) / 10);

            gridGeo.Rows.Clear();
            if (S.Geometry != null)
            {
                foreach (var seg in S.Geometry.All())
                {
                    double isoZcm = (seg.Name == "BR" || seg.Name == "POST") ? 0 : S.IsoOffsetZcm(S.SpineIsoY);
                    gridGeo.Rows.Add(RecipeBook.Id(Role.FieldTarget, seg.Name) ?? seg.Name, F(S.ToUserYcm(seg.PbstvHiZ)), F(S.ToUserYcm(seg.PbstvLoZ)), F(seg.PbstvLengthMm / 10),
                                     F(S.IsoOffsetYcm(seg.IsoZ)), F(isoZcm), F(seg.DistSupEdgeMm / 10), F(seg.DistInfEdgeMm / 10));
                }
                var msgs = new List<string>();
                msgs.AddRange(S.Geometry.Errors.Select(e => "ERROR: " + e));
                msgs.AddRange(S.Geometry.Warnings.Select(w => "Warning: " + w));
                msgs.Add(string.Format("{0} isocenter(s) / {1} fields; planned field-target overlap {2:F1} cm (minimum {3:F1} cm + one slice). {4}",
                                       S.Geometry.IsocenterCount, S.FieldCount,
                                       S.Geometry.OverlapUsedMm / 10, S.OverlapMm / 10, S.SchemeComparison));
                msgs.Add(S.IsoSummary);
                msgs.Add(string.Format("CTV Y {0:F1} to {1:F1} cm (user).  Limit on {2}: {3:F1} cm from iso incl. {4} mm spot margin.  Iso Y/Z are offsets from the brain iso.",
                    S.ToUserYcm(S.CtvTopZ), S.ToUserYcm(S.CtvBottomZ), S.Machine != null ? S.Machine.DisplayName : "-", S.Machine != null ? S.Machine.HalfFieldLimitMm / 10 : 0,
                    Math.Max(S.Cfg.Beams.BrainLateralMarginMm, S.Cfg.Beams.SpineLateralMarginMm)));
                txtGeoMessages.Text = string.Join(Environment.NewLine, msgs);
            }
            UpdateRunState();
        }

        /// <summary>Shows the measured shoulder level / spine iso Z without marking them as user edits.</summary>
        private void SyncAnatomyControls()
        {
            bool was = loading; loading = true;
            nudShoulder.Value = Clamp(nudShoulder, (decimal)S.ToUserYcm(S.ShoulderTopZ));
            nudSpineZ.Value = Clamp(nudSpineZ, (decimal)S.IsoOffsetZcm(S.SpineIsoY));
            lblShoulderInfo.Text = S.Shoulder == null ? "" : (S.Shoulder.Success ? "Auto-detected: " : "Detection FAILED - enter manually: ") + S.Shoulder.Message;
            loading = was;
        }

        private void UpdateTotal() { lblTotal.Text = string.Format("{0:F2} Gy(RBE)", nudDosePerFx.Value * nudFractions.Value); }

        private void UpdateRunState()
        {
            btnRun.Enabled = S.BlockingProblems.Count == 0;
        }

        private void OnRun(object sender, EventArgs e)
        {
            S.CourseId = txtCourse.Text.Trim();
            S.PlanId = txtPlan.Text.Trim();
            S.DosePerFractionGy = (double)nudDosePerFx.Value;
            S.Fractions = (int)nudFractions.Value;
            S.OverwriteDraftStructures = chkOverwriteD.Checked;
            S.DoDraftStructures = chkDraft.Checked;
            S.DoHuOverrides = chkHu.Checked;
            S.DoFieldTargets = chkTargets.Checked;
            S.DoPlan = chkPlan.Checked;
            S.UseRangeMargins = chkRangeMargins.Checked;
            S.DoObjectives = chkObjectives.Checked;
            S.DoOptimize = chkOptimize.Checked;
            foreach (DataGridViewRow row in gridHu.Rows)
            {
                var hr = row.Tag as HuRow;
                if (hr != null) hr.Apply = Convert.ToBoolean(row.Cells[3].Value);
            }

            if (S.DoPlan && S.Machine != null)
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(S.Machine.PatientSupportDeviceId)) missing.Add("PatientSupportDeviceId");
                if (string.IsNullOrWhiteSpace(S.Machine.SnoutId)) missing.Add("SnoutId");
                if (string.IsNullOrWhiteSpace(S.Machine.ToleranceId)) missing.Add("ToleranceId");
                if (string.IsNullOrWhiteSpace(S.Machine.RangeShifterId)) missing.Add("RangeShifterId");
                if (missing.Count > 0)
                {
                    MessageBox.Show(this, string.Format("Room '{0}' is missing {1} in SiteSettings.cs, so the plan cannot be created.\n\n" +
                        "Tab 5 lists the IDs this patient's existing proton plans use, as lines for SiteSettings.cs. " +
                        "Add them there, rebuild and approve the new version.\n\n" +
                        "You can still run the structure steps (A-C) now.", S.Machine.DisplayName, string.Join(", ", missing)),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if (S.DoPlan && !S.DoFieldTargets)
            {
                Cursor = Cursors.WaitCursor;
                var tp = S.FieldTargetProblems();
                Cursor = Cursors.Default;
                if (tp.Count > 0)
                {
                    MessageBox.Show(this, "Step D creates the fields on the field-target structures of the split shown in tab 2, but:\n\n  - " +
                        string.Join("\n  - ", tp) + "\n\nTick step C (Field targets) so they are rebuilt for this split, or change the room / scheme back.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            var needsGeometry = S.DoFieldTargets || S.DoPlan;
            if (needsGeometry && (S.Geometry == null || !S.Geometry.IsFeasible))
            {
                MessageBox.Show(this, "The field split has errors (tab 2). Fix them before creating field targets or fields.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (needsGeometry && S.UseDraftUnion &&
                MessageBox.Show(this, "No physician CTV is selected. Field targets will be built from the DRAFT union of the (D) structures.\n\nThis is for development / dry runs only. Continue?",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (S.DoOptimize &&
                MessageBox.Show(this, "Optimization can take up to ~30 min and Eclipse will not respond until it finishes.\nMake sure field perturbations and RO flags are entered.\n\nContinue?",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ================================================================ small helpers
        private static TableLayoutPanel Table(int cols)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, AutoSize = true, Padding = new Padding(10), AutoScroll = true };
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static void Pair(TableLayoutPanel t, string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
            t.Controls.Add(c);
        }

        private static ComboBox AddCombo(TableLayoutPanel t, string label)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
            Pair(t, label, cb);
            return cb;
        }

        private static NumericUpDown Nud(decimal min, decimal max, decimal inc, int decimals)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = decimals, Width = 90 };
        }

        private static decimal Clamp(NumericUpDown n, decimal v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        private static CheckBox Step(FlowLayoutPanel p, string text)
        {
            var c = new CheckBox { Text = text, AutoSize = true };
            p.Controls.Add(c);
            return c;
        }

        private static void Fill(ComboBox cb, string[] ids, Structure selected, bool allowNone)
        {
            if (allowNone) cb.Items.Add(NoneItem);
            cb.Items.AddRange(ids);
            if (selected != null) cb.SelectedItem = selected.Id;
            else if (allowNone) cb.SelectedItem = NoneItem;
        }

        private Structure Pick(ComboBox cb)
        {
            var id = cb.SelectedItem as string;
            if (id == null || id == NoneItem || id == DraftUnionItem) return null;
            return StructureTools.FindExact(S.SS, id);
        }

        private static string F(double v) { return v.ToString("F1"); }
    }
}
