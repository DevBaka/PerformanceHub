using System;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using DJWinOptimizer.Core;
using DJWinOptimizer.Core.Models;
using DJWinOptimizer.Utils;
using DJWinOptimizer.Services;

namespace DJWinOptimizer.UI
{
    public partial class MainForm : Form
    {
        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private ToolStripMenuItem? _trayAutoSwitchItem;
        private ToolStripMenuItem? _trayShowHideItem;
        private bool _trayBalloonShown;
        // Monitoring
        private readonly System.Windows.Forms.Timer _monTimer = new();
        private PerformanceCounter? _cpuTotal;
        private PerformanceCounter? _diskActiveTime;
        private PerformanceCounter? _cpuDpcTime;
        private PerformanceCounter? _cpuIsrTime;
        private bool _perfReady;
        // Thresholds
        private const float CpuWarn = 90f, CpuCrit = 98f;
        private const float DiskWarn = 90f, DiskCrit = 98f;
        private const float DpcWarn = 8f, DpcCrit = 20f;
        private const float IsrWarn = 3f, IsrCrit = 10f;
        private Sev _lastMonSev = Sev.Normal;
        // Sensors (LibreHardwareMonitor)
        private Computer? _hw;
        private const float CpuTempWarn = 85f, CpuTempCrit = 90f;
        private const float GpuTempWarn = 85f, GpuTempCrit = 90f;
        private bool _drvPaused = false;
        // Sorting for Driver Latencies list
        private class ListViewItemComparer : System.Collections.IComparer
        {
            public int Column { get; set; }
            public bool Desc { get; set; }
            public int Compare(object? x, object? y)
            {
                var a = x as ListViewItem; var b = y as ListViewItem;
                if (a == null || b == null) return 0;
                string sa = Column < a.SubItems.Count ? a.SubItems[Column].Text : a.Text;
                string sb = Column < b.SubItems.Count ? b.SubItems[Column].Text : b.Text;
                // Try numeric compare (dot/comma tolerant)
                if (double.TryParse(sa.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var da) &&
                    double.TryParse(sb.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var db))
                {
                    int cmp = da.CompareTo(db);
                    return Desc ? -cmp : cmp;
                }
                int sc = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
                return Desc ? -sc : sc;
            }
        }
        private readonly ListViewItemComparer _drvSorter = new ListViewItemComparer { Column = 4, Desc = true };
        // Driver latency ETW monitor
        private DriverLatencyMonitor? _drvMon;
        private long _prevEtwTotal;
        private long _prevEtwMatched;
        private DateTime _prevEtwTs = DateTime.MinValue;
        private readonly System.Collections.Generic.Dictionary<string, (int Count, DateTime Ts)> _prevDriverCounts = new();
        
        // Cached controls
        private SoftwareManagerControl? _softwareManagerControl;
        private TweaksControl? _tweaksControl;

        public MainForm()
        {
            InitializeComponent();
            
            // Subscribe to static app logger events for UI display
            App.SubscribeToLogs(OnLogMessage);
            App.Instance!.Logger.Info("MainForm initialized - logging connected");
            App.Instance!.Logger.Info($"Logger type: {App.Instance!.Logger.GetType().Name}");
            App.Instance!.Logger.Info($"PackageManager: {App.Instance!.PackageManager?.GetType().Name}");
            App.Instance!.Logger.Info($"SystemTweaks: {App.Instance!.SystemTweaks?.GetType().Name}");
            
            // Enable copy support on Driver Latencies list
            InitListViewCopy(lvDrivers);
            // Ensure Events/s column exists for Driver Latencies
            try
            {
                if (lvDrivers != null && lvDrivers.Columns.Count < 5)
                {
                    lvDrivers.Columns.Add("Events/s", 80);
                }
                if (lvDrivers != null)
                {
                    // Reduce flicker
                    try { var pi = lvDrivers.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); pi?.SetValue(lvDrivers, true, null); } catch { }
                    lvDrivers.ListViewItemSorter = _drvSorter;
                    lvDrivers.ColumnClick += (s, e) =>
                    {
                        // Toggle sort on clicked column
                        if (_drvSorter.Column == e.Column) _drvSorter.Desc = !_drvSorter.Desc; else { _drvSorter.Column = e.Column; _drvSorter.Desc = true; }
                        lvDrivers.Sort();
                    };
                    // Context menu: toggle system rows
                    var cm = new ContextMenuStrip();
                    var miShowSystem = new ToolStripMenuItem("System-ETW Zeilen anzeigen") { CheckOnClick = true };
                    miShowSystem.Checked = App.Instance?.Config?.Monitoring?.ShowSystemEtwRows ?? true;
                    miShowSystem.CheckedChanged += (_, __) =>
                    {
                        try
                        {
                            var cfg = App.Instance?.Config;
                            if (cfg != null)
                            {
                                cfg.Monitoring.ShowSystemEtwRows = miShowSystem.Checked;
                                DJWinOptimizer.Settings.AppSettings.Save(cfg);
                            }
                        }
                        catch { }
                    };
                    var miSortByEvps = new ToolStripMenuItem("Nach Events/s sortieren (absteigend)");
                    miSortByEvps.Click += (_, __) => { _drvSorter.Column = 4; _drvSorter.Desc = true; lvDrivers.Sort(); };
                    var miCopyCsv = new ToolStripMenuItem("Kopieren (CSV)");
                    miCopyCsv.Click += (_, __) =>
                    {
                        try
                        {
                            var lines = new System.Text.StringBuilder();
                            // header
                            var headers = lvDrivers.Columns.Cast<ColumnHeader>().Select(c => c.Text);
                            lines.AppendLine(string.Join(",", headers));
                            foreach (ListViewItem it in lvDrivers.SelectedItems)
                            {
                                var cells = it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(si => si.Text.Replace('"', '\'')).Select(t => t.Contains(',') ? "\"" + t + "\"" : t);
                                lines.AppendLine(string.Join(",", cells));
                            }
                            if (lines.Length > 0) Clipboard.SetText(lines.ToString());
                        }
                        catch { }
                    };
                    var miExportCsv = new ToolStripMenuItem("Exportieren… (CSV)");
                    miExportCsv.Click += (_, __) =>
                    {
                        try
                        {
                            using var sfd = new SaveFileDialog { Filter = "CSV Dateien (*.csv)|*.csv", FileName = $"DriverLatencies_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
                            if (sfd.ShowDialog(this) == DialogResult.OK)
                            {
                                using var sw = new System.IO.StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8);
                                var headers = lvDrivers.Columns.Cast<ColumnHeader>().Select(c => c.Text);
                                sw.WriteLine(string.Join(",", headers));
                                foreach (ListViewItem it in lvDrivers.Items)
                                {
                                    var cells = it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(si => si.Text.Replace('"', '\'')).Select(t => t.Contains(',') ? "\"" + t + "\"" : t);
                                    sw.WriteLine(string.Join(",", cells));
                                }
                            }
                        }
                        catch { }
                    };
                    var miPause = new ToolStripMenuItem("Monitoring pausieren") { CheckOnClick = true };
                    miPause.CheckedChanged += (_, __) =>
                    {
                        try
                        {
                            _drvPaused = miPause.Checked;
                            if (_drvMon != null)
                            {
                                if (_drvPaused) _drvMon.Stop(); else _drvMon.Start();
                            }
                            miPause.Text = _drvPaused ? "Monitoring fortsetzen" : "Monitoring pausieren";
                        }
                        catch { }
                    };
                    cm.Items.Add(miShowSystem);
                    cm.Items.Add(miSortByEvps);
                    cm.Items.Add(new ToolStripSeparator());
                    cm.Items.Add(miCopyCsv);
                    cm.Items.Add(miExportCsv);
                    cm.Items.Add(new ToolStripSeparator());
                    cm.Items.Add(miPause);
                    lvDrivers.ContextMenuStrip = cm;
                }
            }
            catch { }
            LoadProfiles();
            if (App.Instance!.Config.AutoStartAutoSwitch)
            {
                chkAutoSwitch.Checked = true;
                App.Instance.AutoSwitch.Start();
            }
            // UI timer to refresh status
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += (_, __) => RefreshStatus();
            _uiTimer.Start();

            RefreshTrayMenuProfiles();

            // Start minimized to tray if configured (run on Shown to ensure handle exists)
            this.Shown += (_, __) =>
            {
                if (App.Instance!.Config.StartMinimizedToTray)
                {
                    WindowState = FormWindowState.Minimized;
                    Hide();
                    ShowTrayBalloonOnce();
                    UpdateTrayShowHideItemText();
                }
            };

            InitSettingsUI();
            UpdateAdminStatus();

            // Tab selection handler for new forms - lazy load controls
            tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

            // Init monitoring loop
            InitMonitoring();

            // Cleanup monitoring on close
            this.FormClosed += (_, __) =>
            {
                try { _monTimer.Stop(); } catch { }
                try { _monTimer.Dispose(); } catch { }
                try { _cpuTotal?.Dispose(); } catch { }
                try { _diskActiveTime?.Dispose(); } catch { }
                try { _cpuDpcTime?.Dispose(); } catch { }
                try { _cpuIsrTime?.Dispose(); } catch { }
                try { _hw?.Close(); } catch { }
                try { _drvMon?.Dispose(); } catch { }
            };
        }

        private void OnLogMessage(string logLine)
        {
            try
            {
                if (txtLog != null && !txtLog.IsDisposed)
                {
                    if (txtLog.InvokeRequired)
                    {
                        txtLog.Invoke((MethodInvoker)delegate
                        {
                            txtLog.AppendText(logLine + Environment.NewLine);
                            txtLog.SelectionStart = txtLog.Text.Length;
                            txtLog.ScrollToCaret();
                        });
                    }
                    else
                    {
                        txtLog.AppendText(logLine + Environment.NewLine);
                        txtLog.SelectionStart = txtLog.Text.Length;
                        txtLog.ScrollToCaret();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log error: {ex.Message}");
            }
        }

        private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (tabControl.SelectedTab == tabSoftwareManager)
            {
                if (_softwareManagerControl == null || _softwareManagerControl.IsDisposed)
                {
                    try
                    {
                        _softwareManagerControl = new SoftwareManagerControl(App.Instance!.PackageManager, App.Instance!.Logger);
                        tabSoftwareManager.Controls.Clear();
                        tabSoftwareManager.Controls.Add(_softwareManagerControl);
                    }
                    catch (Exception ex)
                    {
                        App.Instance!.Logger.Error("Failed to initialize SoftwareManagerControl", ex);
                    }
                }
            }
            else if (tabControl.SelectedTab == tabTweaks)
            {
                if (_tweaksControl == null || _tweaksControl.IsDisposed)
                {
                    try
                    {
                        _tweaksControl = new TweaksControl(App.Instance!.SystemTweaks, App.Instance!.Logger);
                        tabTweaks.Controls.Clear();
                        tabTweaks.Controls.Add(_tweaksControl);
                    }
                    catch (Exception ex)
                    {
                        App.Instance!.Logger.Error("Failed to initialize TweaksControl", ex);
                    }
                }
            }
        }

        // =====================
        // ListView copy helpers
        // =====================
        private void InitListViewCopy(ListView? lv)
        {
            if (lv == null) return;
            try
            {
                lv.HideSelection = false;
                lv.KeyDown += LvOnKeyDownCopy;
                var cm = new ContextMenuStrip();
                var miCopy = new ToolStripMenuItem("Copy", null, (_, __) => CopyListViewToClipboard(lv, true));
                var miCopyAll = new ToolStripMenuItem("Copy All", null, (_, __) => CopyListViewToClipboard(lv, false));
                cm.Items.Add(miCopy);
                cm.Items.Add(miCopyAll);
                lv.ContextMenuStrip = cm;
            }
            catch { }
        }

        private void LvOnKeyDownCopy(object? sender, KeyEventArgs e)
        {
            try
            {
                if (sender is ListView lv && e.Control && e.KeyCode == Keys.C)
                {
                    CopyListViewToClipboard(lv, true);
                    e.SuppressKeyPress = true;
                }
            }
            catch { }
        }

        private void CopyListViewToClipboard(ListView lv, bool onlySelected)
        {
            try
            {
                var sb = new StringBuilder();
                // headers
                for (int i = 0; i < lv.Columns.Count; i++)
                {
                    sb.Append(lv.Columns[i].Text);
                    if (i < lv.Columns.Count - 1) sb.Append('\t');
                }
                sb.AppendLine();
                // rows
                var items = onlySelected && lv.SelectedItems.Count > 0 ? lv.SelectedItems.Cast<ListViewItem>() : lv.Items.Cast<ListViewItem>();
                foreach (var it in items)
                {
                    for (int i = 0; i < it.SubItems.Count; i++)
                    {
                        sb.Append(it.SubItems[i].Text);
                        if (i < it.SubItems.Count - 1) sb.Append('\t');
                    }
                    sb.AppendLine();
                }
                if (sb.Length == 0) return;
                Clipboard.SetText(sb.ToString());
                if (lblDrvStatus != null && lv == lvDrivers)
                    lblDrvStatus.Text = "Copied to clipboard.";
            }
            catch { }
        }

        // =====================
        // Autostart editor
        // =====================
        private void EditorAutoRefreshList(System.Collections.Generic.IEnumerable<DJWinOptimizer.Core.Models.ProgramAction>? items)
        {
            if (lstAutoStart == null) return;
            lstAutoStart.Items.Clear();
            if (items == null) return;
            foreach (var a in items)
            {
                var lvi = new ListViewItem(new[]
                {
                    a.Path ?? string.Empty,
                    a.Args ?? string.Empty,
                    a.SkipIfRunning ? "Yes" : "No",
                    a.WaitForRunningTimeoutMs.ToString(),
                    a.DelayMsAfterStart.ToString()
                });
                lvi.Tag = a;
                lstAutoStart.Items.Add(lvi);
            }
        }

        private System.Collections.Generic.List<DJWinOptimizer.Core.Models.ProgramAction> EditorAutoCollectItems()
        {
            var list = new System.Collections.Generic.List<DJWinOptimizer.Core.Models.ProgramAction>();
            if (lstAutoStart == null) return list;
            foreach (ListViewItem it in lstAutoStart.Items)
            {
                if (it.Tag is DJWinOptimizer.Core.Models.ProgramAction a)
                {
                    list.Add(a);
                }
                else
                {
                    // Fallback parse from columns
                    var pa = new DJWinOptimizer.Core.Models.ProgramAction
                    {
                        Path = it.SubItems.Count > 0 ? it.SubItems[0].Text : null,
                        Args = it.SubItems.Count > 1 ? it.SubItems[1].Text : null,
                        SkipIfRunning = it.SubItems.Count > 2 && string.Equals(it.SubItems[2].Text, "Yes", StringComparison.OrdinalIgnoreCase),
                    };
                    int val;
                    if (it.SubItems.Count > 3 && int.TryParse(it.SubItems[3].Text, out val)) pa.WaitForRunningTimeoutMs = val;
                    if (it.SubItems.Count > 4 && int.TryParse(it.SubItems[4].Text, out val)) pa.DelayMsAfterStart = val;
                    list.Add(pa);
                }
            }
            return list;
        }

        private void EditorAutoAdd()
        {
            var pa = EditorAutoPrompt(null);
            if (pa == null || lstAutoStart == null) return;
            var lvi = new ListViewItem(new[] { pa.Path ?? string.Empty, pa.Args ?? string.Empty, pa.SkipIfRunning ? "Yes" : "No", pa.WaitForRunningTimeoutMs.ToString(), pa.DelayMsAfterStart.ToString() }) { Tag = pa };
            lstAutoStart.Items.Add(lvi);
        }

        private void EditorAutoEditSelected()
        {
            if (lstAutoStart == null || lstAutoStart.SelectedItems.Count == 0) return;
            var it = lstAutoStart.SelectedItems[0];
            var orig = it.Tag as DJWinOptimizer.Core.Models.ProgramAction;
            var updated = EditorAutoPrompt(orig);
            if (updated == null) return;
            it.SubItems[0].Text = updated.Path ?? string.Empty;
            it.SubItems[1].Text = updated.Args ?? string.Empty;
            it.SubItems[2].Text = updated.SkipIfRunning ? "Yes" : "No";
            it.SubItems[3].Text = updated.WaitForRunningTimeoutMs.ToString();
            it.SubItems[4].Text = updated.DelayMsAfterStart.ToString();
            it.Tag = updated;
        }

        private void EditorAutoRemoveSelected()
        {
            if (lstAutoStart == null) return;
            while (lstAutoStart.SelectedItems.Count > 0)
                lstAutoStart.Items.Remove(lstAutoStart.SelectedItems[0]);
        }

        private void EditorAutoMoveSelected(bool up)
        {
            if (lstAutoStart == null || lstAutoStart.SelectedItems.Count == 0) return;
            var idx = lstAutoStart.SelectedItems[0].Index;
            var newIdx = up ? idx - 1 : idx + 1;
            if (newIdx < 0 || newIdx >= lstAutoStart.Items.Count) return;
            var item = lstAutoStart.Items[idx];
            lstAutoStart.Items.RemoveAt(idx);
            lstAutoStart.Items.Insert(newIdx, item);
            item.Selected = true;
        }

        private DJWinOptimizer.Core.Models.ProgramAction? EditorAutoPrompt(DJWinOptimizer.Core.Models.ProgramAction? original)
        {
            // Lightweight modal editor
            var dlg = new Form
            {
                Text = original == null ? "Add Autostart Item" : "Edit Autostart Item",
                StartPosition = FormStartPosition.CenterParent,
                Size = new System.Drawing.Size(520, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var lblPath = new Label { Text = "Path or script:", AutoSize = true, Location = new System.Drawing.Point(12, 18) };
            var txtPath = new TextBox { Location = new System.Drawing.Point(120, 14), Width = 300 };
            var btnBrowse = new Button { Text = "...", Location = new System.Drawing.Point(430, 12), Size = new System.Drawing.Size(30, 26) };
            btnBrowse.Click += (s, e) => { using var ofd = new OpenFileDialog { Filter = "Executables/Scripts (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|All files (*.*)|*.*" }; if (ofd.ShowDialog(dlg) == DialogResult.OK) txtPath.Text = ofd.FileName; };
            var lblArgs = new Label { Text = "Args:", AutoSize = true, Location = new System.Drawing.Point(12, 52) };
            var txtArgs = new TextBox { Location = new System.Drawing.Point(120, 48), Width = 340 };
            var chkSkip = new CheckBox { Text = "Skip if already running", AutoSize = true, Location = new System.Drawing.Point(120, 80) };
            var lblWait = new Label { Text = "Wait until running (ms):", AutoSize = true, Location = new System.Drawing.Point(12, 110) };
            var numWait = new NumericUpDown { Location = new System.Drawing.Point(180, 108), Minimum = 0, Maximum = 120000, Increment = 100, Width = 80 };
            var lblDelay = new Label { Text = "Delay after start (ms):", AutoSize = true, Location = new System.Drawing.Point(12, 140) };
            var numDelay = new NumericUpDown { Location = new System.Drawing.Point(180, 138), Minimum = 0, Maximum = 600000, Increment = 100, Width = 80 };
            var lblCpn = new Label { Text = "Check Proc Name (opt):", AutoSize = true, Location = new System.Drawing.Point(12, 170) };
            var txtCpn = new TextBox { Location = new System.Drawing.Point(180, 166), Width = 160 };
            var lblCwd = new Label { Text = "Working Dir (opt):", AutoSize = true, Location = new System.Drawing.Point(12, 200) };
            var txtCwd = new TextBox { Location = new System.Drawing.Point(180, 196), Width = 280 };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(320, 230) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(405, 230) };

            if (original != null)
            {
                txtPath.Text = original.Path ?? string.Empty;
                txtArgs.Text = original.Args ?? string.Empty;
                chkSkip.Checked = original.SkipIfRunning;
                numWait.Value = Math.Max(numWait.Minimum, Math.Min(numWait.Maximum, original.WaitForRunningTimeoutMs));
                numDelay.Value = Math.Max(numDelay.Minimum, Math.Min(numDelay.Maximum, original.DelayMsAfterStart));
                txtCpn.Text = original.CheckProcessName ?? string.Empty;
                txtCwd.Text = original.WorkingDirectory ?? string.Empty;
            }

            dlg.Controls.AddRange(new Control[] { lblPath, txtPath, btnBrowse, lblArgs, txtArgs, chkSkip, lblWait, numWait, lblDelay, numDelay, lblCpn, txtCpn, lblCwd, txtCwd, btnOk, btnCancel });

            var result = dlg.ShowDialog(this);
            if (result != DialogResult.OK) { dlg.Dispose(); return null; }
            var pa = original ?? new DJWinOptimizer.Core.Models.ProgramAction();
            pa.Path = string.IsNullOrWhiteSpace(txtPath.Text) ? null : txtPath.Text.Trim();
            pa.Args = string.IsNullOrWhiteSpace(txtArgs.Text) ? null : txtArgs.Text.Trim();
            pa.SkipIfRunning = chkSkip.Checked;
            pa.WaitForRunningTimeoutMs = (int)numWait.Value;
            pa.DelayMsAfterStart = (int)numDelay.Value;
            pa.CheckProcessName = string.IsNullOrWhiteSpace(txtCpn.Text) ? null : txtCpn.Text.Trim();
            pa.WorkingDirectory = string.IsNullOrWhiteSpace(txtCwd.Text) ? null : txtCwd.Text.Trim();
            dlg.Dispose();
            return pa;
        }

        private void TryApplyServiceTagsToEditor(DJWinOptimizer.Core.Models.ServiceToggles svc)
        {
            try
            {
                if (tabEditor == null) return;
                var t = typeof(DJWinOptimizer.Core.Models.ServiceToggles);
                void Walk(System.Windows.Forms.Control.ControlCollection coll)
                {
                    foreach (Control c in coll)
                    {
                        if (c is CheckBox cb && c.Tag is string prop && !string.IsNullOrWhiteSpace(prop))
                        {
                            var pi = t.GetProperty(prop);
                            if (pi != null && pi.PropertyType == typeof(bool))
                            {
                                var val = (bool)(pi.GetValue(svc) ?? false);
                                cb.Checked = val;
                            }
                        }
                        if (c.HasChildren) Walk(c.Controls);
                    }
                }
                Walk(tabEditor.Controls);
            }
            catch { }
        }

        private void TryReadServiceTagsFromEditor(DJWinOptimizer.Core.Models.ServiceToggles svc)
        {
            try
            {
                if (tabEditor == null) return;
                var t = typeof(DJWinOptimizer.Core.Models.ServiceToggles);
                void Walk(System.Windows.Forms.Control.ControlCollection coll)
                {
                    foreach (Control c in coll)
                    {
                        if (c is CheckBox cb && c.Tag is string prop && !string.IsNullOrWhiteSpace(prop))
                        {
                            var pi = t.GetProperty(prop);
                            if (pi != null && pi.PropertyType == typeof(bool))
                            {
                                pi.SetValue(svc, cb.Checked);
                            }
                        }
                        if (c.HasChildren) Walk(c.Controls);
                    }
                }
                Walk(tabEditor.Controls);
            }
            catch { }
        }

        private void InitSettingsUI()
        {
            // Initialize settings checkboxes
            chkStartMinimized.Checked = App.Instance!.Config.StartMinimizedToTray;
            chkAutoStartAutoSwitch.Checked = App.Instance!.Config.AutoStartAutoSwitch;
            chkStartWithWindows.Checked = App.Instance!.Config.StartWithWindows;

            chkStartMinimized.CheckedChanged += (_, __) =>
            {
                App.Instance!.Config.StartMinimizedToTray = chkStartMinimized.Checked;
                DJWinOptimizer.Settings.AppSettings.Save(App.Instance.Config);
            };

            chkAutoStartAutoSwitch.CheckedChanged += (_, __) =>
            {
                App.Instance!.Config.AutoStartAutoSwitch = chkAutoStartAutoSwitch.Checked;
                DJWinOptimizer.Settings.AppSettings.Save(App.Instance.Config);
            };

            chkStartWithWindows.CheckedChanged += (_, __) =>
            {
                App.Instance!.Config.StartWithWindows = chkStartWithWindows.Checked;
                DJWinOptimizer.Settings.AppSettings.Save(App.Instance.Config);
                if (!DJWinOptimizer.Utils.AutostartUtil.TrySetEnabled(chkStartWithWindows.Checked, Application.ExecutablePath, out var err))
                {
                    MessageBox.Show(this, $"Failed to update startup setting: {err}", "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // reflect actual state if error
                    chkStartWithWindows.Checked = DJWinOptimizer.Utils.AutostartUtil.IsEnabled();
                }
            };

            chkAutoSwitch.CheckedChanged += (_, __) =>
            {
                // keep tray item text in sync when checkbox toggles via UI
                UpdateTrayAutoSwitchItemText();
            };

            // Update admin status label initially
            UpdateAdminStatus();

            // Load hotkey text boxes from settings
            txtHKToggle.Text = App.Instance!.Config.Hotkeys.ToggleAutoSwitch;
            txtHKShowHide.Text = App.Instance!.Config.Hotkeys.ShowHideWindow;
            txtHKApplyProfile.Text = App.Instance!.Config.Hotkeys.ApplySelectedProfile;

            // Wire capture handlers so users can press combos directly
            WireHotkeyCapture(txtHKToggle);
            WireHotkeyCapture(txtHKShowHide);
            WireHotkeyCapture(txtHKApplyProfile);

            // Register defaults on startup
            ApplyHotkeySettings();

            // Live parse validation for hotkeys
            txtHKToggle.TextChanged += (_, __) => UpdateHotkeyPreviewStatuses();
            txtHKShowHide.TextChanged += (_, __) => UpdateHotkeyPreviewStatuses();
            txtHKApplyProfile.TextChanged += (_, __) => UpdateHotkeyPreviewStatuses();
            UpdateHotkeyPreviewStatuses();
        }

        private void LoadProfiles()
        {
            listProfiles.Items.Clear();
            foreach (var p in App.Instance!.Profiles.GetAll())
                listProfiles.Items.Add(p.Name);
            RefreshTrayMenuProfiles();
            // Auto-select first profile if none selected and sync editor
            if (listProfiles.Items.Count > 0 && listProfiles.SelectedIndex < 0)
                listProfiles.SelectedIndex = 0;
            OnProfileSelectedChanged();
        }

        private void ApplySelectedProfile()
        {
            if (listProfiles.SelectedItem is string name)
            {
                BeginOperation($"Applying profile: {name}...");
                try
                {
                    if (App.Instance!.Profiles.ApplyProfileByName(name))
                    {
                        statusLabel.Text = $"Active profile: {name}";
                        AppendLog($"Applied profile: {name}");
                        try
                        {
                            var prof = App.Instance!.Profiles.GetByName(name);
                            var tr = App.Instance!.TimerResolution.IsOneMillisecond ? "1ms" : "stock";
                            AppendLog($"Timer resolution -> {tr}");
                            if (prof?.Programs != null)
                            {
                                var l = prof.Programs.LaunchOnEnter?.Count ?? 0;
                                var k = prof.Programs.KillOnExit?.Count ?? 0;
                                AppendLog($"Program actions: Launch {l}, Kill {k}");
                            }
                        }
                        catch { }
                        RefreshStatus();
                        // Verify power plan if specified
                        var p = App.Instance!.Profiles.GetByName(name);
                        if (p != null && !string.IsNullOrWhiteSpace(p.PowerPlanGuid))
                        {
                            var active = App.Instance.PowerPlans.GetActiveGuid();
                            if (!string.Equals(active, p.PowerPlanGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                AppendLog($"Warning: Active power plan did not match requested GUID {p.PowerPlanGuid}. Current active: {active ?? "(unknown)"}");
                                MessageBox.Show(this, "Der Energieplan konnte möglicherweise nicht übernommen werden. Prüfe Administratorrechte und versuche es erneut.",
                                    "Energieplan nicht aktiv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                        EndOperation($"Applied: {name}");
                        TryTrayToast($"Applied profile: {name}");
                    }
                    else
                    {
                        EndOperation($"Apply failed: {name}");
                        MessageBox.Show(this, $"Failed to apply profile '{name}'. See Logs tab for details.", "Apply Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    // Ensure progress stops if any exception bubbles
                }
            }
        }

        // Targets management helpers (OR/AND)
        private void EditorBrowseTarget(bool orList)
        {
            using var ofd = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var file = System.IO.Path.GetFileName(ofd.FileName);
                    if (string.IsNullOrWhiteSpace(file)) return;
                    // Ensure .exe
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file += ".exe";
                    var lst = orList ? lstTargetsAny : lstTargetsAll;
                    var txt = orList ? txtTargetAny : txtTargetAll;
                    if (lst != null)
                    {
                        var exists = lst.Items.Cast<object>().Any(it => string.Equals(it?.ToString(), file, StringComparison.OrdinalIgnoreCase));
                        if (!exists) lst.Items.Add(file);
                    }
                    if (txt != null) txt.Clear();
                }
                catch { }
            }
        }

        private void EditorAddTarget(bool orList)
        {
            var lst = orList ? lstTargetsAny : lstTargetsAll;
            var txt = orList ? txtTargetAny : txtTargetAll;
            if (lst == null || txt == null) return;
            var text = (txt.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            // Normalize to filename only
            try { text = System.IO.Path.GetFileName(text); } catch { }
            // Avoid duplicates
            var exists = lst.Items.Cast<object>().Any(it => string.Equals(it?.ToString(), text, StringComparison.OrdinalIgnoreCase));
            if (!exists) lst.Items.Add(text);
            txt.Clear();
        }

        private void EditorRemoveSelectedTarget(bool orList)
        {
            var lst = orList ? lstTargetsAny : lstTargetsAll;
            if (lst == null) return;
            while (lst.SelectedIndices.Count > 0)
            {
                lst.Items.RemoveAt(lst.SelectedIndices[0]);
            }
        }

        private void EditorAddFromProcesses(bool orList)
        {
            try
            {
                // Build a lightweight modal picker
                var dlg = new Form
                {
                    Text = "Select running processes",
                    StartPosition = FormStartPosition.CenterParent,
                    Size = new System.Drawing.Size(420, 420),
                    MinimizeBox = false,
                    MaximizeBox = false,
                    FormBorderStyle = FormBorderStyle.FixedDialog
                };
                var txtFilter = new TextBox { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new System.Drawing.Point(10, 10), Width = 380 };
                var lst = new ListBox { Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Location = new System.Drawing.Point(10, 40), Size = new System.Drawing.Size(380, 300) };
                var btnOk = new Button { Text = "Add Selected", Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Location = new System.Drawing.Point(210, 350), Width = 180 };
                dlg.Controls.Add(txtFilter);
                dlg.Controls.Add(lst);
                dlg.Controls.Add(btnOk);

                // Load running process names (file names)
                var names = System.Diagnostics.Process.GetProcesses()
                    .Select(p => {
                        try { return System.IO.Path.GetFileName(p.MainModule?.FileName ?? string.Empty); } catch { return p.ProcessName + ".exe"; }
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : (s + ".exe"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                void RefreshList()
                {
                    var f = (txtFilter.Text ?? string.Empty).Trim();
                    lst.BeginUpdate();
                    lst.Items.Clear();
                    IEnumerable<string> q = names;
                    if (!string.IsNullOrEmpty(f)) q = q.Where(n => n.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                    foreach (var n in q) lst.Items.Add(n);
                    lst.EndUpdate();
                }

                txtFilter.TextChanged += (s, e) => RefreshList();
                lst.DoubleClick += (s, e) => btnOk.PerformClick();
                btnOk.Click += (s, e) => dlg.DialogResult = DialogResult.OK;

                RefreshList();
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var selected = lst.SelectedItems.Cast<object>().Select(o => o?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
                    if (selected.Count == 0 && lst.SelectedItem is object so && !string.IsNullOrWhiteSpace(so.ToString()))
                        selected = new[] { so.ToString()! }.ToList();
                    var targetList = orList ? lstTargetsAny : lstTargetsAll;
                    if (selected.Count > 0 && targetList != null)
                    {
                        foreach (var s in selected)
                        {
                            var exists = targetList.Items.Cast<object>().Any(it => string.Equals(it?.ToString(), s, StringComparison.OrdinalIgnoreCase));
                            if (!exists) targetList.Items.Add(s);
                        }
                    }
                }
                dlg.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fehler beim Lesen der Prozesse: " + ex.Message, "Prozesse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void NewProfile()
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox("Profile name:", "New Profile", "Custom Profile");
            if (!string.IsNullOrWhiteSpace(name))
            {
                App.Instance!.Profiles.Create(name);
                LoadProfiles();
            }
        }

        private void DeleteProfile()
        {
            if (listProfiles.SelectedItem is string name)
            {
                if (MessageBox.Show($"Delete profile '{name}'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    App.Instance!.Profiles.Delete(name);
                    LoadProfiles();
                }
            }
        }

        private void EditProfile()
        {
            if (listProfiles.SelectedItem is not string name) return;
            var p = App.Instance!.Profiles.GetByName(name);
            if (p == null) return;
            using var dlg = new ProfileEditorForm(p);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                App.Instance!.Profiles.Save(dlg.ResultProfile);
                LoadProfiles();
                AppendLog($"Edited profile: {dlg.ResultProfile.Name}");
            }
        }

        private void ImportProfile()
        {
            using var ofd = new OpenFileDialog { Filter = "Profile JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                var p = App.Instance!.Profiles.Import(ofd.FileName);
                if (p != null)
                {
                    LoadProfiles();
                    AppendLog($"Imported profile: {p.Name}");
                }
                else
                {
                    MessageBox.Show("Import failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportProfile()
        {
            if (listProfiles.SelectedItem is not string name) return;
            using var sfd = new SaveFileDialog { Filter = "Profile JSON (*.json)|*.json", FileName = name + ".json" };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                if (App.Instance!.Profiles.Export(name, sfd.FileName))
                {
                    AppendLog($"Exported profile: {name} -> {sfd.FileName}");
                }
                else
                {
                    MessageBox.Show("Export failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ToggleAutoSwitch()
        {
            if (chkAutoSwitch.Checked) App.Instance!.AutoSwitch.Start(); else App.Instance!.AutoSwitch.Stop();
            UpdateTrayAutoSwitchItemText();
        }

        private void AppendLog(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }

        private void RefreshStatus()
        {
            var active = App.Instance!.Profiles.ActiveProfile?.Name ?? "-";
            var adminNote = AdminUtil.IsAdministrator() ? string.Empty : " (non-admin: limited toggles)";
            var tr = App.Instance!.TimerResolution.IsOneMillisecond ? "1ms" : "stock";
            if (statusLabel != null)
                statusLabel.Text = $"Active profile: {active}{adminNote} | TimerRes: {tr}";
            var trig = App.Instance!.AutoSwitch.LastTrigger;
            if (!string.IsNullOrWhiteSpace(trig) && lblLastTrigger != null)
                lblLastTrigger.Text = $"Last trigger: {trig}";
            // keep admin label in sync
            UpdateAdminStatus();
            // Update active plan label in editor if present
            try
            {
                var guid = App.Instance.PowerPlans.GetActiveGuid();
                if (lblActivePlan != null)
                {
                    lblActivePlan.Text = guid != null ? $"Active: {guid}" : "Active: -";
                }
            }
            catch { }

            // Ensure Driver Latencies list shows diagnostics and keeps it updated even if ETW hasn't updated yet
            try
            {
                if (lvDrivers != null && !lvDrivers.IsDisposed)
                {
                    lvDrivers.BeginUpdate();
                    var isAdminU = AdminUtil.IsAdministrator();
                    long totalEv = 0, matchedEv = 0;
                    try { if (_drvMon != null) { totalEv = _drvMon.TotalKernelEvents; matchedEv = _drvMon.MatchedDpcIsrEvents; } } catch { }

                    if (lvDrivers.Items.Count == 0)
                    {
                        var lastErr = _drvMon?.LastError;
                        var errPart = !string.IsNullOrWhiteSpace(lastErr) ? $" | Error: {lastErr}" : string.Empty;
                        var itDiagU = new ListViewItem($"(diagnostics) Admin={isAdminU} | Waiting for ETW updates... ETW events: {totalEv}, matched: {matchedEv}{errPart}");
                        itDiagU.SubItems.Add("-");
                        itDiagU.SubItems.Add("-");
                        itDiagU.SubItems.Add("-");
                        lvDrivers.Items.Add(itDiagU);
                    }
                    else
                    {
                        // If the list only contains the initial placeholder/diagnostics, refresh it with current counters
                        if (lvDrivers.Items.Count == 1)
                        {
                            var txt = lvDrivers.Items[0].Text ?? string.Empty;
                            if (txt.StartsWith("(diagnostics)") || txt.IndexOf("Initializing ETW monitor", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                lvDrivers.Items[0].Text = $"(diagnostics) Admin={isAdminU} | ETW events: {totalEv}, matched: {matchedEv}";
                            }
                        }
                        else
                        {
                            // Ensure last row is a diagnostics line and keep it updated
                            var last = lvDrivers.Items[lvDrivers.Items.Count - 1];
                            var lastText = last.Text ?? string.Empty;
                            if (lastText.StartsWith("(diagnostics)"))
                            {
                                var lastErr = _drvMon?.LastError;
                                var errPart = !string.IsNullOrWhiteSpace(lastErr) ? $" | Error: {lastErr}" : string.Empty;
                                last.Text = $"(diagnostics) Admin={isAdminU} | ETW events: {totalEv}, matched: {matchedEv}{errPart}";
                            }
                        }
                    }
                    lvDrivers.EndUpdate();
                }
            }
            catch { }
            // Update status label even if ETW hasn't fired UI updates yet
            try
            {
                if (lblDrvStatus != null && _drvMon != null)
                {
                    var total = _drvMon.TotalKernelEvents;
                    var matched = _drvMon.MatchedDpcIsrEvents;
                    var lastErr = _drvMon.LastError;
                    var errPart = !string.IsNullOrWhiteSpace(lastErr) ? $" | Error: {lastErr}" : string.Empty;
                    var now = DateTime.UtcNow;
                    double rateTot = 0, rateMat = 0;
                    if (_prevEtwTs != DateTime.MinValue)
                    {
                        var dt = (now - _prevEtwTs).TotalSeconds;
                        if (dt > 0)
                        {
                            rateTot = (total - _prevEtwTotal) / dt;
                            rateMat = (matched - _prevEtwMatched) / dt;
                        }
                    }
                    _prevEtwTs = now;
                    _prevEtwTotal = total;
                    _prevEtwMatched = matched;
                    lblDrvStatus.Text = AdminUtil.IsAdministrator()
                        ? $"Driver latencies: ETW running. ETW events: {total} (+{rateTot:0.0}/s), matched: {matched} (+{rateMat:0.0}/s){errPart}"
                        : "Driver latencies require Administrator. Please run as Admin.";
                }
            }
            catch { }
        }

        // =====================
        // Monitoring (System tab)
        // =====================
        private enum Sev { Normal, Warn, Crit }

        private void InitMonitoring()
        {
            try
            {
                // Create PerformanceCounters with localization fallbacks. First read returns 0, so prime them.
                _cpuTotal = TryCreateCounter(
                    new[] { "Processor", "Prozessor" },
                    new[] { "% Processor Time", "% Prozessorzeit" },
                    "_Total");
                if (_cpuTotal != null) _ = _cpuTotal.NextValue();

                _diskActiveTime = TryCreateCounter(
                    new[] { "PhysicalDisk", "Physikalischer Datenträger" },
                    new[] { "% Disk Time", "% Datenträgerzeit" },
                    "_Total");
                if (_diskActiveTime != null) _ = _diskActiveTime.NextValue();

                // DPC/ISR percentages (per _Total)
                _cpuDpcTime = TryCreateCounter(
                    new[] { "Processor", "Prozessor", "Processor Information", "Prozessorinformationen" },
                    new[] { "% DPC Time", "% DPC-Zeit" },
                    "_Total");
                if (_cpuDpcTime != null) _ = _cpuDpcTime.NextValue();

                _cpuIsrTime = TryCreateCounter(
                    new[] { "Processor", "Prozessor", "Processor Information", "Prozessorinformationen" },
                    new[] { "% Interrupt Time", "% Interruptzeit", "% Unterbrechungszeit" },
                    "_Total");
                if (_cpuIsrTime != null) _ = _cpuIsrTime.NextValue();
                _perfReady = _cpuDpcTime != null && _cpuIsrTime != null;
            }
            catch
            {
                _perfReady = false;
            }

            // Initialize hardware sensors (best-effort)
            try
            {
                _hw = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true
                };
                _hw.Open();
            }
            catch
            {
                try { _hw?.Close(); } catch { }
                _hw = null;
            }

            _monTimer.Interval = 1000;
            _monTimer.Tick += (_, __) => UpdateMonitoring();
            _monTimer.Start();

            // Start ETW-based driver latency monitor (best-effort)
            try
            {
                var mon = App.Instance?.Config?.Monitoring;
                int refreshMs = mon?.DriverLatencyRefreshMs > 0 ? mon!.DriverLatencyRefreshMs : 1000;
                bool includeSystemRows = mon?.ShowSystemEtwRows ?? true;
                _drvMon = new DriverLatencyMonitor(refreshMs, includeSystemRows);
                // Subscribe BEFORE starting to avoid missing early updates
                _drvMon.OnUpdate += entries =>
                {
                    if (lvDrivers == null) return;
                    if (lvDrivers.IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (lvDrivers == null) return;
                                lvDrivers.BeginUpdate();
                                lvDrivers.Items.Clear();
                                var nowTs = DateTime.UtcNow;
                                var monCfg = App.Instance?.Config?.Monitoring;
                                bool showSystem = monCfg?.ShowSystemEtwRows ?? true;
                                foreach (var e in entries)
                                {
                                    // Determine if this entry looks like a driver-related offender
                                    var name = e.Name ?? string.Empty;
                                    bool looksDriver = name.Equals("DPC", StringComparison.OrdinalIgnoreCase)
                                                       || name.Equals("Interrupt", StringComparison.OrdinalIgnoreCase)
                                                       || name.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)
                                                       || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                                    // Optionally filter out generic system ETW rows
                                    if (!showSystem && !looksDriver) continue;
                                    var it = new ListViewItem(e.Name);
                                    it.SubItems.Add(e.DpcMs.ToString("0.0"));
                                    it.SubItems.Add(e.IsrMs.ToString("0.0"));
                                    it.SubItems.Add(e.Events.ToString());
                                    // Events per second
                                    double evps = 0;
                                    try
                                    {
                                        if (_prevDriverCounts.TryGetValue(e.Name, out var prev))
                                        {
                                            var dt = (nowTs - prev.Ts).TotalSeconds;
                                            if (dt > 0) evps = Math.Max(0, (e.Events - prev.Count) / dt);
                                        }
                                    }
                                    catch { }
                                    it.SubItems.Add(evps.ToString("0.0"));
                                    // Row coloring thresholds based on Events/s for likely driver offenders
                                    try
                                    {
                                        if (looksDriver)
                                        {
                                            var mon = App.Instance?.Config?.Monitoring;
                                            float warn = mon?.DriverEventsWarn ?? 200f;
                                            float crit = mon?.DriverEventsCrit ?? 1000f;
                                            if (evps >= crit) it.ForeColor = Color.Red;
                                            else if (evps >= warn) it.ForeColor = Color.DarkOrange;
                                        }
                                    }
                                    catch { }
                                    lvDrivers.Items.Add(it);
                                    _prevDriverCounts[e.Name] = (e.Events, nowTs);
                                }
                                // Always append totals via PerformanceCounters so users see meaningful metrics
                                if (_perfReady && _cpuDpcTime != null && _cpuIsrTime != null)
                                {
                                    try
                                    {
                                        var dpcPct = Math.Max(0f, Math.Min(100f, _cpuDpcTime.NextValue()));
                                        var isrPct = Math.Max(0f, Math.Min(100f, _cpuIsrTime.NextValue()));
                                        var itDpc = new ListViewItem("DPC (total %)");
                                        itDpc.SubItems.Add(dpcPct.ToString("0.0"));
                                        itDpc.SubItems.Add("0.0");
                                        itDpc.SubItems.Add("-");
                                        itDpc.SubItems.Add("-");
                                        // Color by threshold
                                        try
                                        {
                                            var sev = Eval(dpcPct, App.Instance?.Config?.Monitoring?.DpcWarn ?? DpcWarn, App.Instance?.Config?.Monitoring?.DpcCrit ?? DpcCrit);
                                            if (sev == Sev.Crit) itDpc.ForeColor = Color.Red; else if (sev == Sev.Warn) itDpc.ForeColor = Color.DarkOrange;
                                        }
                                        catch { }
                                        lvDrivers.Items.Add(itDpc);
                                        var itIsr = new ListViewItem("Interrupt (total %)");
                                        itIsr.SubItems.Add("0.0");
                                        itIsr.SubItems.Add(isrPct.ToString("0.0"));
                                        itIsr.SubItems.Add("-");
                                        itIsr.SubItems.Add("-");
                                        try
                                        {
                                            var sevI = Eval(isrPct, App.Instance?.Config?.Monitoring?.IsrWarn ?? IsrWarn, App.Instance?.Config?.Monitoring?.IsrCrit ?? IsrCrit);
                                            if (sevI == Sev.Crit) itIsr.ForeColor = Color.Red; else if (sevI == Sev.Warn) itIsr.ForeColor = Color.DarkOrange;
                                        }
                                        catch { }
                                        lvDrivers.Items.Add(itIsr);
                                    }
                                    catch { }
                                }
                                // Always surface diagnostics inline so it's visible without a separate status label
                                {
                                    var isAdmin = AdminUtil.IsAdministrator();
                                    var total = _drvMon.TotalKernelEvents;
                                    var matched = _drvMon.MatchedDpcIsrEvents;
                                    var now = DateTime.UtcNow;
                                    double rateTot = 0, rateMat = 0;
                                    if (_prevEtwTs != DateTime.MinValue)
                                    {
                                        var dt = (now - _prevEtwTs).TotalSeconds;
                                        if (dt > 0)
                                        {
                                            rateTot = (total - _prevEtwTotal) / dt;
                                            rateMat = (matched - _prevEtwMatched) / dt;
                                        }
                                    }
                                    _prevEtwTs = now;
                                    _prevEtwTotal = total;
                                    _prevEtwMatched = matched;
                                    var samples = _drvMon.GetRecentEventSamples(3);
                                    var sampleText = samples != null && samples.Length > 0 ? string.Join(" | ", samples) : "-";
                                    string counterInfo = string.Empty;
                                    try
                                    {
                                        float dpcNow = float.NaN, isrNow = float.NaN;
                                        if (_cpuDpcTime != null) try { dpcNow = _cpuDpcTime.NextValue(); } catch { }
                                        if (_cpuIsrTime != null) try { isrNow = _cpuIsrTime.NextValue(); } catch { }
                                        if (_cpuDpcTime != null || _cpuIsrTime != null)
                                        {
                                            var dpcName = _cpuDpcTime != null ? $"{_cpuDpcTime.CategoryName}/{_cpuDpcTime.CounterName}={dpcNow:0.0}%" : "n/a";
                                            var isrName = _cpuIsrTime != null ? $"{_cpuIsrTime.CategoryName}/{_cpuIsrTime.CounterName}={isrNow:0.0}%" : "n/a";
                                            counterInfo = $" | Counters: DPC={dpcName}, ISR={isrName}";
                                        }
                                    }
                                    catch { }
                                    var itDiag = new ListViewItem($"(diagnostics) Admin={isAdmin} | ETW events: {total} (+{rateTot:0.0}/s), matched: {matched} (+{rateMat:0.0}/s){counterInfo} | Samples: {sampleText}");
                                    itDiag.SubItems.Add("-");
                                    itDiag.SubItems.Add("-");
                                    itDiag.SubItems.Add("-");
                                    itDiag.SubItems.Add("-");
                                    lvDrivers.Items.Add(itDiag);
                                }
                                lvDrivers.EndUpdate();
                                try { lvDrivers.Sort(); } catch { }
                                if (lblDrvStatus != null)
                                {
                                    var total = _drvMon.TotalKernelEvents;
                                    var matched = _drvMon.MatchedDpcIsrEvents;
                                    if (entries.Count == 0)
                                    {
                                        if (AdminUtil.IsAdministrator())
                                        {
                                            var samples = _drvMon.GetRecentEventSamples(5);
                                            var sampleText = samples != null && samples.Length > 0 ? string.Join(" | ", samples) : "-";
                                            lblDrvStatus.Text = $"No driver latency activity observed yet... ETW events: {total}, matched DPC/ISR: {matched}. Samples: {sampleText}";
                                        }
                                        else
                                        {
                                            lblDrvStatus.Text = "Driver latencies require Administrator. Please run as Admin.";
                                        }
                                    }
                                    else
                                    {
                                        lblDrvStatus.Text = $"Showing {entries.Count} items. ETW events: {total}, matched DPC/ISR: {matched}";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                try { if (lblDrvStatus != null) lblDrvStatus.Text = $"Driver latencies UI error: {ex.Message}"; } catch { }
                            }
                        }));
                    }
                    catch { }
                };
                bool started = _drvMon.Start();
                if (lblDrvStatus != null)
                {
                    if (!AdminUtil.IsAdministrator())
                        lblDrvStatus.Text = "Driver latencies require Administrator. Please run as Admin.";
                    else
                        lblDrvStatus.Text = started ? "Driver latencies: ETW running..." : "Driver latencies: ETW not started.";
                }
                // Add a placeholder diagnostics row immediately so the user sees guidance even before first ETW tick
                try
                {
                    if (lvDrivers != null && !lvDrivers.IsDisposed)
                    {
                        lvDrivers.BeginUpdate();
                        lvDrivers.Items.Clear();
                        var isAdmin0 = AdminUtil.IsAdministrator();
                        var itDiag0 = new ListViewItem($"(diagnostics) Admin={isAdmin0} | Initializing ETW monitor...");
                        itDiag0.SubItems.Add("-");
                        itDiag0.SubItems.Add("-");
                        itDiag0.SubItems.Add("-");
                        lvDrivers.Items.Add(itDiag0);
                        lvDrivers.EndUpdate();
                    }
                }
                catch { }
            }
            catch
            {
                if (lblDrvStatus != null)
                    lblDrvStatus.Text = "Driver latency monitor failed to start.";
            }
        }

        private void UpdateMonitoring()
        {
            if (tabMonSystem == null) return;
            float cpu = float.NaN, disk = float.NaN;
            float cpuTemp = float.NaN, gpuTemp = float.NaN, gpuLoad = float.NaN;
            float dpc = float.NaN, isr = float.NaN;
            if (_perfReady)
            {
                try { cpu = _cpuTotal?.NextValue() ?? float.NaN; } catch { cpu = float.NaN; }
                try { disk = _diskActiveTime?.NextValue() ?? float.NaN; } catch { disk = float.NaN; }
                try { dpc = _cpuDpcTime?.NextValue() ?? float.NaN; } catch { dpc = float.NaN; }
                try { isr = _cpuIsrTime?.NextValue() ?? float.NaN; } catch { isr = float.NaN; }
            }

            // Poll temps and GPU load
            try
            {
                if (_hw != null)
                {
                    foreach (var hw in _hw.Hardware)
                    {
                        try { hw.Update(); } catch { }
                        // CPU temperature: take max temp sensor
                        if (hw.HardwareType == HardwareType.Cpu)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    if (s.Value.HasValue)
                                        cpuTemp = float.IsNaN(cpuTemp) ? s.Value.Value : Math.Max(cpuTemp, s.Value.Value);
                                }
                            }
                        }
                        // GPU load and temperature
                        if (hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuIntel)
                        {
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                    gpuTemp = float.IsNaN(gpuTemp) ? s.Value.Value : Math.Max(gpuTemp, s.Value.Value);
                                if (s.SensorType == SensorType.Load && s.Value.HasValue)
                                {
                                    var name = s.Name ?? string.Empty;
                                    // Prefer total/core load sensors
                                    if (name.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("gpu", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        gpuLoad = float.IsNaN(gpuLoad) ? s.Value.Value : Math.Max(gpuLoad, s.Value.Value);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Update labels (if present)
            try
            {
                if (lblCpuUtil != null) lblCpuUtil.Text = float.IsNaN(cpu) ? "n/a" : $"{cpu:0}%";
                if (lblDiskUtil != null) lblDiskUtil.Text = float.IsNaN(disk) ? "n/a" : $"{disk:0}%";
                if (lblCpuTemp != null) lblCpuTemp.Text = float.IsNaN(cpuTemp) ? "n/a" : $"{cpuTemp:0}°C";
                if (lblGpuTemp != null) lblGpuTemp.Text = float.IsNaN(gpuTemp) ? "n/a" : $"{gpuTemp:0}°C";
                if (lblGpuUtil != null) lblGpuUtil.Text = float.IsNaN(gpuLoad) ? "n/a" : $"{gpuLoad:0}%";
                if (lblDpcPct != null) lblDpcPct.Text = float.IsNaN(dpc) ? "n/a" : $"{dpc:0.0}%";
                if (lblIsrPct != null) lblIsrPct.Text = float.IsNaN(isr) ? "n/a" : $"{isr:0.0}%";
            }
            catch { }

            // Evaluate severity using configured thresholds (fallback to defaults)
            var m = App.Instance?.Config?.Monitoring;
            float cpuWarn = m?.CpuWarn ?? CpuWarn, cpuCrit = m?.CpuCrit ?? CpuCrit;
            float diskWarn = m?.DiskWarn ?? DiskWarn, diskCrit = m?.DiskCrit ?? DiskCrit;
            float cpuTempWarn = m?.CpuTempWarn ?? CpuTempWarn, cpuTempCrit = m?.CpuTempCrit ?? CpuTempCrit;
            float gpuTempWarn = m?.GpuTempWarn ?? GpuTempWarn, gpuTempCrit = m?.GpuTempCrit ?? GpuTempCrit;
            float dpcWarn = m?.DpcWarn ?? DpcWarn, dpcCrit = m?.DpcCrit ?? DpcCrit;
            float isrWarn = m?.IsrWarn ?? IsrWarn, isrCrit = m?.IsrCrit ?? IsrCrit;

            var sev = MaxSev(
                Eval(cpu, cpuWarn, cpuCrit),
                Eval(disk, diskWarn, diskCrit),
                Eval(cpuTemp, cpuTempWarn, cpuTempCrit),
                Eval(gpuTemp, gpuTempWarn, gpuTempCrit)
            );
            ApplyTabSeverity(tabMonSystem, sev);
            // Driver tab severity from DPC/ISR
            var drvSev = MaxSev(Eval(dpc, dpcWarn, dpcCrit), Eval(isr, isrWarn, isrCrit));
            if (tabMonDrivers != null) ApplyTabSeverity(tabMonDrivers, drvSev);
            if (sev != _lastMonSev)
            {
                try
                {
                    var msg = sev switch
                    {
                        Sev.Normal => "Monitoring back to normal",
                        Sev.Warn => "Monitoring WARN: high load detected",
                        Sev.Crit => "Monitoring HOT: critical load detected",
                        _ => null
                    };
                    if (!string.IsNullOrEmpty(msg)) AppendLog(msg);
                }
                catch { }
                _lastMonSev = sev;
            }
        }

        private static Sev Eval(float val, float warn, float crit)
        {
            if (float.IsNaN(val)) return Sev.Normal;
            if (val >= crit) return Sev.Crit;
            if (val >= warn) return Sev.Warn;
            return Sev.Normal;
        }

        private static Sev MaxSev(params Sev[] s)
        {
            return s.Any(x => x == Sev.Crit) ? Sev.Crit : (s.Any(x => x == Sev.Warn) ? Sev.Warn : Sev.Normal);
        }

        private static PerformanceCounter? TryCreateCounter(string[] categoryCandidates, string[] counterCandidates, string instanceName)
        {
            // Try exact candidates first
            foreach (var cat in categoryCandidates)
            {
                foreach (var ctr in counterCandidates)
                {
                    try { return new PerformanceCounter(cat, ctr, instanceName, true); }
                    catch { }
                }
            }
            // Heuristic scan across installed categories to find best match by contains/starts-with
            try
            {
                foreach (var cat in PerformanceCounterCategory.GetCategories())
                {
                    var catName = cat.CategoryName ?? string.Empty;
                    bool catMatch = categoryCandidates.Any(c => catName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!catMatch) continue;
                    string[] instances;
                    try { instances = cat.GetInstanceNames(); } catch { instances = Array.Empty<string>(); }
                    // Build preferred instance order: requested instance, _Total, contains Total, then each instance
                    var prefInstances = new List<string>();
                    if (!string.IsNullOrEmpty(instanceName)) prefInstances.Add(instanceName);
                    if (instances.Length > 0)
                    {
                        var totalExact = instances.FirstOrDefault(n => n.Equals("_Total", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(totalExact) && !prefInstances.Contains(totalExact)) prefInstances.Add(totalExact);
                        var totalLike = instances.FirstOrDefault(n => n.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!string.IsNullOrEmpty(totalLike) && !prefInstances.Contains(totalLike)) prefInstances.Add(totalLike);
                        foreach (var inst in instances)
                            if (!prefInstances.Contains(inst)) prefInstances.Add(inst);
                    }
                    if (prefInstances.Count == 0) prefInstances.Add(""); // some categories are single-instance

                    foreach (var inst in prefInstances)
                    {
                        CounterCreationData[] dummy = null;
                        PerformanceCounter[] counters;
                        try { counters = cat.GetCounters(inst); }
                        catch { continue; }
                        foreach (var c in counters)
                        {
                            var nm = c.CounterName ?? string.Empty;
                            bool ctrMatch = counterCandidates.Any(k => nm.IndexOf(k.Trim('%').Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                                            || nm.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0
                                            || nm.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                                            || nm.IndexOf("Unterbrechung", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (ctrMatch)
                            {
                                try { return new PerformanceCounter(catName, c.CounterName, inst, true); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            // Broad fallback: scan all categories for any DPC/Interrupt counters
            try
            {
                foreach (var cat in PerformanceCounterCategory.GetCategories())
                {
                    string[] instances;
                    try { instances = cat.GetInstanceNames(); } catch { instances = Array.Empty<string>(); }
                    if (instances.Length == 0) instances = new[] { string.Empty };
                    foreach (var inst in instances)
                    {
                        PerformanceCounter[] counters;
                        try { counters = cat.GetCounters(inst); } catch { continue; }
                        foreach (var c in counters)
                        {
                            var nm = c.CounterName ?? string.Empty;
                            if (nm.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0
                                || nm.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                                || nm.IndexOf("Unterbrechung", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try { return new PerformanceCounter(cat.CategoryName, c.CounterName, inst, true); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void ApplyTabSeverity(TabPage tab, Sev sev)
        {
            try
            {
                // Colorize header via text suffix and label forecolors for visibility
                string baseText = tab.Text;
                baseText = baseText.Replace(" (WARN)", string.Empty).Replace(" (HOT)", string.Empty);
                Color fore = SystemColors.ControlText;
                switch (sev)
                {
                    case Sev.Warn:
                        tab.Text = baseText + " (WARN)";
                        fore = Color.DarkOrange;
                        break;
                    case Sev.Crit:
                        tab.Text = baseText + " (HOT)";
                        fore = Color.Red;
                        break;
                    default:
                        tab.Text = baseText;
                        break;
                }
                // Apply to value labels in this tab
                if (lblCpuUtil != null) lblCpuUtil.ForeColor = fore;
                if (lblDiskUtil != null) lblDiskUtil.ForeColor = fore;
            }
            catch { }
        }

        private void UpdateAdminStatus()
        {
            var active = App.Instance!.Profiles.ActiveProfile?.Name ?? "-";
            var adminNote = AdminUtil.IsAdministrator() ? string.Empty : " (non-admin: limited toggles)";
            var tr = App.Instance!.TimerResolution.IsOneMillisecond ? "1ms" : "stock";
            statusLabel.Text = $"Active profile: {active}{adminNote} | TimerRes: {tr}";
            if (AdminUtil.IsAdministrator())
            {
                lblAdminStatus.Text = "Admin status: Elevated";
                if (btnRelaunchAdmin != null) btnRelaunchAdmin.Enabled = false;
            }
            else
            {
                lblAdminStatus.Text = "Admin status: Standard (some toggles require elevation)";
                if (btnRelaunchAdmin != null) btnRelaunchAdmin.Enabled = true;
            }
        }

        private void RelaunchAsAdministrator()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--elevated",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                // Start elevated instance
                Process.Start(psi);
                // Hide UI and tray then exit to release mutex ASAP
                try { trayIcon.Visible = false; } catch {}
                Hide();
                // Trigger app shutdown (stops engines, disposes hotkeys)
                try { App.Instance?.Shutdown(); } catch {}
                // Close quickly
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to relaunch as Administrator: {ex.Message}", "Elevation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshTrayMenuProfiles()
        {
            // Keep first two static items (Show/Quit) at the end, insert profile list above them
            trayMenu.Items.Clear();
            foreach (var p in App.Instance!.Profiles.GetAll())
            {
                var item = new ToolStripMenuItem(p.Name);
                item.Click += (_, __) =>
                {
                    BeginOperation($"Applying profile: {p.Name}...");
                    if (App.Instance!.Profiles.ApplyProfileByName(p.Name))
                    {
                        statusLabel.Text = $"Active profile: {p.Name}";
                        AppendLog($"Applied profile via tray: {p.Name}");
                        try
                        {
                            var tr = App.Instance!.TimerResolution.IsOneMillisecond ? "1ms" : "stock";
                            AppendLog($"Timer resolution -> {tr}");
                            if (p?.Programs != null)
                            {
                                var l = p.Programs.LaunchOnEnter?.Count ?? 0;
                                var k = p.Programs.KillOnExit?.Count ?? 0;
                                AppendLog($"Program actions: Launch {l}, Kill {k}");
                            }
                        }
                        catch { }
                        RefreshStatus();
                        // Verify power plan if specified
                        if (!string.IsNullOrWhiteSpace(p.PowerPlanGuid))
                        {
                            var active = App.Instance.PowerPlans.GetActiveGuid();
                            if (!string.Equals(active, p.PowerPlanGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                AppendLog($"Warning: Active power plan did not match requested GUID {p.PowerPlanGuid}. Current active: {active ?? "(unknown)"}");
                                TryTrayToast("Power plan may not have been applied. Try running as Administrator.");
                            }
                        }
                        EndOperation($"Applied: {p.Name}");
                        TryTrayToast($"Applied profile: {p.Name}");
                    }
                    else
                    {
                        EndOperation($"Apply failed: {p.Name}");
                        TryTrayToast($"Apply failed: {p.Name}");
                    }
                };
                trayMenu.Items.Add(item);
            }
            trayMenu.Items.Add(new ToolStripSeparator());
            // Auto-Switch toggle item
            _trayAutoSwitchItem = new ToolStripMenuItem();
            _trayAutoSwitchItem.Click += (_, __) =>
            {
                chkAutoSwitch.Checked = !chkAutoSwitch.Checked;
                ToggleAutoSwitch();
            };
            UpdateTrayAutoSwitchItemText();
            trayMenu.Items.Add(_trayAutoSwitchItem);

            trayMenu.Items.Add(new ToolStripSeparator());
            _trayShowHideItem = new ToolStripMenuItem();
            _trayShowHideItem.Click += (s, e) =>
            {
                if (Visible && WindowState != FormWindowState.Minimized)
                {
                    Hide();
                }
                else
                {
                    Show();
                    WindowState = FormWindowState.Normal;
                    Activate();
                }
                UpdateTrayShowHideItemText();
            };
            UpdateTrayShowHideItemText();
            trayMenu.Items.Add(_trayShowHideItem);
            trayMenu.Items.Add("Restart as Administrator", null, (s, e) => RelaunchAsAdministrator());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApp());
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowTrayBalloonOnce();
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try { trayIcon.Visible = false; } catch { }
            try { App.Instance?.Shutdown(); } catch { }
        }

        private void ExitApp()
        {
            try { trayIcon.Visible = false; } catch { }
            try { Hide(); } catch { }
            try { App.Instance?.Shutdown(); } catch { }
            try { Application.Exit(); } catch { }
        }

        // Hotkeys
        private const int HK_ID_TOGGLE = 1;
        private const int HK_ID_SHOWHIDE = 2;
        private const int HK_ID_APPLY = 3;

        private void ApplyHotkeySettings()
        {
            // Unregister previous
            App.Instance!.Hotkeys.UnregisterAll();

            // Save current textbox values
            App.Instance.Config.Hotkeys.ToggleAutoSwitch = txtHKToggle.Text.Trim();
            App.Instance.Config.Hotkeys.ShowHideWindow = txtHKShowHide.Text.Trim();
            App.Instance.Config.Hotkeys.ApplySelectedProfile = txtHKApplyProfile.Text.Trim();
            DJWinOptimizer.Settings.AppSettings.Save(App.Instance.Config);

            // Register each if parsable
            var failures = new System.Text.StringBuilder();

            bool regToggle = false;
            if (TryParseHotkey(txtHKToggle.Text, out var m1, out var k1))
            {
                regToggle = App.Instance.Hotkeys.Register(HK_ID_TOGGLE, m1, k1, () =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        chkAutoSwitch.Checked = !chkAutoSwitch.Checked;
                        ToggleAutoSwitch();
                    }));
                });
                if (!regToggle) failures.AppendLine("- Toggle Auto-Switch hotkey failed to register.");
            }
            SetHotkeyRegistrationStatus(lblHKToggleStatus, TryParseHotkey(txtHKToggle.Text, out _, out _) ? regToggle : (bool?)false);

            bool regShowHide = false;
            if (TryParseHotkey(txtHKShowHide.Text, out var m2, out var k2))
            {
                regShowHide = App.Instance.Hotkeys.Register(HK_ID_SHOWHIDE, m2, k2, () =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (Visible && WindowState != FormWindowState.Minimized)
                        {
                            Hide();
                        }
                        else
                        {
                            Show();
                            WindowState = FormWindowState.Normal;
                            Activate();
                        }
                        UpdateTrayShowHideItemText();
                    }));
                });
                if (!regShowHide) failures.AppendLine("- Show/Hide Window hotkey failed to register.");
            }
            SetHotkeyRegistrationStatus(lblHKShowHideStatus, TryParseHotkey(txtHKShowHide.Text, out _, out _) ? regShowHide : (bool?)false);

            bool regApply = false;
            if (TryParseHotkey(txtHKApplyProfile.Text, out var m3, out var k3))
            {
                regApply = App.Instance.Hotkeys.Register(HK_ID_APPLY, m3, k3, () =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        ApplySelectedProfile();
                    }));
                });
                if (!regApply) failures.AppendLine("- Apply Selected Profile hotkey failed to register.");
            }
            SetHotkeyRegistrationStatus(lblHKApplyStatus, TryParseHotkey(txtHKApplyProfile.Text, out _, out _) ? regApply : (bool?)false);

            if (failures.Length > 0)
            {
                MessageBox.Show(this, "Some hotkeys could not be registered. They may be in use by another app or require different combinations:\n\n" + failures.ToString(),
                    "Hotkey Registration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool TryParseHotkey(string? text, out Keys modifiers, out Keys key)
        {
            modifiers = Keys.None;
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            Keys lastKey = Keys.None;
            foreach (var part in parts)
            {
                var p = part.Trim();
                var lower = p.ToLowerInvariant();
                if (lower is "ctrl" or "control") { modifiers |= Keys.Control; continue; }
                if (lower == "alt") { modifiers |= Keys.Alt; continue; }
                if (lower == "shift") { modifiers |= Keys.Shift; continue; }
                if (lower is "win" or "lwin" or "rwin") { modifiers |= Keys.LWin; continue; }

                // Try parse as Keys enum (A, F1, D1, etc.)
                if (Enum.TryParse<Keys>(p, true, out var parsed))
                {
                    lastKey = parsed;
                }
                else if (p.Length == 1)
                {
                    lastKey = (Keys)char.ToUpperInvariant(p[0]);
                }
            }

            if (lastKey == Keys.None) return false;
            key = lastKey;
            return true;
        }

        private void WireHotkeyCapture(TextBox tb)
        {
            tb.KeyDown += (s, e) =>
            {
                // Build combo from modifiers + key
                var mods = e.Modifiers;
                var code = e.KeyCode;

                // Ignore pure modifier keys (wait for real key)
                if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                var parts = new System.Collections.Generic.List<string>();
                if (mods.HasFlag(Keys.Control)) parts.Add("Ctrl");
                if (mods.HasFlag(Keys.Alt)) parts.Add("Alt");
                if (mods.HasFlag(Keys.Shift)) parts.Add("Shift");
                // Win key capture is limited in WinForms; keep manual entry support

                parts.Add(code.ToString());
                tb.Text = string.Join("+", parts);

                // Prevent the key from being typed into the box
                e.SuppressKeyPress = true;
                e.Handled = true;
            };
        }

        private void UpdateTrayAutoSwitchItemText()
        {
            if (_trayAutoSwitchItem == null) return;
            var on = App.Instance!.AutoSwitch.Running;
            _trayAutoSwitchItem.Text = on ? "Auto-Switch: On (click to turn off)" : "Auto-Switch: Off (click to turn on)";
        }

        private void UpdateTrayShowHideItemText()
        {
            if (_trayShowHideItem == null) return;
            var shouldHide = Visible && WindowState != FormWindowState.Minimized;
            _trayShowHideItem.Text = shouldHide ? "Hide" : "Show";
        }

        private void ShowTrayBalloonOnce()
        {
            if (_trayBalloonShown) return;
            _trayBalloonShown = true;
            try
            {
                trayIcon.BalloonTipTitle = "DJ Win Optimizer";
                trayIcon.BalloonTipText = "Running in the system tray. Double-click the icon to open.";
                trayIcon.ShowBalloonTip(3000);
            }
            catch { }
        }

        // Lightweight operation feedback in status bar + optional tray toast
        private void BeginOperation(string message)
        {
            try
            {
                statusLabel.Text = message;
                if (statusProgress != null)
                {
                    statusProgress.Visible = true;
                    statusProgress.MarqueeAnimationSpeed = 30;
                }
            }
            catch { }
        }

        private void EndOperation(string message)
        {
            try
            {
                statusLabel.Text = message;
                if (statusProgress != null)
                {
                    statusProgress.MarqueeAnimationSpeed = 0;
                    statusProgress.Visible = false;
                }
            }
            catch { }
        }

        private void TryTrayToast(string text)
        {
            try
            {
                trayIcon.BalloonTipTitle = "DJ Win Optimizer";
                trayIcon.BalloonTipText = text;
                trayIcon.ShowBalloonTip(2000);
            }
            catch { }
        }

        // Hotkey UI helpers
        private void UpdateHotkeyPreviewStatuses()
        {
            SetHotkeyPreview(lblHKToggleStatus, txtHKToggle.Text);
            SetHotkeyPreview(lblHKShowHideStatus, txtHKShowHide.Text);
            SetHotkeyPreview(lblHKApplyStatus, txtHKApplyProfile.Text);
        }

        private void SetHotkeyPreview(Label label, string? text)
        {
            if (label == null) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                label.Text = "-";
                label.ForeColor = System.Drawing.SystemColors.ControlText;
                return;
            }

            if (TryParseHotkey(text, out _, out _))
            {
                label.Text = "✓";
                label.ForeColor = System.Drawing.Color.ForestGreen;
            }
            else
            {
                label.Text = "✗";
                label.ForeColor = System.Drawing.Color.IndianRed;
            }
        }

        private void SetHotkeyRegistrationStatus(Label label, bool? success)
        {
            if (label == null) return;
            if (success == null)
            {
                label.Text = "-";
                label.ForeColor = System.Drawing.SystemColors.ControlText;
                return;
            }
            if (success.Value)
            {
                label.Text = "✓";
                label.ForeColor = System.Drawing.Color.ForestGreen;
            }
            else
            {
                label.Text = "✗";
                label.ForeColor = System.Drawing.Color.IndianRed;
            }
        }

        // =====================
        // Embedded Profile Editor
        // =====================

        private record PlanItem(string Guid, string Name, bool Active)
        {
            public override string ToString() => Active ? $"{Name} ({Guid}) *" : $"{Name} ({Guid})";
        }

        private string? GetSelectedProfileName()
            => listProfiles.SelectedItem as string;

        private void OnProfileSelectedChanged()
        {
            var name = GetSelectedProfileName();
            if (string.IsNullOrWhiteSpace(name)) return;
            var p = App.Instance!.Profiles.GetByName(name);
            if (p == null) return;

            // Populate editor fields
            if (txtProfName != null) txtProfName.Text = p.Name;
            if (txtProfDesc != null) txtProfDesc.Text = p.Description ?? string.Empty;

            EditorRefreshPlans();

            // Select profile's configured power plan if present
            if (!string.IsNullOrWhiteSpace(p.PowerPlanGuid) && cboPowerPlans != null)
            {
                for (int i = 0; i < cboPowerPlans.Items.Count; i++)
                {
                    if (cboPowerPlans.Items[i] is PlanItem it &&
                        string.Equals(it.Guid, p.PowerPlanGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        cboPowerPlans.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Service toggles
            if (p.Services != null)
            {
                if (chkSvcSysMain != null) chkSvcSysMain.Checked = p.Services.DisableSysMain;
                if (chkSvcSearchIndex != null) chkSvcSearchIndex.Checked = p.Services.DisableSearchIndex;
                if (chkSvcPrintSpooler != null) chkSvcPrintSpooler.Checked = p.Services.DisablePrintSpooler;
                if (chkSvcDefenderRealtime != null) chkSvcDefenderRealtime.Checked = p.Services.DisableDefenderRealtime;
                if (chkSvcOneDrivePause != null) chkSvcOneDrivePause.Checked = p.Services.PauseOneDrive;
                if (chkSvcOneDriveStop != null) chkSvcOneDriveStop.Checked = p.Services.StopOneDrive;
                if (chkSvcWindowsUpdates != null) chkSvcWindowsUpdates.Checked = p.Services.PauseWindowsUpdates;
                if (chkSvcGameDvr != null) chkSvcGameDvr.Checked = p.Services.DisableGameDvr;
                // Apply any Tag-mapped checkboxes (e.g., new services)
                TryApplyServiceTagsToEditor(p.Services);
            }

            // Targets lists (OR/AND) and Priority
            if (lstTargetsAny != null) lstTargetsAny.Items.Clear();
            if (lstTargetsAll != null) lstTargetsAll.Items.Clear();
            // Backward compatibility: if new fields are null but legacy Targets exist, use them as OR list
            var any = p.TargetsAny ?? ((p.TargetsAll == null || p.TargetsAll.Count == 0) ? p.Targets : p.TargetsAny);
            if (any != null && lstTargetsAny != null)
                foreach (var t in any) lstTargetsAny.Items.Add(t);
            if (p.TargetsAll != null && lstTargetsAll != null)
                foreach (var t in p.TargetsAll) lstTargetsAll.Items.Add(t);
            if (nudPriority != null)
            {
                try { nudPriority.Value = Math.Max(nudPriority.Minimum, Math.Min(nudPriority.Maximum, p.Priority)); } catch { nudPriority.Value = 0; }
            }

            // Autostart list (launch on enter)
            EditorAutoRefreshList(p.Programs?.LaunchOnEnter);
        }

        private void EditorRefreshPlans()
        {
            try
            {
                if (cboPowerPlans == null) return;
                cboPowerPlans.Items.Clear();
                foreach (var (guid, name, active) in App.Instance!.PowerPlans.GetAvailablePlans())
                {
                    cboPowerPlans.Items.Add(new PlanItem(guid, name, active));
                }
                if (cboPowerPlans.Items.Count > 0 && cboPowerPlans.SelectedIndex < 0)
                    cboPowerPlans.SelectedIndex = 0;
                // Update active label
                var activeGuid = App.Instance.PowerPlans.GetActiveGuid();
                if (lblActivePlan != null) lblActivePlan.Text = activeGuid != null ? $"Active: {activeGuid}" : "Active: -";
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to refresh power plans: {ex.Message}");
            }
        }

        private void EditorUseCurrentPlan()
        {
            try
            {
                var active = App.Instance!.PowerPlans.GetActiveGuid();
                if (active == null) { MessageBox.Show(this, "Kein aktiver Energieplan ermittelt."); return; }
                EditorRefreshPlans();
                if (cboPowerPlans == null) return;
                for (int i = 0; i < cboPowerPlans.Items.Count; i++)
                {
                    if (cboPowerPlans.Items[i] is PlanItem it && string.Equals(it.Guid, active, StringComparison.OrdinalIgnoreCase))
                    {
                        cboPowerPlans.SelectedIndex = i;
                        return;
                    }
                }
                // Not in list: add ephemeral entry
                cboPowerPlans.Items.Add(new PlanItem(active, "Current", true));
                cboPowerPlans.SelectedIndex = cboPowerPlans.Items.Count - 1;
            }
            catch (Exception ex)
            {
                AppendLog($"Use current plan failed: {ex.Message}");
            }
        }

        private void EditorCloneSelectedPlan()
        {
            try
            {
                if (cboPowerPlans == null || cboPowerPlans.SelectedItem is not PlanItem it)
                {
                    MessageBox.Show(this, "Bitte zuerst einen Energieplan auswählen.");
                    return;
                }
                var baseGuid = it.Guid;
                var newName = $"{it.Name} (Cloned {DateTime.Now:HHmmss})";
                if (App.Instance!.PowerPlans.TryClone(baseGuid, newName, out var newGuid, out var err))
                {
                    AppendLog($"Cloned power plan {baseGuid} -> {newGuid} '{newName}'");
                    EditorRefreshPlans();
                    if (newGuid != null && cboPowerPlans != null)
                    {
                        for (int i = 0; i < cboPowerPlans.Items.Count; i++)
                        {
                            if (cboPowerPlans.Items[i] is PlanItem pi && string.Equals(pi.Guid, newGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                cboPowerPlans.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show(this, $"Clone failed: {err}", "Power Plans", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Clone plan failed: {ex.Message}");
            }
        }

        private void EditorSaveProfile()
        {
            var name = GetSelectedProfileName();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show(this, "Bitte zuerst ein Profil in der Liste auswählen oder neu anlegen."); return; }
            var p = App.Instance!.Profiles.GetByName(name);
            if (p == null) { MessageBox.Show(this, "Profil konnte nicht geladen werden."); return; }

            // Update from editor
            if (txtProfName != null && !string.IsNullOrWhiteSpace(txtProfName.Text)) p.Name = txtProfName.Text.Trim();
            if (txtProfDesc != null) p.Description = string.IsNullOrWhiteSpace(txtProfDesc.Text) ? null : txtProfDesc.Text.Trim();
            if (cboPowerPlans != null && cboPowerPlans.SelectedItem is PlanItem it)
                p.PowerPlanGuid = it.Guid;

            if (p.Services == null) p.Services = new DJWinOptimizer.Core.Models.ServiceToggles();
            if (chkSvcSysMain != null) p.Services.DisableSysMain = chkSvcSysMain.Checked;
            if (chkSvcSearchIndex != null) p.Services.DisableSearchIndex = chkSvcSearchIndex.Checked;
            if (chkSvcPrintSpooler != null) p.Services.DisablePrintSpooler = chkSvcPrintSpooler.Checked;
            if (chkSvcDefenderRealtime != null) p.Services.DisableDefenderRealtime = chkSvcDefenderRealtime.Checked;
            if (chkSvcOneDrivePause != null) p.Services.PauseOneDrive = chkSvcOneDrivePause.Checked;
            if (chkSvcOneDriveStop != null) p.Services.StopOneDrive = chkSvcOneDriveStop.Checked;
            if (chkSvcWindowsUpdates != null) p.Services.PauseWindowsUpdates = chkSvcWindowsUpdates.Checked;
            if (chkSvcGameDvr != null) p.Services.DisableGameDvr = chkSvcGameDvr.Checked;
            // Read any Tag-mapped checkboxes back into ServiceToggles
            TryReadServiceTagsFromEditor(p.Services);

            // Save targets (OR/AND) and priority
            if (lstTargetsAny != null)
                p.TargetsAny = lstTargetsAny.Items.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lstTargetsAll != null)
                p.TargetsAll = lstTargetsAll.Items.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Maintain legacy Targets for compatibility (mirror OR list when AND is empty)
            if (p.TargetsAll == null || p.TargetsAll.Count == 0)
                p.Targets = p.TargetsAny?.ToList() ?? new System.Collections.Generic.List<string>();
            if (nudPriority != null) p.Priority = (int)nudPriority.Value;

            // Save Autostart list
            p.Programs ??= new DJWinOptimizer.Core.Models.ProgramSets();
            p.Programs.LaunchOnEnter = EditorAutoCollectItems();

            App.Instance!.Profiles.Save(p);
            LoadProfiles();
            // Reselect saved profile in list
            for (int i = 0; i < listProfiles.Items.Count; i++)
            {
                if (string.Equals(listProfiles.Items[i]?.ToString(), p.Name, StringComparison.OrdinalIgnoreCase))
                {
                    listProfiles.SelectedIndex = i; break;
                }
            }
            AppendLog($"Saved profile: {p.Name}");
        }

        private void EditorApplyProfile()
        {
            EditorSaveProfile();
            ApplySelectedProfile();
        }

        // =====================
        // Services diagnostics
        // =====================
        private void RefreshServiceStatus(Label lblSysMain, Label lblWSearch, Label lblSpooler, Label lblWU, Label lblBITS, Label lblDO, Label lblDefRealtime, Label lblOneDrive)
        {
            string GetSvcState(string name)
            {
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController(name);
                    sc.Refresh();
                    return sc.Status.ToString();
                }
                catch (InvalidOperationException)
                {
                    return "NotFound";
                }
                catch (Exception ex)
                {
                    return $"Error: {ex.Message}";
                }
            }

            lblSysMain.Text = GetSvcState("SysMain");
            lblWSearch.Text = GetSvcState("WSearch");
            lblSpooler.Text = GetSvcState("Spooler");
            lblWU.Text = GetSvcState("wuauserv");
            lblBITS.Text = GetSvcState("bits");
            lblDO.Text = GetSvcState("dosvc");

            // Defender realtime (best-effort). Requires Windows Defender module.
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -NonInteractive -Command \"try{(Get-MpPreference).DisableRealtimeMonitoring}catch{''}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null && p.WaitForExit(5000))
                {
                    var output = p.StandardOutput.ReadToEnd().Trim();
                    if (string.Equals(output, "True", StringComparison.OrdinalIgnoreCase))
                        lblDefRealtime.Text = "Disabled";
                    else if (string.Equals(output, "False", StringComparison.OrdinalIgnoreCase))
                        lblDefRealtime.Text = "Enabled";
                    else
                        lblDefRealtime.Text = string.IsNullOrWhiteSpace(output) ? "n/a" : output;
                }
                else
                {
                    lblDefRealtime.Text = "n/a";
                }
            }
            catch
            {
                lblDefRealtime.Text = "n/a";
            }

            // OneDrive pause state is non-trivial to detect reliably; show n/a
            lblOneDrive.Text = "n/a";
        }
    }
}
