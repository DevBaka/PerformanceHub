using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PerformanceHub.Core.Models;
using PerformanceHub.Core;

namespace PerformanceHub.UI
{
    public partial class ProfileEditorForm : Form
    {
        public Profile ResultProfile { get; private set; }

        public ProfileEditorForm(Profile source)
        {
            InitializeComponent();
            // clone to avoid mutating the original object before OK
            ResultProfile = new Profile
            {
                Name = source.Name,
                Description = source.Description,
                PowerPlanGuid = source.PowerPlanGuid,
                Services = new ServiceToggles
                {
                    DisableDefenderRealtime = source.Services.DisableDefenderRealtime,
                    BlockScheduledScans = source.Services.BlockScheduledScans,
                    PauseOneDrive = source.Services.PauseOneDrive,
                    DisableSearchIndex = source.Services.DisableSearchIndex,
                    DisableSysMain = source.Services.DisableSysMain,
                    DisableGameDvr = source.Services.DisableGameDvr,
                    DisablePrintSpooler = source.Services.DisablePrintSpooler,
                    PauseWindowsUpdates = source.Services.PauseWindowsUpdates
                },
                Audio = new AudioOptimizations
                {
                    EnableWasapiExclusive = source.Audio.EnableWasapiExclusive,
                    PreferAsioIfAvailable = source.Audio.PreferAsioIfAvailable
                },
                Targets = new List<string>(source.Targets ?? new List<string>()),
                ProcessPriorities = source.ProcessPriorities != null
                    ? new Dictionary<string, string>(source.ProcessPriorities, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                ,
                TimerResolution = source.TimerResolution,
                Programs = new ProgramSets
                {
                    LaunchOnEnter = new List<ProgramAction>(source.Programs?.LaunchOnEnter ?? new()),
                    KillOnExit = new List<ProgramAction>(source.Programs?.KillOnExit ?? new())
                }
            };

            // Populate UI
            txtName.Text = ResultProfile.Name;
            txtDescription.Text = ResultProfile.Description ?? string.Empty;
            txtPowerPlanGuid.Text = ResultProfile.PowerPlanGuid ?? string.Empty;

            chkDisableDefender.Checked = ResultProfile.Services.DisableDefenderRealtime;
            chkBlockScans.Checked = ResultProfile.Services.BlockScheduledScans;
            chkPauseOneDrive.Checked = ResultProfile.Services.PauseOneDrive;
            chkDisableIndexing.Checked = ResultProfile.Services.DisableSearchIndex;
            chkDisableSysMain.Checked = ResultProfile.Services.DisableSysMain;
            chkDisableGameDvr.Checked = ResultProfile.Services.DisableGameDvr;
            chkDisableSpooler.Checked = ResultProfile.Services.DisablePrintSpooler;
            chkPauseUpdates.Checked = ResultProfile.Services.PauseWindowsUpdates;

            chkWasapiExclusive.Checked = ResultProfile.Audio.EnableWasapiExclusive;
            chkPreferAsio.Checked = ResultProfile.Audio.PreferAsioIfAvailable;

            // Timer resolution
            try
            {
                if (cmbTimerRes != null)
                {
                    var mode = ResultProfile.TimerResolution;
                    cmbTimerRes.SelectedIndex = mode == TimerResolutionMode.OneMs ? 1 : 0;
                }
            }
            catch { }

            txtTargets.Lines = (ResultProfile.Targets ?? new List<string>()).ToArray();
            txtPriorities.Lines = (ResultProfile.ProcessPriorities ?? new Dictionary<string, string>())
                .Select(kv => $"{kv.Key}={kv.Value}")
                .ToArray();

            // Programs: Launch (paths) and Kill (process names)
            if (txtLaunch != null)
            {
                txtLaunch.Lines = (ResultProfile.Programs?.LaunchOnEnter ?? new())
                    .Select(a => a.Path ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
            }
            if (txtKill != null)
            {
                txtKill.Lines = (ResultProfile.Programs?.KillOnExit ?? new())
                    .Select(a => a.ProcessName ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
            }

            btnOk.Click += (_, __) => OnOk();
            btnAddFromRunning.Click += (_, __) => OnAddFromRunning();
            btnBrowseTarget.Click += (_, __) => OnBrowseTarget();

            // Power plans UI wiring
            btnRefreshPlans.Click += (_, __) => PopulatePowerPlans();
            cmbPowerPlans.SelectedIndexChanged += (_, __) => OnPlanSelected();
            btnUseCurrentPlan.Click += (_, __) => UseCurrentPlan();
            btnClonePlan.Click += (_, __) => CloneSelectedPlan();
            PopulatePowerPlans();
        }

        private void OnBrowseTarget()
        {
            using var ofd = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var file = System.IO.Path.GetFileName(ofd.FileName);
                    if (string.IsNullOrWhiteSpace(file)) return;
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file += ".exe";
                    var lines = new HashSet<string>((txtTargets.Lines ?? Array.Empty<string>()).Select(l => (l ?? string.Empty).Trim()).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase);
                    lines.Add(file);
                    txtTargets.Lines = lines.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch { }
            }
        }

        private void OnAddFromRunning()
        {
            using var dlg = new ProcessPickerForm();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var existing = new HashSet<string>(txtTargets.Lines ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var exe in dlg.SelectedExecutables)
                {
                    if (!existing.Contains(exe)) existing.Add(exe);
                }
                txtTargets.Lines = existing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        private void OnOk()
        {
            // Basic validation
            var name = (txtName.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Name is required", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None; // keep dialog open
                return;
            }

            ResultProfile.Name = name;
            ResultProfile.Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim();
            ResultProfile.PowerPlanGuid = string.IsNullOrWhiteSpace(txtPowerPlanGuid.Text) ? null : txtPowerPlanGuid.Text.Trim();

            // Timer resolution
            if (cmbTimerRes != null)
            {
                var sel = (cmbTimerRes.SelectedItem?.ToString() ?? cmbTimerRes.Text ?? "Stock").Trim();
                ResultProfile.TimerResolution = string.Equals(sel, "OneMs", StringComparison.OrdinalIgnoreCase)
                    ? TimerResolutionMode.OneMs : TimerResolutionMode.Stock;
            }

            ResultProfile.Services.DisableDefenderRealtime = chkDisableDefender.Checked;
            ResultProfile.Services.BlockScheduledScans = chkBlockScans.Checked;
            ResultProfile.Services.PauseOneDrive = chkPauseOneDrive.Checked;
            ResultProfile.Services.DisableSearchIndex = chkDisableIndexing.Checked;
            ResultProfile.Services.DisableSysMain = chkDisableSysMain.Checked;
            ResultProfile.Services.DisableGameDvr = chkDisableGameDvr.Checked;
            ResultProfile.Services.DisablePrintSpooler = chkDisableSpooler.Checked;
            ResultProfile.Services.PauseWindowsUpdates = chkPauseUpdates.Checked;

            ResultProfile.Audio.EnableWasapiExclusive = chkWasapiExclusive.Checked;
            ResultProfile.Audio.PreferAsioIfAvailable = chkPreferAsio.Checked;

            // Targets: one per non-empty line
            ResultProfile.Targets = txtTargets.Lines
                .Select(l => (l ?? string.Empty).Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Priorities: exe=Priority per line
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in txtPriorities.Lines ?? Array.Empty<string>())
            {
                var s = (line ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                var idx = s.IndexOf('=');
                if (idx <= 0 || idx >= s.Length - 1) continue;
                var exe = s.Substring(0, idx).Trim();
                var pr = s.Substring(idx + 1).Trim();
                if (!string.IsNullOrWhiteSpace(exe)) map[exe] = pr;
            }
            ResultProfile.ProcessPriorities = map;

            // Programs
            var launch = new List<ProgramAction>();
            foreach (var line in txtLaunch?.Lines ?? Array.Empty<string>())
            {
                var s = (line ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                launch.Add(new ProgramAction { Path = s, Args = string.Empty, Wait = false });
            }
            var kills = new List<ProgramAction>();
            foreach (var line in txtKill?.Lines ?? Array.Empty<string>())
            {
                var s = (line ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                // Normalize to filename only for ProcessName
                try { s = System.IO.Path.GetFileName(s); } catch { }
                kills.Add(new ProgramAction { ProcessName = s });
            }
            ResultProfile.Programs = new ProgramSets { LaunchOnEnter = launch, KillOnExit = kills };
        }

        private sealed class PlanItem
        {
            public string Guid { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public bool Active { get; init; }
            public override string ToString()
            {
                var star = Active ? " *" : string.Empty;
                return string.IsNullOrEmpty(Guid) ? Name : $"{Name} ({Guid}){star}";
            }
        }

        private void PopulatePowerPlans()
        {
            try
            {
                cmbPowerPlans.Items.Clear();
                // First item: no change
                var none = new PlanItem { Guid = string.Empty, Name = "No change (keep current)", Active = false };
                cmbPowerPlans.Items.Add(none);

                var plans = App.Instance!.PowerPlans.GetAvailablePlans();
                string? activeGuid = null; string activeName = "-";
                foreach (var (guid, name, active) in plans)
                {
                    cmbPowerPlans.Items.Add(new PlanItem { Guid = guid, Name = name, Active = active });
                    if (active) { activeGuid = guid; activeName = name; }
                }

                // Select current profile setting if present, else active plan, else first item
                var wanted = ResultProfile.PowerPlanGuid ?? string.Empty;
                int indexToSelect = 0;
                for (int i = 0; i < cmbPowerPlans.Items.Count; i++)
                {
                    if (cmbPowerPlans.Items[i] is PlanItem pi && string.Equals(pi.Guid, wanted, System.StringComparison.OrdinalIgnoreCase))
                    {
                        indexToSelect = i; break;
                    }
                }
                if (indexToSelect == 0)
                {
                    for (int i = 0; i < cmbPowerPlans.Items.Count; i++)
                    {
                        if (cmbPowerPlans.Items[i] is PlanItem pi && pi.Active) { indexToSelect = i; break; }
                    }
                }
                cmbPowerPlans.SelectedIndex = indexToSelect;
                OnPlanSelected();

                // Update active plan label
                if (!string.IsNullOrWhiteSpace(activeGuid))
                    lblActivePlan.Text = $"Active: {activeName} ({activeGuid})";
                else
                    lblActivePlan.Text = "Active: -";
            }
            catch { }
        }

        private void OnPlanSelected()
        {
            try
            {
                if (cmbPowerPlans.SelectedItem is PlanItem pi)
                {
                    txtPowerPlanGuid.Text = pi.Guid;
                }
            }
            catch { }
        }

        private void UseCurrentPlan()
        {
            try
            {
                var activeGuid = App.Instance!.PowerPlans.GetActiveGuid();
                if (string.IsNullOrWhiteSpace(activeGuid))
                {
                    MessageBox.Show(this, "Aktiver Energieplan konnte nicht ermittelt werden.", "Energieplan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // Find in combo, else set GUID directly
                for (int i = 0; i < cmbPowerPlans.Items.Count; i++)
                {
                    if (cmbPowerPlans.Items[i] is PlanItem pi && string.Equals(pi.Guid, activeGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbPowerPlans.SelectedIndex = i;
                        OnPlanSelected();
                        return;
                    }
                }
                // Not listed (rare) -> set GUID box
                txtPowerPlanGuid.Text = activeGuid;
            }
            catch { }
        }

        private void CloneSelectedPlan()
        {
            try
            {
                if (cmbPowerPlans.SelectedItem is not PlanItem pi || string.IsNullOrWhiteSpace(pi.Guid))
                {
                    MessageBox.Show(this, "Bitte zuerst einen konkreten Energieplan auswählen.", "Energieplan klonen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var baseName = string.IsNullOrWhiteSpace(pi.Name) ? "Plan" : pi.Name;
                var newName = $"{baseName} (Copy {DateTime.Now:yyyy-MM-dd HH:mm})";
                if (!App.Instance!.PowerPlans.TryClone(pi.Guid, newName, out var newGuid, out var error) || string.IsNullOrWhiteSpace(newGuid))
                {
                    MessageBox.Show(this, $"Klonen fehlgeschlagen: {error ?? "Unbekannter Fehler"}", "Energieplan klonen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                // Refresh list and select the new plan
                PopulatePowerPlans();
                for (int i = 0; i < cmbPowerPlans.Items.Count; i++)
                {
                    if (cmbPowerPlans.Items[i] is PlanItem item && string.Equals(item.Guid, newGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbPowerPlans.SelectedIndex = i;
                        OnPlanSelected();
                        break;
                    }
                }
                MessageBox.Show(this, $"Energieplan geklont: {newName}\nGUID: {newGuid}", "Energieplan klonen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fehler beim Klonen: {ex.Message}", "Energieplan klonen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
