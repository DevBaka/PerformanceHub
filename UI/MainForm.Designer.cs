using System.Windows.Forms;

namespace PerformanceHub.UI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private TabControl tabControl;
        private TabPage tabProfiles;
        private TabPage tabMonitoring;
        private TabPage tabAutoSwitch;
        private TabPage tabLogs;
        private TabPage tabSettings;
        private TabPage tabEditor;
        private TabPage tabSoftwareManager;
        private TabPage tabTweaks;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar statusProgress;
        private ListBox listProfiles;
        private Button btnApplyProfile;
        private Button btnNewProfile;
        private Button btnDeleteProfile;
        private Button btnEditProfile;
        private Button btnImportProfile;
        private Button btnExportProfile;
        private TextBox txtLog;
        private CheckBox chkAutoSwitch;
        private Label lblLastTrigger;
        private CheckBox chkStartMinimized;
        private CheckBox chkAutoStartAutoSwitch;
        private CheckBox chkStartWithWindows;
        private Label lblAdminStatus;
        private Button btnRelaunchAdmin;
        private Label lblHKToggle;
        private TextBox txtHKToggle;
        private Label lblHKShowHide;
        private TextBox txtHKShowHide;
        private Label lblHKApplyProfile;
        private TextBox txtHKApplyProfile;
        private Button btnApplyHotkeys;
        private Label lblHKToggleStatus;
        private Label lblHKShowHideStatus;
        private Label lblHKApplyStatus;

        // Editor tab controls
        private TextBox txtProfName;
        private TextBox txtProfDesc;
        private ComboBox cboPowerPlans;
        private Button btnPlanRefresh;
        private Button btnPlanUseCurrent;
        private Button btnPlanClone;
        private Label lblActivePlan;
        private CheckBox chkSvcSysMain;
        private CheckBox chkSvcSearchIndex;
        private CheckBox chkSvcPrintSpooler;
        private CheckBox chkSvcDefenderRealtime;
        private CheckBox chkSvcOneDrivePause;
        private CheckBox chkSvcOneDriveStop;
        private CheckBox chkSvcWindowsUpdates;
        private CheckBox chkSvcGameDvr;
        private ListBox lstTargetsAny;
        private ListBox lstTargetsAll;
        private TextBox txtTargetAny;
        private TextBox txtTargetAll;
        private Button btnAnyAdd;
        private Button btnAnyBrowse;
        private Button btnAnyRemove;
        private Button btnAnyFromProc;
        private Button btnAllAdd;
        private Button btnAllBrowse;
        private Button btnAllRemove;
        private Button btnAllFromProc;
        private NumericUpDown nudPriority;
        private Button btnEditorSave;
        private Button btnEditorApply;
        private ToolTip toolTip;
        // Autostart editor controls
        private ListView lstAutoStart;
        private ColumnHeader colAutoPath;
        private ColumnHeader colAutoArgs;
        private ColumnHeader colAutoSkip;
        private ColumnHeader colAutoWait;
        private ColumnHeader colAutoDelay;
        private Button btnAutoAdd;
        private Button btnAutoEdit;
        private Button btnAutoRemove;
        private Button btnAutoUp;
        private Button btnAutoDown;
        // Monitoring tab (nested)
        private TabControl tabMonitoringTabs;
        private TabPage tabMonServices;
        private TabPage tabMonSystem;
        private TabPage tabMonDrivers;
        // System Monitoring labels (updated in code-behind)
        private Label lblCpuUtil;
        private Label lblCpuTemp;
        private Label lblGpuUtil;
        private Label lblGpuTemp;
        private Label lblDiskUtil;
        // Driver Latencies (placeholder list)
        private ListView lvDrivers;
        private ColumnHeader colDrvName;
        private ColumnHeader colDpcMs;
        private ColumnHeader colIsrMs;
        private ColumnHeader colEvents;
        private Label lblDpcPct;
        private Label lblIsrPct;
        private Label lblDrvStatus;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            tabControl = new TabControl();
            tabProfiles = new TabPage();
            tabEditor = new TabPage();
            tabMonitoring = new TabPage();
            tabAutoSwitch = new TabPage();
            tabLogs = new TabPage();
            tabSettings = new TabPage();
            tabSoftwareManager = new TabPage();
            tabTweaks = new TabPage();
            trayIcon = new NotifyIcon(components);
            trayMenu = new ContextMenuStrip(components);
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            statusProgress = new ToolStripProgressBar();
            listProfiles = new ListBox();
            btnApplyProfile = new Button();
            btnNewProfile = new Button();
            btnDeleteProfile = new Button();
            txtLog = new TextBox();
            chkAutoSwitch = new CheckBox();
            lblLastTrigger = new Label();

            SuspendLayout();

            // TabControl
            tabControl.Dock = DockStyle.Fill;
            tabControl.TabPages.AddRange(new[] { tabProfiles, tabEditor, tabMonitoring, tabAutoSwitch, tabLogs, tabSettings, tabSoftwareManager, tabTweaks });

            // Profiles Tab
            tabProfiles.Text = "Profiles";
            listProfiles.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listProfiles.Location = new System.Drawing.Point(10, 10);
            listProfiles.Size = new System.Drawing.Size(500, 300);
            listProfiles.SelectedIndexChanged += (s, e) => OnProfileSelectedChanged();

            btnApplyProfile.Text = "Apply";
            btnApplyProfile.Location = new System.Drawing.Point(520, 10);
            btnApplyProfile.Click += (s, e) => ApplySelectedProfile();

            btnNewProfile.Text = "New";
            btnNewProfile.Location = new System.Drawing.Point(520, 45);
            btnNewProfile.Click += (s, e) => NewProfile();

            btnDeleteProfile.Text = "Delete";
            btnDeleteProfile.Location = new System.Drawing.Point(520, 80);
            btnDeleteProfile.Click += (s, e) => DeleteProfile();

            btnEditProfile = new Button();
            btnEditProfile.Text = "Edit";
            btnEditProfile.Location = new System.Drawing.Point(520, 115);
            btnEditProfile.Click += (s, e) => EditProfile();

            btnImportProfile = new Button();
            btnImportProfile.Text = "Import";
            btnImportProfile.Location = new System.Drawing.Point(520, 150);
            btnImportProfile.Click += (s, e) => ImportProfile();

            btnExportProfile = new Button();
            btnExportProfile.Text = "Export";
            btnExportProfile.Location = new System.Drawing.Point(520, 185);
            btnExportProfile.Click += (s, e) => ExportProfile();

            tabProfiles.Controls.Add(listProfiles);
            tabProfiles.Controls.Add(btnApplyProfile);
            tabProfiles.Controls.Add(btnNewProfile);
            tabProfiles.Controls.Add(btnDeleteProfile);
            tabProfiles.Controls.Add(btnEditProfile);
            tabProfiles.Controls.Add(btnImportProfile);
            tabProfiles.Controls.Add(btnExportProfile);

            // Editor Tab
            tabEditor.Text = "Editor";
            var y = 10;
            tabEditor.Controls.Add(new Label { Text = "Name:", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            txtProfName = new TextBox { Location = new System.Drawing.Point(120, y - 4), Width = 280 };
            tabEditor.Controls.Add(txtProfName); y += 30;

            tabEditor.Controls.Add(new Label { Text = "Description:", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            txtProfDesc = new TextBox { Location = new System.Drawing.Point(120, y - 4), Width = 420 };
            tabEditor.Controls.Add(txtProfDesc); y += 30;

            tabEditor.Controls.Add(new Label { Text = "Power plan:", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            cboPowerPlans = new ComboBox { Location = new System.Drawing.Point(120, y - 4), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            tabEditor.Controls.Add(cboPowerPlans);
            btnPlanRefresh = new Button { Text = "Refresh", Location = new System.Drawing.Point(430, y - 6) };
            btnPlanUseCurrent = new Button { Text = "Use current", Location = new System.Drawing.Point(510, y - 6) };
            btnPlanClone = new Button { Text = "Clone", Location = new System.Drawing.Point(610, y - 6) };
            tabEditor.Controls.Add(btnPlanRefresh);
            tabEditor.Controls.Add(btnPlanUseCurrent);
            tabEditor.Controls.Add(btnPlanClone);
            y += 32;
            lblActivePlan = new Label { AutoSize = true, Location = new System.Drawing.Point(120, y), Text = "Active: -" };
            tabEditor.Controls.Add(lblActivePlan);
            y += 28;

            tabEditor.Controls.Add(new Label { Text = "Service toggles:", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            y += 24;
            chkSvcSysMain = new CheckBox { Text = "Disable SysMain", AutoSize = true, Location = new System.Drawing.Point(120, y) };
            chkSvcSearchIndex = new CheckBox { Text = "Disable Search Index", AutoSize = true, Location = new System.Drawing.Point(280, y) };
            chkSvcPrintSpooler = new CheckBox { Text = "Disable Print Spooler", AutoSize = true, Location = new System.Drawing.Point(470, y) };
            y += 24;
            chkSvcDefenderRealtime = new CheckBox { Text = "Disable Defender Realtime (admin)", AutoSize = true, Location = new System.Drawing.Point(120, y) };
            chkSvcOneDrivePause = new CheckBox { Text = "Pause OneDrive", AutoSize = true, Location = new System.Drawing.Point(380, y), Enabled = true };
            y += 24;
            chkSvcWindowsUpdates = new CheckBox { Text = "Pause Windows Updates (admin)", AutoSize = true, Location = new System.Drawing.Point(120, y) };
            chkSvcGameDvr = new CheckBox { Text = "Disable Game DVR (limited)", AutoSize = true, Location = new System.Drawing.Point(380, y) };
            chkSvcOneDriveStop = new CheckBox { Text = "Stop OneDrive (force)", AutoSize = true, Location = new System.Drawing.Point(580, y), Enabled = true };
            y += 24;
            var chkSvcXbox = new CheckBox { Text = "Disable Xbox services (admin)", AutoSize = true, Location = new System.Drawing.Point(120, y) };
            var chkSvcTelemetry = new CheckBox { Text = "Reduce telemetry (admin)", AutoSize = true, Location = new System.Drawing.Point(380, y) };
            y += 24;
            var chkSvcConsumer = new CheckBox { Text = "Disable consumer features (admin)", AutoSize = true, Location = new System.Drawing.Point(120, y) };
            var chkSvcActivity = new CheckBox { Text = "Disable activity history (admin)", AutoSize = true, Location = new System.Drawing.Point(380, y) };
            y += 12;
            // attach to fields via Tag for code-behind access (avoid new fields explosion)
            chkSvcXbox.Tag = nameof(PerformanceHub.Core.Models.ServiceToggles.DisableXboxServices);
            chkSvcTelemetry.Tag = nameof(PerformanceHub.Core.Models.ServiceToggles.ReduceTelemetry);
            chkSvcConsumer.Tag = nameof(PerformanceHub.Core.Models.ServiceToggles.DisableConsumerFeatures);
            chkSvcActivity.Tag = nameof(PerformanceHub.Core.Models.ServiceToggles.DisableActivityHistory);
            // Tooltips
            toolTip = new ToolTip(components);
            toolTip.SetToolTip(chkSvcXbox, "Stops Xbox* services: XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc.");
            toolTip.SetToolTip(chkSvcTelemetry, "Sets policies to lowest telemetry and stops DiagTrack/dmwappushservice.");
            toolTip.SetToolTip(chkSvcConsumer, "Disables cloud consumer features / tips via policy.");
            toolTip.SetToolTip(chkSvcActivity, "Disables Activity History collection and upload via policy.");
            y += 12;
            tabEditor.Controls.AddRange(new Control[] { chkSvcSysMain, chkSvcSearchIndex, chkSvcPrintSpooler, chkSvcDefenderRealtime, chkSvcOneDrivePause, chkSvcWindowsUpdates, chkSvcGameDvr, chkSvcOneDriveStop, chkSvcXbox, chkSvcTelemetry, chkSvcConsumer, chkSvcActivity });

            // Targets management (OR / AND) and Priority
            tabEditor.Controls.Add(new Label { Text = "Targets (OR):", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            tabEditor.Controls.Add(new Label { Text = "Targets (AND):", AutoSize = true, Location = new System.Drawing.Point(400, y) });
            y += 16;
            lstTargetsAny = new ListBox { Location = new System.Drawing.Point(120, y), Size = new System.Drawing.Size(240, 96) };
            lstTargetsAll = new ListBox { Location = new System.Drawing.Point(510, y), Size = new System.Drawing.Size(240, 96) };
            tabEditor.Controls.Add(lstTargetsAny);
            tabEditor.Controls.Add(lstTargetsAll);
            // OR row controls
            txtTargetAny = new TextBox { Location = new System.Drawing.Point(120, y + 104), Width = 160 };
            btnAnyBrowse = new Button { Text = "Browse...", Location = new System.Drawing.Point(285, y + 102), Width = 75 };
            btnAnyAdd = new Button { Text = "Add", Location = new System.Drawing.Point(365, y + 102), Width = 50 };
            btnAnyFromProc = new Button { Text = "From Processes...", Location = new System.Drawing.Point(420, y + 102), Width = 120 };
            btnAnyRemove = new Button { Text = "Remove", Location = new System.Drawing.Point(545, y + 102), Width = 70 };
            // AND row controls
            txtTargetAll = new TextBox { Location = new System.Drawing.Point(510, y + 104), Width = 160 };
            btnAllBrowse = new Button { Text = "Browse...", Location = new System.Drawing.Point(675, y + 102), Width = 75 };
            btnAllAdd = new Button { Text = "Add", Location = new System.Drawing.Point(755, y + 102), Width = 50 };
            btnAllFromProc = new Button { Text = "From Processes...", Location = new System.Drawing.Point(810, y + 102), Width = 120 };
            btnAllRemove = new Button { Text = "Remove", Location = new System.Drawing.Point(935, y + 102), Width = 70 };
            tabEditor.Controls.AddRange(new Control[] { txtTargetAny, btnAnyBrowse, btnAnyAdd, btnAnyFromProc, btnAnyRemove, txtTargetAll, btnAllBrowse, btnAllAdd, btnAllFromProc, btnAllRemove });
            y += 136;

            tabEditor.Controls.Add(new Label { Text = "Priority (higher wins):", AutoSize = true, Location = new System.Drawing.Point(10, y) });
            nudPriority = new NumericUpDown { Location = new System.Drawing.Point(160, y - 4), Width = 80, Minimum = -100, Maximum = 100, Increment = 1 };
            tabEditor.Controls.Add(nudPriority);
            y += 30;

            btnEditorSave = new Button { Text = "Save", Location = new System.Drawing.Point(120, y) };
            btnEditorApply = new Button { Text = "Apply", Location = new System.Drawing.Point(200, y) };
            tabEditor.Controls.Add(btnEditorSave);
            tabEditor.Controls.Add(btnEditorApply);

            // Wire editor events (code-behind handlers exist in MainForm.cs)
            btnPlanRefresh.Click += (s, e) => EditorRefreshPlans();
            btnPlanUseCurrent.Click += (s, e) => EditorUseCurrentPlan();
            btnPlanClone.Click += (s, e) => EditorCloneSelectedPlan();
            btnEditorSave.Click += (s, e) => EditorSaveProfile();
            btnEditorApply.Click += (s, e) => ApplySelectedProfile();

            // Tooltips
            toolTip.SetToolTip(chkSvcOneDrivePause, "OneDrive restartet durchgehend");
            toolTip.SetToolTip(chkSvcOneDriveStop, "OneDrive restartet durchgehend");
            toolTip.SetToolTip(btnAnyBrowse, "Datei auswählen und zur OR-Liste hinzufügen");
            toolTip.SetToolTip(btnAllBrowse, "Datei auswählen und zur AND-Liste hinzufügen");

            // Targets events (code-behind)
            btnAnyBrowse.Click += (s, e) => EditorBrowseTarget(true);
            btnAnyAdd.Click += (s, e) => EditorAddTarget(true);
            btnAnyRemove.Click += (s, e) => EditorRemoveSelectedTarget(true);
            btnAnyFromProc.Click += (s, e) => EditorAddFromProcesses(true);
            txtTargetAny.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditorAddTarget(true); } };
            btnAllBrowse.Click += (s, e) => EditorBrowseTarget(false);
            btnAllAdd.Click += (s, e) => EditorAddTarget(false);
            btnAllRemove.Click += (s, e) => EditorRemoveSelectedTarget(false);
            btnAllFromProc.Click += (s, e) => EditorAddFromProcesses(false);

            // ===== Autostart (LaunchOnEnter) =====
            // Controls
            var lblAuto = new Label { Text = "Autostart (ordered)", AutoSize = true };
            lblAuto.Location = new System.Drawing.Point(10, 420);
            tabEditor.Controls.Add(lblAuto);

            lstAutoStart = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Location = new System.Drawing.Point(10, 440),
                Size = new System.Drawing.Size(620, 140)
            };
            colAutoPath = new ColumnHeader { Text = "Path", Width = 220 };
            colAutoArgs = new ColumnHeader { Text = "Args", Width = 140 };
            colAutoSkip = new ColumnHeader { Text = "SkipIfRunning", Width = 100 };
            colAutoWait = new ColumnHeader { Text = "WaitRun(ms)", Width = 80 };
            colAutoDelay = new ColumnHeader { Text = "Delay(ms)", Width = 80 };
            lstAutoStart.Columns.AddRange(new[] { colAutoPath, colAutoArgs, colAutoSkip, colAutoWait, colAutoDelay });
            tabEditor.Controls.Add(lstAutoStart);

            btnAutoAdd = new Button { Text = "Add", Location = new System.Drawing.Point(640, 440), Size = new System.Drawing.Size(90, 26) };
            btnAutoEdit = new Button { Text = "Edit", Location = new System.Drawing.Point(640, 472), Size = new System.Drawing.Size(90, 26) };
            btnAutoRemove = new Button { Text = "Remove", Location = new System.Drawing.Point(640, 504), Size = new System.Drawing.Size(90, 26) };
            btnAutoUp = new Button { Text = "Up", Location = new System.Drawing.Point(640, 536), Size = new System.Drawing.Size(90, 26) };
            btnAutoDown = new Button { Text = "Down", Location = new System.Drawing.Point(640, 568), Size = new System.Drawing.Size(90, 26) };
            tabEditor.Controls.Add(btnAutoAdd);
            tabEditor.Controls.Add(btnAutoEdit);
            tabEditor.Controls.Add(btnAutoRemove);
            tabEditor.Controls.Add(btnAutoUp);
            tabEditor.Controls.Add(btnAutoDown);

            btnAutoAdd.Click += (s, e) => EditorAutoAdd();
            btnAutoEdit.Click += (s, e) => EditorAutoEditSelected();
            btnAutoRemove.Click += (s, e) => EditorAutoRemoveSelected();
            btnAutoUp.Click += (s, e) => EditorAutoMoveSelected(true);
            btnAutoDown.Click += (s, e) => EditorAutoMoveSelected(false);
            txtTargetAll.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditorAddTarget(false); } };

            // Monitoring Tab (parent)
            tabMonitoring.Text = "Monitoring";
            tabMonitoringTabs = new TabControl { Dock = DockStyle.Fill };
            tabMonServices = new TabPage { Text = "Services" };
            tabMonSystem = new TabPage { Text = "System Monitoring" };
            tabMonDrivers = new TabPage { Text = "Driver Latencies" };
            tabMonitoringTabs.TabPages.AddRange(new[] { tabMonServices, tabMonSystem, tabMonDrivers });
            tabMonitoring.Controls.Add(tabMonitoringTabs);

            // Services sub-tab (Diagnostics)
            var sy = 10;
            var x1 = 10; var x2 = 220;
            tabMonServices.Controls.Add(new Label { Text = "Diagnostics (read-only):", AutoSize = true, Location = new System.Drawing.Point(x1, sy) }); sy += 24;
            tabMonServices.Controls.Add(new Label { Text = "SysMain:", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcSysMain = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcSysMain); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "Windows Search (WSearch):", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcWSearch = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcWSearch); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "Print Spooler:", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcSpooler = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcSpooler); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "Windows Update (wuauserv):", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcWU = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcWU); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "BITS:", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcBITS = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcBITS); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "Delivery Optimization (dosvc):", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblSvcDO = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblSvcDO); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "Defender Realtime:", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblDefRealtime = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "-" }; tabMonServices.Controls.Add(lblDefRealtime); sy += 20;
            tabMonServices.Controls.Add(new Label { Text = "OneDrive Paused:", AutoSize = true, Location = new System.Drawing.Point(x1, sy) });
            var lblOneDrive = new Label { AutoSize = true, Location = new System.Drawing.Point(x2, sy), Text = "n/a" }; tabMonServices.Controls.Add(lblOneDrive); sy += 30;
            var btnSvcRefresh = new Button { Text = "Refresh", Location = new System.Drawing.Point(x1, sy) };
            tabMonServices.Controls.Add(btnSvcRefresh);
            btnSvcRefresh.Click += (s, e) => RefreshServiceStatus(lblSvcSysMain, lblSvcWSearch, lblSvcSpooler, lblSvcWU, lblSvcBITS, lblSvcDO, lblDefRealtime, lblOneDrive);

            // System Monitoring sub-tab (labels wired for updates)
            var smY = 10;
            tabMonSystem.Controls.Add(new Label { Text = "CPU Utilization:", AutoSize = true, Location = new System.Drawing.Point(10, smY) });
            lblCpuUtil = new Label { AutoSize = true, Location = new System.Drawing.Point(160, smY), Text = "-" }; tabMonSystem.Controls.Add(lblCpuUtil); smY += 22;
            tabMonSystem.Controls.Add(new Label { Text = "CPU Temperature:", AutoSize = true, Location = new System.Drawing.Point(10, smY) });
            lblCpuTemp = new Label { AutoSize = true, Location = new System.Drawing.Point(160, smY), Text = "n/a" }; tabMonSystem.Controls.Add(lblCpuTemp); smY += 22;
            tabMonSystem.Controls.Add(new Label { Text = "GPU Utilization:", AutoSize = true, Location = new System.Drawing.Point(10, smY) });
            lblGpuUtil = new Label { AutoSize = true, Location = new System.Drawing.Point(160, smY), Text = "n/a" }; tabMonSystem.Controls.Add(lblGpuUtil); smY += 22;
            tabMonSystem.Controls.Add(new Label { Text = "GPU Temperature:", AutoSize = true, Location = new System.Drawing.Point(10, smY) });
            lblGpuTemp = new Label { AutoSize = true, Location = new System.Drawing.Point(160, smY), Text = "n/a" }; tabMonSystem.Controls.Add(lblGpuTemp); smY += 22;
            tabMonSystem.Controls.Add(new Label { Text = "Disk Active Time:", AutoSize = true, Location = new System.Drawing.Point(10, smY) });
            lblDiskUtil = new Label { AutoSize = true, Location = new System.Drawing.Point(160, smY), Text = "-" }; tabMonSystem.Controls.Add(lblDiskUtil); smY += 22;
            // Driver Latencies sub-tab (placeholder ListView + totals)
            tabMonDrivers.Controls.Add(new Label { Text = "% DPC Time:", AutoSize = true, Location = new System.Drawing.Point(10, 10) });
            lblDpcPct = new Label { AutoSize = true, Location = new System.Drawing.Point(120, 10), Text = "-" }; tabMonDrivers.Controls.Add(lblDpcPct);
            tabMonDrivers.Controls.Add(new Label { Text = "% Interrupt Time:", AutoSize = true, Location = new System.Drawing.Point(10, 32) });
            lblIsrPct = new Label { AutoSize = true, Location = new System.Drawing.Point(120, 32), Text = "-" }; tabMonDrivers.Controls.Add(lblIsrPct);
            lblDrvStatus = new Label { AutoSize = true, Location = new System.Drawing.Point(10, 56), Text = "Driver latency collection: initializing..." }; tabMonDrivers.Controls.Add(lblDrvStatus);
            lvDrivers = new ListView { View = View.Details, FullRowSelect = true, GridLines = false, Location = new System.Drawing.Point(10, 80), Size = new System.Drawing.Size(560, 236), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            colDrvName = new ColumnHeader { Text = "Driver/Process", Width = 250 };
            colDpcMs = new ColumnHeader { Text = "DPC ms", Width = 90 };
            colIsrMs = new ColumnHeader { Text = "ISR ms", Width = 90 };
            colEvents = new ColumnHeader { Text = "Events", Width = 90 };
            lvDrivers.Columns.AddRange(new[] { colDrvName, colDpcMs, colIsrMs, colEvents });
            tabMonDrivers.Controls.Add(lvDrivers);

            // AutoSwitch Tab
            tabAutoSwitch.Text = "Auto-Switch";
            chkAutoSwitch.Text = "Enable Auto-Switch";
            chkAutoSwitch.Location = new System.Drawing.Point(10, 10);
            chkAutoSwitch.CheckedChanged += (s, e) => ToggleAutoSwitch();
            lblLastTrigger.Text = "Last trigger: -";
            lblLastTrigger.Location = new System.Drawing.Point(10, 40);
            tabAutoSwitch.Controls.Add(chkAutoSwitch);
            tabAutoSwitch.Controls.Add(lblLastTrigger);

            // Logs Tab
            tabLogs.Text = "Logs";
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.Dock = DockStyle.Fill;
            tabLogs.Controls.Add(txtLog);

            // Settings Tab
            tabSettings.Text = "Settings";

            // Software Manager Tab
            tabSoftwareManager.Text = "Software Manager";

            // Tweaks Tab
            tabTweaks.Text = "System Tweaks";
            chkStartMinimized = new CheckBox { Text = "Start minimized to tray", AutoSize = true, Location = new System.Drawing.Point(10, 10) };
            chkAutoStartAutoSwitch = new CheckBox { Text = "Auto start Auto-Switch", AutoSize = true, Location = new System.Drawing.Point(10, 40) };
            chkStartWithWindows = new CheckBox { Text = "Start with Windows (current user)", AutoSize = true, Location = new System.Drawing.Point(10, 70) };
            lblAdminStatus = new Label { AutoSize = true, Location = new System.Drawing.Point(10, 100), Text = "Admin status: -" };
            btnRelaunchAdmin = new Button { Text = "Relaunch as Administrator", AutoSize = true, Location = new System.Drawing.Point(10, 130) };
            btnRelaunchAdmin.Click += (s, e) => RelaunchAsAdministrator();

            // Hotkeys UI
            lblHKToggle = new Label { AutoSize = true, Location = new System.Drawing.Point(10, 170), Text = "Toggle Auto-Switch:" };
            txtHKToggle = new TextBox { Location = new System.Drawing.Point(180, 166), Width = 140 };
            lblHKToggleStatus = new Label { AutoSize = true, Location = new System.Drawing.Point(330, 170), Text = "-" };
            lblHKShowHide = new Label { AutoSize = true, Location = new System.Drawing.Point(10, 200), Text = "Show/Hide Window:" };
            txtHKShowHide = new TextBox { Location = new System.Drawing.Point(180, 196), Width = 140 };
            lblHKShowHideStatus = new Label { AutoSize = true, Location = new System.Drawing.Point(330, 200), Text = "-" };
            lblHKApplyProfile = new Label { AutoSize = true, Location = new System.Drawing.Point(10, 230), Text = "Apply selected profile:" };
            txtHKApplyProfile = new TextBox { Location = new System.Drawing.Point(180, 226), Width = 140 };
            lblHKApplyStatus = new Label { AutoSize = true, Location = new System.Drawing.Point(330, 230), Text = "-" };
            btnApplyHotkeys = new Button { Text = "Apply Hotkeys", AutoSize = true, Location = new System.Drawing.Point(10, 260) };
            btnApplyHotkeys.Click += (s, e) => ApplyHotkeySettings();

            tabSettings.Controls.Add(chkStartMinimized);
            tabSettings.Controls.Add(chkAutoStartAutoSwitch);
            tabSettings.Controls.Add(chkStartWithWindows);
            tabSettings.Controls.Add(lblAdminStatus);
            tabSettings.Controls.Add(btnRelaunchAdmin);
            tabSettings.Controls.Add(lblHKToggle);
            tabSettings.Controls.Add(txtHKToggle);
            tabSettings.Controls.Add(lblHKShowHide);
            tabSettings.Controls.Add(txtHKShowHide);
            tabSettings.Controls.Add(lblHKShowHideStatus);
            tabSettings.Controls.Add(lblHKApplyProfile);
            tabSettings.Controls.Add(txtHKApplyProfile);
            tabSettings.Controls.Add(lblHKApplyStatus);
            tabSettings.Controls.Add(btnApplyHotkeys);
            tabSettings.Controls.Add(lblHKToggleStatus);

            // Tray icon
            trayIcon.Text = "PerformanceHub";
            trayIcon.Icon = System.Drawing.SystemIcons.Application;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
            trayIcon.ContextMenuStrip = trayMenu;

            // Tray menu: items are built dynamically in code

            // Status strip
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(new ToolStripSeparator());
            statusProgress.Visible = false;
            statusProgress.Style = ProgressBarStyle.Marquee;
            statusProgress.MarqueeAnimationSpeed = 0;
            statusProgress.Size = new System.Drawing.Size(100, 16);
            statusStrip.Items.Add(statusProgress);
            statusLabel.Text = "Ready";

            // MainForm
            ClientSize = new System.Drawing.Size(700, 420);
            Controls.Add(tabControl);
            Controls.Add(statusStrip);
            Text = "PerformanceHub";
            Resize += MainForm_Resize;
            FormClosing += MainForm_FormClosing;

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
