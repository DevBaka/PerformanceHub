using ProfileDeck.Core.Audio;
using ProfileDeck.Core.Display;
using ProfileDeck.Core.Logging;
using ProfileDeck.Core.Models;
using ProfileDeck.Core.Persistence;
using ProfileDeck.Core.Power;
using ProfileDeck.Core.Processes;
using ProfileDeck.Core.Services;
using ProfileDeck.Core.Settings;

namespace ProfileDeck.Core.Engine;

public sealed record SystemProfileApplyResult(bool Success, IReadOnlyList<string> Warnings);

/// <summary>
/// Orchestrates applying a SystemProfile: snapshots current state, then runs every configured action group.
/// Every step is individually fault-tolerant - one failed step is logged and skipped, never aborts the rest.
/// </summary>
public sealed class SystemProfileEngine
{
    private readonly DisplayManager _displayManager;
    private readonly ProcessLauncherService _launcher = new();
    private readonly ServiceControlService _services = new();
    private readonly SettingsToggleService _settings = new();
    private readonly PowerPlanService _power = new();
    private readonly ProcessorSchedulingService _scheduling = new();
    private readonly ProcessPriorityService _priority = new();
    private readonly AudioDeviceService _audio = new();
    private readonly ProfileRepository<DisplayProfile> _displayRepo;
    private readonly ILogSink _log;

    public SystemProfileEngine(ILogSink log, DisplayManager? displayManager = null, ProfileRepository<DisplayProfile>? displayRepo = null)
    {
        _log = log;
        _displayManager = displayManager ?? new DisplayManager();
        _displayRepo = displayRepo ?? new ProfileRepository<DisplayProfile>(AppPaths.DisplayProfilesDir, p => p.Name);
    }

    public SystemProfileApplyResult Apply(SystemProfile profile, bool dryRun = false)
    {
        var warnings = new List<string>();
        try
        {
            if (!dryRun)
            {
                var snapshot = CaptureSnapshot(profile);
                JsonStore.Save(AppPaths.SnapshotFile, snapshot);
            }

            _log.Info($"{(dryRun ? "[Dry-Run] " : "")}Wende System-Profil '{profile.Name}' an...");

            if (!string.IsNullOrWhiteSpace(profile.DisplayProfileName))
            {
                var displayProfile = _displayRepo.GetByName(profile.DisplayProfileName);
                if (displayProfile == null)
                    warnings.Add($"Display-Profil '{profile.DisplayProfileName}' nicht gefunden.");
                else
                    _displayManager.Apply(displayProfile, _log, dryRun);
            }

            if (profile.ProcessesToKill.Count > 0)
                RunStep("Prozesse beenden", dryRun, () => _launcher.Kill(profile.ProcessesToKill, _log));

            if (profile.Services.Count > 0)
                RunStep("Dienste", dryRun, () => _services.Apply(profile.Services, _log));

            foreach (var toggle in profile.SettingToggles)
                RunStep($"Einstellung {toggle.Setting}", dryRun, () => _settings.Apply(toggle.Setting, toggle.Enabled, _log));

            if (!string.IsNullOrWhiteSpace(profile.PowerPlanGuid))
                RunStep("Power-Plan", dryRun, () => _power.TrySetActive(profile.PowerPlanGuid, _log));

            if (profile.ProcessorScheduling.HasValue)
                RunStep("Prozessor-Scheduling", dryRun, () => _scheduling.Apply(profile.ProcessorScheduling.Value, _log));

            if (profile.ProcessPriorities.Count > 0)
                RunStep("Prozess-Prioritäten", dryRun, () => _priority.Apply(profile.ProcessPriorities, _log));

            if (!string.IsNullOrWhiteSpace(profile.DefaultAudioOutputId))
                RunStep("Standard-Audioausgabe", dryRun, () => _audio.SetDefaultOutput(profile.DefaultAudioOutputId, _log));
            if (!string.IsNullOrWhiteSpace(profile.DefaultAudioInputId))
                RunStep("Standard-Audioeingabe", dryRun, () => _audio.SetDefaultInput(profile.DefaultAudioInputId, _log));

            if (profile.ProgramsToLaunch.Count > 0)
                RunStep("Programme starten", dryRun, () => _launcher.Launch(profile.ProgramsToLaunch, _log));

            _log.Info($"{(dryRun ? "[Dry-Run] " : "")}System-Profil '{profile.Name}' fertig angewendet.");
            return new SystemProfileApplyResult(true, warnings);
        }
        catch (Exception ex)
        {
            _log.Error($"System-Profil '{profile.Name}' konnte nicht angewendet werden", ex);
            warnings.Add(ex.Message);
            return new SystemProfileApplyResult(false, warnings);
        }
    }

    /// <summary>Reverts the last snapshot taken before a profile was applied.</summary>
    public SystemProfileApplyResult RestorePrevious()
    {
        var snapshot = JsonStore.Load<Snapshot>(AppPaths.SnapshotFile);
        if (snapshot == null)
        {
            _log.Warn("Kein Snapshot zum Wiederherstellen vorhanden.");
            return new SystemProfileApplyResult(false, new[] { "Kein Snapshot vorhanden." });
        }

        _log.Info($"Stelle Zustand von {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC wieder her...");

        if (snapshot.Displays.Count > 0)
        {
            var restoreProfile = new DisplayProfile { Name = "__restore__", Displays = snapshot.Displays };
            RunStep("Display-Wiederherstellung", false, () => _displayManager.Apply(restoreProfile, _log));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PowerPlanGuid))
            RunStep("Power-Plan wiederherstellen", false, () => _power.TrySetActive(snapshot.PowerPlanGuid, _log));

        if (snapshot.ProcessorScheduling.HasValue)
            RunStep("Prozessor-Scheduling wiederherstellen", false, () => _scheduling.Apply(snapshot.ProcessorScheduling.Value, _log));

        foreach (var (settingName, wasEnabled) in snapshot.SettingToggles)
        {
            if (Enum.TryParse<WindowsSetting>(settingName, out var setting))
                RunStep($"Einstellung {setting} wiederherstellen", false, () => _settings.Apply(setting, wasEnabled, _log));
        }

        foreach (var (serviceName, state) in snapshot.ServiceStates)
        {
            RunStep($"Dienst {serviceName} wiederherstellen", false, () => _services.Apply(new[]
            {
                new ServiceAction
                {
                    ServiceName = serviceName,
                    DesiredState = state.WasRunning ? ServiceDesiredState.Start : ServiceDesiredState.Stop,
                    StartupType = Enum.TryParse<ServiceStartupType>(state.StartupType, out var st) ? st : null,
                }
            }, _log));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DefaultAudioOutputId))
            RunStep("Audioausgabe wiederherstellen", false, () => _audio.SetDefaultOutput(snapshot.DefaultAudioOutputId, _log));
        if (!string.IsNullOrWhiteSpace(snapshot.DefaultAudioInputId))
            RunStep("Audioeingabe wiederherstellen", false, () => _audio.SetDefaultInput(snapshot.DefaultAudioInputId, _log));

        _log.Info("Wiederherstellung abgeschlossen.");
        return new SystemProfileApplyResult(true, Array.Empty<string>());
    }

    private Snapshot CaptureSnapshot(SystemProfile profile)
    {
        var snapshot = new Snapshot();

        if (!string.IsNullOrWhiteSpace(profile.DisplayProfileName))
            snapshot.Displays = _displayManager.CaptureCurrentAsProfile("__snapshot__").Displays;

        if (!string.IsNullOrWhiteSpace(profile.PowerPlanGuid))
            snapshot.PowerPlanGuid = _power.GetActiveGuid();

        if (profile.ProcessorScheduling.HasValue)
        {
            var raw = _scheduling.GetCurrentRawValue();
            snapshot.ProcessorScheduling = raw == 38 ? ProcessorSchedulingMode.ProgramsFocused : ProcessorSchedulingMode.BackgroundServicesFocused;
        }

        foreach (var toggle in profile.SettingToggles)
        {
            var current = _settings.GetCurrentState(toggle.Setting);
            if (current.HasValue) snapshot.SettingToggles[toggle.Setting.ToString()] = current.Value;
        }

        foreach (var action in profile.Services)
        {
            var status = _services.GetStatus(action.ServiceName);
            var startupType = _services.GetStartupType(action.ServiceName);
            if (status.HasValue)
            {
                snapshot.ServiceStates[action.ServiceName] = new ServiceSnapshotState
                {
                    WasRunning = status.Value == System.ServiceProcess.ServiceControllerStatus.Running,
                    StartupType = startupType ?? "Manual",
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.DefaultAudioOutputId))
            snapshot.DefaultAudioOutputId = _audio.GetDefaultOutputId();
        if (!string.IsNullOrWhiteSpace(profile.DefaultAudioInputId))
            snapshot.DefaultAudioInputId = _audio.GetDefaultInputId();

        return snapshot;
    }

    private void RunStep(string stepName, bool dryRun, Action action)
    {
        if (dryRun)
        {
            _log.Info($"[Dry-Run] würde ausführen: {stepName}");
            return;
        }
        try { action(); }
        catch (Exception ex) { _log.Warn($"Schritt '{stepName}' fehlgeschlagen: {ex.Message}"); }
    }
}
