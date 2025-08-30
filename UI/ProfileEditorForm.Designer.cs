using System.Windows.Forms;

namespace DJWinOptimizer.UI
{
    partial class ProfileEditorForm
    {
        private System.ComponentModel.IContainer components = null;
        private TextBox txtName;
        private TextBox txtDescription;
        private TextBox txtPowerPlanGuid;
        private ComboBox cmbPowerPlans;
        private Button btnRefreshPlans;
        private CheckBox chkDisableDefender;
        private CheckBox chkBlockScans;
        private CheckBox chkPauseOneDrive;
        private CheckBox chkDisableIndexing;
        private CheckBox chkDisableSysMain;
        private CheckBox chkDisableGameDvr;
        private CheckBox chkDisableSpooler;
        private CheckBox chkPauseUpdates;
        private CheckBox chkWasapiExclusive;
        private CheckBox chkPreferAsio;
        private TextBox txtTargets;
        private TextBox txtPriorities;
        private Button btnAddFromRunning;
        private Button btnBrowseTarget;
        private Button btnOk;
        private Button btnCancel;
        private Label lblName;
        private Label lblDesc;
        private Label lblGuid;
        private Label lblTargets;
        private Label lblTargetsHint;
        private Label lblPriorities;
        private Label lblActivePlan;
        private Button btnUseCurrentPlan;
        private Button btnClonePlan;
        private ToolTip toolTip;
        private Label lblTimerRes;
        private ComboBox cmbTimerRes;
        private Label lblLaunch;
        private TextBox txtLaunch;
        private Label lblKill;
        private TextBox txtKill;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            txtName = new TextBox();
            txtDescription = new TextBox();
            txtPowerPlanGuid = new TextBox();
            chkDisableDefender = new CheckBox();
            chkBlockScans = new CheckBox();
            chkPauseOneDrive = new CheckBox();
            chkDisableIndexing = new CheckBox();
            chkDisableSysMain = new CheckBox();
            chkDisableGameDvr = new CheckBox();
            chkDisableSpooler = new CheckBox();
            chkPauseUpdates = new CheckBox();
            chkWasapiExclusive = new CheckBox();
            chkPreferAsio = new CheckBox();
            txtTargets = new TextBox();
            txtPriorities = new TextBox();
            btnOk = new Button();
            btnCancel = new Button();
            lblName = new Label();
            lblDesc = new Label();
            lblGuid = new Label();
            lblTargets = new Label();
            lblPriorities = new Label();
            cmbPowerPlans = new ComboBox();
            btnRefreshPlans = new Button();
            lblActivePlan = new Label();
            btnUseCurrentPlan = new Button();
            btnClonePlan = new Button();
            toolTip = new ToolTip(components);

            SuspendLayout();

            // Labels
            lblName.Text = "Name"; lblName.AutoSize = true; lblName.Location = new System.Drawing.Point(12, 15);
            lblDesc.Text = "Description"; lblDesc.AutoSize = true; lblDesc.Location = new System.Drawing.Point(12, 45);
            lblGuid.Text = "PowerPlan GUID"; lblGuid.AutoSize = true; lblGuid.Location = new System.Drawing.Point(12, 75);
            lblTargets.Text = "Targets (one per line)"; lblTargets.AutoSize = true; lblTargets.Location = new System.Drawing.Point(12, 210);
            lblTargetsHint = new Label(); lblTargetsHint.Text = "z.B. obs64.exe  |  oder per Buttons hinzufügen"; lblTargetsHint.AutoSize = true; lblTargetsHint.Location = new System.Drawing.Point(15, 230);
            lblPriorities.Text = "Priorities (exe=Priority per line)"; lblPriorities.AutoSize = true; lblPriorities.Location = new System.Drawing.Point(350, 210);
            lblTimerRes = new Label(); lblTimerRes.Text = "Timer Resolution"; lblTimerRes.AutoSize = true; lblTimerRes.Location = new System.Drawing.Point(12, 175);
            lblLaunch = new Label(); lblLaunch.Text = "Launch on Apply (one full path per line)"; lblLaunch.AutoSize = true; lblLaunch.Location = new System.Drawing.Point(12, 455);
            lblKill = new Label(); lblKill.Text = "Kill on Revert (process names, one per line)"; lblKill.AutoSize = true; lblKill.Location = new System.Drawing.Point(350, 455);

            // TextBoxes
            txtName.Location = new System.Drawing.Point(120, 12); txtName.Width = 500;
            txtDescription.Location = new System.Drawing.Point(120, 42); txtDescription.Width = 500;
            txtPowerPlanGuid.Location = new System.Drawing.Point(120, 72); txtPowerPlanGuid.Width = 500;
            cmbPowerPlans.DropDownStyle = ComboBoxStyle.DropDownList; cmbPowerPlans.Location = new System.Drawing.Point(120, 100); cmbPowerPlans.Width = 420;
            btnRefreshPlans.Text = "Refresh"; btnRefreshPlans.Location = new System.Drawing.Point(550, 98); btnRefreshPlans.Width = 70;
            lblActivePlan.Text = "Active: -"; lblActivePlan.AutoSize = true; lblActivePlan.Location = new System.Drawing.Point(120, 125);
            btnUseCurrentPlan.Text = "Use current"; btnUseCurrentPlan.Location = new System.Drawing.Point(550, 122); btnUseCurrentPlan.Width = 70;
            btnClonePlan.Text = "Clone"; btnClonePlan.Location = new System.Drawing.Point(550, 146); btnClonePlan.Width = 70;
            cmbTimerRes = new ComboBox(); cmbTimerRes.DropDownStyle = ComboBoxStyle.DropDownList; cmbTimerRes.Location = new System.Drawing.Point(120, 172); cmbTimerRes.Width = 150; cmbTimerRes.Items.AddRange(new object[] { "Stock", "OneMs" });

            // ToolTips
            toolTip.SetToolTip(cmbPowerPlans, "Select a Windows power plan to apply with this profile. '*” marks the active plan.");
            toolTip.SetToolTip(btnRefreshPlans, "Reload plans from powercfg");
            toolTip.SetToolTip(btnUseCurrentPlan, "Set the profile to use the currently active power plan");
            toolTip.SetToolTip(btnClonePlan, "Clone selected plan (powercfg -duplicatescheme) and select the clone");
            toolTip.SetToolTip(txtPowerPlanGuid, "Optional: manually set a custom power plan GUID. Leave empty for no change.");
            toolTip.SetToolTip(cmbTimerRes, "Timerauflösung: Stock (Standard) oder 1ms für niedrige Latenz.");
            // Admin hints for toggles that typically require elevation
            toolTip.SetToolTip(chkDisableDefender, "Requires Administrator to fully disable Defender realtime protection.");
            toolTip.SetToolTip(chkBlockScans, "Requires Administrator to block Defender scheduled scans.");
            toolTip.SetToolTip(chkPauseUpdates, "Requires Administrator to pause Windows Update services.");
            toolTip.SetToolTip(chkDisableGameDvr, "May require Administrator / group policy rights to disable Game DVR.");
            toolTip.SetToolTip(chkDisableIndexing, "Requires Administrator to stop Search Indexing service.");
            toolTip.SetToolTip(chkDisableSysMain, "Requires Administrator to stop SysMain service.");
            toolTip.SetToolTip(chkDisableSpooler, "Requires Administrator to stop Print Spooler service.");

            // Service toggles
            chkDisableDefender.Text = "Disable Defender realtime"; chkDisableDefender.Location = new System.Drawing.Point(15, 135);
            chkBlockScans.Text = "Block scheduled scans"; chkBlockScans.Location = new System.Drawing.Point(15, 160);
            chkPauseOneDrive.Text = "Pause OneDrive"; chkPauseOneDrive.Location = new System.Drawing.Point(15, 185);
            chkDisableIndexing.Text = "Disable Search Indexing"; chkDisableIndexing.Location = new System.Drawing.Point(15, 210);
            chkDisableSysMain.Text = "Disable SysMain"; chkDisableSysMain.Location = new System.Drawing.Point(250, 135);
            chkDisableGameDvr.Text = "Disable Game DVR"; chkDisableGameDvr.Location = new System.Drawing.Point(250, 160);
            chkDisableSpooler.Text = "Disable Print Spooler"; chkDisableSpooler.Location = new System.Drawing.Point(250, 185);
            chkPauseUpdates.Text = "Pause Windows Updates"; chkPauseUpdates.Location = new System.Drawing.Point(250, 210);

            // Audio
            chkWasapiExclusive.Text = "Enable WASAPI Exclusive"; chkWasapiExclusive.Location = new System.Drawing.Point(480, 110);
            chkPreferAsio.Text = "Prefer ASIO if available"; chkPreferAsio.Location = new System.Drawing.Point(480, 135);

            // Multiline areas
            txtTargets.Location = new System.Drawing.Point(15, 260); txtTargets.Multiline = true; txtTargets.ScrollBars = ScrollBars.Vertical; txtTargets.Size = new System.Drawing.Size(300, 180);
            btnAddFromRunning = new Button(); btnAddFromRunning.Text = "Add from running..."; btnAddFromRunning.Location = new System.Drawing.Point(15, 445); btnAddFromRunning.Width = 150;
            btnBrowseTarget = new Button(); btnBrowseTarget.Text = "Browse..."; btnBrowseTarget.Location = new System.Drawing.Point(175, 445); btnBrowseTarget.Width = 90;
            txtPriorities.Location = new System.Drawing.Point(350, 260); txtPriorities.Multiline = true; txtPriorities.ScrollBars = ScrollBars.Vertical; txtPriorities.Size = new System.Drawing.Size(270, 180);
            txtLaunch = new TextBox(); txtLaunch.Location = new System.Drawing.Point(15, 475); txtLaunch.Multiline = true; txtLaunch.ScrollBars = ScrollBars.Vertical; txtLaunch.Size = new System.Drawing.Size(300, 90);
            txtKill = new TextBox(); txtKill.Location = new System.Drawing.Point(350, 475); txtKill.Multiline = true; txtKill.ScrollBars = ScrollBars.Vertical; txtKill.Size = new System.Drawing.Size(270, 90);

            // Buttons
            btnOk.Text = "OK"; btnOk.Location = new System.Drawing.Point(460, 575); btnOk.DialogResult = DialogResult.OK;
            btnCancel.Text = "Cancel"; btnCancel.Location = new System.Drawing.Point(545, 575); btnCancel.DialogResult = DialogResult.Cancel;

            // Form
            ClientSize = new System.Drawing.Size(640, 615);
            Controls.Add(lblName);
            Controls.Add(lblDesc);
            Controls.Add(lblGuid);
            Controls.Add(lblTargets);
            Controls.Add(lblPriorities);
            Controls.Add(txtName);
            Controls.Add(txtDescription);
            Controls.Add(txtPowerPlanGuid);
            Controls.Add(cmbPowerPlans);
            Controls.Add(btnRefreshPlans);
            Controls.Add(lblActivePlan);
            Controls.Add(btnUseCurrentPlan);
            Controls.Add(btnClonePlan);
            Controls.Add(lblTimerRes);
            Controls.Add(cmbTimerRes);
            Controls.Add(chkDisableDefender);
            Controls.Add(chkBlockScans);
            Controls.Add(chkPauseOneDrive);
            Controls.Add(chkDisableIndexing);
            Controls.Add(chkDisableSysMain);
            Controls.Add(chkDisableGameDvr);
            Controls.Add(chkDisableSpooler);
            Controls.Add(chkPauseUpdates);
            Controls.Add(chkWasapiExclusive);
            Controls.Add(chkPreferAsio);
            Controls.Add(txtTargets);
            Controls.Add(btnAddFromRunning);
            Controls.Add(btnBrowseTarget);
            Controls.Add(txtPriorities);
            Controls.Add(lblLaunch);
            Controls.Add(txtLaunch);
            Controls.Add(lblKill);
            Controls.Add(txtKill);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            Controls.Add(lblTargetsHint);
            Text = "Edit Profile";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
