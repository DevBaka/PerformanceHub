using System;
using System.Linq;
using System.ServiceProcess;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;
using DJWinOptimizer.Utils;
using Microsoft.Win32;

namespace DJWinOptimizer.Services
{
    public class WindowsServiceManager : IServiceManager
    {
        private readonly ILogger _log;
        private System.Threading.CancellationTokenSource? _odBlockCts;
        // OneDrive Run key backup so we can restore on revert
        private string? _odRunBackup;
        private bool _odRunWasPresent;
        private string? _odRunBackupLM;
        private bool _odRunWasPresentLM;
        // Backups of service start types and running states for precise reverts this session
        private readonly System.Collections.Generic.Dictionary<string, string> _svcStartTypeBackup = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, bool> _svcWasRunningBackup = new(StringComparer.OrdinalIgnoreCase);
        // Backups of registry DWORD values: key tuple -> (existed, value)
        private readonly System.Collections.Generic.Dictionary<(RegistryHive hive, string path, string name), (bool existed, int value)> _regDwordBackup = new();
        public WindowsServiceManager(ILogger log) { _log = log; }

        public void Apply(ServiceToggles t)
        {
            TryService("SysMain", t.DisableSysMain);
            TryService("WSearch", t.DisableSearchIndex);
            TryService("Spooler", t.DisablePrintSpooler);

            // Defender realtime
            if (t.DisableDefenderRealtime)
            {
                if (AdminUtil.IsAdministrator())
                {
                    if (!TrySetDefenderRealtime(disable: true, out var err))
                    {
                        _log.Warn($"Defender realtime disable failed: {err}");
                    }
                    else
                    {
                        _log.Info("Defender realtime monitoring disabled.");
                    }
                }
                else
                {
                    _log.Warn("Cannot disable Defender realtime without administrator rights.");
                }
            }

            // Pause Windows Update services (best-effort)
            if (t.PauseWindowsUpdates)
            {
                if (AdminUtil.IsAdministrator())
                {
                    TryService("wuauserv", true);
                    TryService("bits", true);
                    TryService("dosvc", true); // Delivery Optimization
                }
                else
                {
                    _log.Warn("Cannot pause Windows Update services without administrator rights.");
                }
            }

            // OneDrive controls: pause or stop + keep down (best-effort)
            try
            {
                if (t.StopOneDrive)
                {
                    // Prevent auto-start (HKCU Run) to reduce restarts
                    TryToggleOneDriveRunAutostart(enable: false);
                    // Try disable scheduled update tasks (best-effort)
                    TryToggleOneDriveTasks(disable: true, out _);
                    // Enforce OneDrive disabled via policy (machine-wide) to avoid client restarts
                    SetDwordWithBackup(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/OneDrive", "DisableFileSyncNGSC", 1, out _, out _);
                    if (!TryOneDriveStop(out var odErr))
                        _log.Warn($"OneDrive stop failed: {odErr}");
                    // Keep trying to block restarts while profile is active
                    EnsureOneDriveBlocker(enabled: true);
                }
                else if (t.PauseOneDrive)
                {
                    EnsureOneDriveBlocker(enabled: false);
                    if (!TryOneDrivePause(out var odErr))
                        _log.Warn($"OneDrive pause failed: {odErr}");
                }
                else
                {
                    // No OneDrive control requested for this profile
                    EnsureOneDriveBlocker(enabled: false);
                }
            }
            catch { }

            // Game DVR via registry/policy
            if (t.DisableGameDvr)
            {
                if (!TrySetGameDvr(disable: true, out var gErr))
                    _log.Warn($"Game DVR disable failed: {gErr}");
                else
                    _log.Info("Game DVR disabled.");
            }

            // Defender scheduled scans: disable scheduled tasks to block automatic scans
            if (t.BlockScheduledScans)
            {
                if (!TryToggleDefenderScheduledTasks(disable: true, out var dErr))
                    _log.Warn($"Defender scheduled task disable failed: {dErr}");
                else
                    _log.Info("Defender scheduled scans blocked (tasks disabled).");
            }

            // Xbox related services
            if (t.DisableXboxServices)
            {
                if (!TryToggleXboxServices(disable: true, out var xErr))
                    _log.Warn($"Xbox services disable failed: {xErr}");
                else
                    _log.Info("Xbox services disabled.");
            }

            // Telemetry reduction (policies + services)
            if (t.ReduceTelemetry)
            {
                if (!TrySetTelemetryPolicies(reduce: true, out var telErr))
                    _log.Warn($"Telemetry policy set failed: {telErr}");
                else
                    _log.Info("Telemetry reduced via policy and services.");
            }

            // Consumer features (CloudContent)
            if (t.DisableConsumerFeatures)
            {
                if (!TrySetConsumerFeatures(disable: true, out var cErr))
                    _log.Warn($"Disable consumer features failed: {cErr}");
                else
                    _log.Info("Consumer features disabled.");
            }

            // Activity history collection/upload
            if (t.DisableActivityHistory)
            {
                if (!TrySetActivityHistory(disable: true, out var aErr))
                    _log.Warn($"Disable activity history failed: {aErr}");
                else
                    _log.Info("Activity history disabled.");
            }
        }

        public void Revert(ServiceToggles t)
        {
            // Stop OneDrive blocker first so we can resume gracefully
            EnsureOneDriveBlocker(enabled: false);
            TryService("SysMain", false);
            TryService("WSearch", false);
            TryService("Spooler", false);

            // Re-enable Windows Update services if they were paused
            TryService("wuauserv", false);
            TryService("bits", false);
            TryService("dosvc", false);

            // Re-enable Defender realtime if previously disabled
            if (AdminUtil.IsAdministrator())
            {
                if (!TrySetDefenderRealtime(disable: false, out var err))
                    _log.Warn($"Defender realtime re-enable failed: {err}");
            }

            // Re-enable Game DVR settings if they were disabled
            if (!TrySetGameDvr(disable: false, out var gErr))
                _log.Warn($"Game DVR re-enable failed: {gErr}");

            // Re-enable Defender scheduled tasks if they were disabled
            if (!TryToggleDefenderScheduledTasks(disable: false, out var dErr))
                _log.Warn($"Defender scheduled task re-enable failed: {dErr}");

            // Attempt to resume OneDrive if it was paused/stopped
            try
            {
                if (t.PauseOneDrive || t.StopOneDrive)
                {
                    if (!TryOneDriveResume(out var odErr))
                        _log.Warn($"OneDrive resume attempt failed: {odErr}");
                    // Re-enable scheduled tasks (best-effort)
                    TryToggleOneDriveTasks(disable: false, out _);
                    // Restore Run autostart if we had disabled it
                    TryToggleOneDriveRunAutostart(enable: true);
                    // Restore OneDrive policy (delete or set back)
                    RestoreDword(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/OneDrive", "DisableFileSyncNGSC", defaultValue: 0, out _);
                }
            }
            catch { }

            // Re-enable Xbox services
            if (!TryToggleXboxServices(disable: false, out var xErr))
                _log.Warn($"Xbox services enable failed: {xErr}");

            // Restore telemetry defaults
            if (!TrySetTelemetryPolicies(reduce: false, out var telErr))
                _log.Warn($"Telemetry policy restore failed: {telErr}");

            // Restore consumer features
            if (!TrySetConsumerFeatures(disable: false, out var cErr))
                _log.Warn($"Restore consumer features failed: {cErr}");

            // Restore activity history settings
            if (!TrySetActivityHistory(disable: false, out var aErr))
                _log.Warn($"Restore activity history failed: {aErr}");
        }

        private bool TryToggleXboxServices(bool disable, out string? error)
        {
            error = null;
            var services = new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" };
            foreach (var s in services)
            {
                try
                {
                    BackupServiceState(s);
                    if (!TrySetServiceStartType(s, disable ? "disabled" : "auto", out var cfgErr))
                        error = string.IsNullOrWhiteSpace(error) ? cfgErr : ($"{error}; {cfgErr}");
                    TryService(s, disable);
                }
                catch (Exception ex)
                {
                    error = string.IsNullOrWhiteSpace(error) ? ex.Message : ($"{error}; {ex.Message}");
                }
            }
            return string.IsNullOrWhiteSpace(error);
        }

        private bool TrySetTelemetryPolicies(bool reduce, out string? error)
        {
            // Policies: HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection AllowTelemetry=0
            // Services: DiagTrack, dmwappushservice
            error = null;
            try
            {
                // Backup originals once
                SetDwordWithBackup(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/DataCollection", "AllowTelemetry", reduce ? 0 : (RestoreOrDefault(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/DataCollection", "AllowTelemetry", 3)), out var ok1, out var e1);
                SetDwordWithBackup(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/AppCompat", "AITEnable", reduce ? 0 : (RestoreOrDefault(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/AppCompat", "AITEnable", 1)), out var ok2, out var e2);
                if (!(ok1 && ok2))
                {
                    error = string.Join(" | ", new[] { e1, e2 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                }

                TryService("DiagTrack", disable: reduce);
                TryService("dmwappushservice", disable: reduce);
                if (!reduce)
                {
                    RestoreServiceState("DiagTrack");
                    RestoreServiceState("dmwappushservice");
                }
                return string.IsNullOrWhiteSpace(error);
            }
            catch (Exception ex)
            {
                error = ex.Message; return false;
            }
        }

        private bool TrySetConsumerFeatures(bool disable, out string? error)
        {
            // HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent DisableConsumerFeatures=1
            error = null;
            try
            {
                if (disable)
                {
                    SetDwordWithBackup(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/CloudContent", "DisableConsumerFeatures", 1, out var ok1, out var e1);
                    SetDwordWithBackup(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/CloudContent", "DisableSoftLanding", 1, out var ok2, out var e2);
                    if (!(ok1 && ok2))
                        error = string.Join(" | ", new[] { e1, e2 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                }
                else
                {
                    var r1 = RestoreDword(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/CloudContent", "DisableConsumerFeatures", defaultValue: 0, out var e1);
                    var r2 = RestoreDword(RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/CloudContent", "DisableSoftLanding", defaultValue: 0, out var e2);
                    if (!(r1 && r2)) error = string.Join(" | ", new[] { e1, e2 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                }
                return string.IsNullOrWhiteSpace(error);
            }
            catch (Exception ex)
            {
                error = ex.Message; return false;
            }
        }

        private bool TrySetActivityHistory(bool disable, out string? error)
        {
            // HKLM\SOFTWARE\Policies\Microsoft\Windows\System {EnableActivityFeed, PublishUserActivities, UploadUserActivities} = 0/1
            error = null;
            try
            {
                var key = "SOFTWARE/Policies/Microsoft/Windows/System";
                if (disable)
                {
                    SetDwordWithBackup(RegistryHive.LocalMachine, key, "EnableActivityFeed", 0, out var ok1, out var e1);
                    SetDwordWithBackup(RegistryHive.LocalMachine, key, "PublishUserActivities", 0, out var ok2, out var e2);
                    SetDwordWithBackup(RegistryHive.LocalMachine, key, "UploadUserActivities", 0, out var ok3, out var e3);
                    if (!(ok1 && ok2 && ok3))
                        error = string.Join(" | ", new[] { e1, e2, e3 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                }
                else
                {
                    var r1 = RestoreDword(RegistryHive.LocalMachine, key, "EnableActivityFeed", defaultValue: 1, out var e1);
                    var r2 = RestoreDword(RegistryHive.LocalMachine, key, "PublishUserActivities", defaultValue: 1, out var e2);
                    var r3 = RestoreDword(RegistryHive.LocalMachine, key, "UploadUserActivities", defaultValue: 1, out var e3);
                    if (!(r1 && r2 && r3))
                        error = string.Join(" | ", new[] { e1, e2, e3 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                }
                return string.IsNullOrWhiteSpace(error);
            }
            catch (Exception ex)
            {
                error = ex.Message; return false;
            }
        }

        private void BackupServiceState(string serviceName)
        {
            try
            {
                if (!_svcStartTypeBackup.ContainsKey(serviceName))
                {
                    if (TryGetServiceStartType(serviceName, out var startType))
                        _svcStartTypeBackup[serviceName] = startType!;
                }
                if (!_svcWasRunningBackup.ContainsKey(serviceName))
                {
                    using var sc = new ServiceController(serviceName);
                    sc.Refresh();
                    _svcWasRunningBackup[serviceName] = sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch { }
        }

        private void RestoreServiceState(string serviceName)
        {
            try
            {
                if (_svcStartTypeBackup.TryGetValue(serviceName, out var startType))
                {
                    TrySetServiceStartType(serviceName, startType, out _);
                }
                if (_svcWasRunningBackup.TryGetValue(serviceName, out var wasRunning))
                {
                    TryService(serviceName, disable: !wasRunning);
                }
            }
            catch { }
        }

        private bool TryGetServiceStartType(string serviceName, out string? startType)
        {
            startType = null;
            if (!AdminUtil.TryRunProcess("sc.exe", $"qc {serviceName}", 8000, out var output)) return false;
            if (string.IsNullOrWhiteSpace(output)) return false;
            try
            {
                var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .FirstOrDefault(l => l.IndexOf("START_TYPE", StringComparison.OrdinalIgnoreCase) >= 0);
                if (line == null) return false;
                // Example: START_TYPE         : 2   AUTO_START
                if (line.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0) { startType = "disabled"; return true; }
                if (line.IndexOf("AUTO_START", StringComparison.OrdinalIgnoreCase) >= 0) { startType = "auto"; return true; }
                if (line.IndexOf("DEMAND_START", StringComparison.OrdinalIgnoreCase) >= 0) { startType = "demand"; return true; }
            }
            catch { }
            return false;
        }

        private void SetDwordWithBackup(RegistryHive hive, string path, string name, int value, out bool ok, out string? err)
        {
            try
            {
                var key = (hive, path, name);
                if (!_regDwordBackup.ContainsKey(key))
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var sub = baseKey.OpenSubKey(path, writable: false);
                    var val = sub?.GetValue(name);
                    if (val is int i)
                        _regDwordBackup[key] = (true, i);
                    else if (val is null)
                        _regDwordBackup[key] = (false, 0);
                    else
                        _regDwordBackup[key] = (true, Convert.ToInt32(val));
                }
                ok = RegistryUtil.TrySetDword(hive, path, name, value, out err);
            }
            catch (Exception ex)
            {
                ok = false; err = ex.Message;
            }
        }

        private bool RestoreDword(RegistryHive hive, string path, string name, int defaultValue, out string? err)
        {
            var key = (hive, path, name);
            if (_regDwordBackup.TryGetValue(key, out var backup))
            {
                if (!backup.existed)
                {
                    // Best-effort delete when it didn't exist originally
                    return RegistryUtil.TryDeleteValue(hive, path, name, out err);
                }
                return RegistryUtil.TrySetDword(hive, path, name, backup.value, out err);
            }
            // No backup; set to provided default for sane state
            return RegistryUtil.TrySetDword(hive, path, name, defaultValue, out err);
        }

        private int RestoreOrDefault(RegistryHive hive, string path, string name, int @default)
        {
            var key = (hive, path, name);
            if (_regDwordBackup.TryGetValue(key, out var backup) && backup.existed)
                return backup.value;
            return @default;
        }

        private void EnsureOneDriveBlocker(bool enabled)
        {
            if (!enabled)
            {
                try { _odBlockCts?.Cancel(); } catch { }
                _odBlockCts = null;
                return;
            }

            if (_odBlockCts != null && !_odBlockCts.IsCancellationRequested) return; // already running

            _odBlockCts = new System.Threading.CancellationTokenSource();
            var token = _odBlockCts.Token;
            System.Threading.Tasks.Task.Run(async () =>
            {
                _log.Info("OneDrive blocker started.");
                // Try a single graceful shutdown when the blocker starts
                try { TryOneDriveStop(out _); } catch { }
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Kill OneDrive and its updater helpers if they reappear
                        var names = new[] { "OneDrive", "OneDriveStandaloneUpdater", "OneDriveSetup" };
                        foreach (var n in names)
                        {
                            var procs = System.Diagnostics.Process.GetProcessesByName(n);
                            foreach (var p in procs)
                            {
                                try
                                {
                                    if (!p.HasExited)
                                    {
                                        p.Kill(entireProcessTree: true);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    // reasonable backoff to reduce churn and UI side effects
                    try { await System.Threading.Tasks.Task.Delay(3000, token); } catch { }
                }
                _log.Info("OneDrive blocker stopped.");
            }, token);
        }

        private void TryService(string serviceName, bool disable)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var desired = disable ? ServiceControllerStatus.Stopped : ServiceControllerStatus.Running;
                var action = disable ? "stop" : "start";

                // If already in desired state, nothing to do
                sc.Refresh();
                if (sc.Status == desired) return;

                // Try to set StartType first to reduce auto-restart fights (admin required)
                if (AdminUtil.IsAdministrator())
                {
                    var startType = disable ? "disabled" : "auto";
                    if (!TrySetServiceStartType(serviceName, startType, out var cfgErr))
                    {
                        _log.Warn($"sc config {serviceName} start= {startType} failed: {cfgErr}");
                    }
                }

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        if (disable)
                            sc.Stop();
                        else
                            sc.Start();

                        if (WaitForStatusWithRefresh(sc, desired, TimeSpan.FromSeconds(7)))
                        {
                            _log.Info($"Service {serviceName} {action}ed (attempt {attempt}).");
                            return;
                        }
                        else
                        {
                            _log.Warn($"Service {serviceName} did not reach state {desired} within timeout (attempt {attempt}).");
                        }
                    }
                    catch (Exception exAttempt)
                    {
                        _log.Warn($"Service {serviceName} {action} attempt {attempt} failed: {exAttempt.Message}");
                    }

                    // brief backoff before retry
                    System.Threading.Thread.Sleep(400);
                    sc.Refresh();
                }

                _log.Warn($"Service '{serviceName}' could not {action} after retries.");
            }
            catch (Exception ex)
            {
                _log.Warn($"Service '{serviceName}' change failed: {ex.Message}");
            }
        }

        private bool TrySetServiceStartType(string serviceName, string startType, out string? error)
        {
            // Use sc.exe to change start type: start= disabled | demand | auto
            // Note the required space after 'start=' when calling sc via cmd.
            // We'll invoke through 'sc.exe' directly with proper quoting.
            var args = $"config {serviceName} start= {startType}";
            return AdminUtil.TryRunProcess("sc.exe", args, 8000, out error);
        }

        private static bool WaitForStatusWithRefresh(ServiceController sc, ServiceControllerStatus desired, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                sc.Refresh();
                if (sc.Status == desired) return true;
                System.Threading.Thread.Sleep(200);
            }
            return false;
        }

        private bool TrySetDefenderRealtime(bool disable, out string? error)
        {
            // Requires admin, Windows Defender cmdlet available on Windows 10+
            var flag = disable ? "$true" : "$false";
            var ps = $"Try {{ Set-MpPreference -DisableRealtimeMonitoring {flag}; Write-Output 'OK' }} Catch {{ Write-Error $_; exit 1 }}";
            var arg = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"";
            var ok = AdminUtil.TryRunProcess("powershell", arg, 12000, out error);
            if (!ok)
            {
                error = string.IsNullOrWhiteSpace(error) ? "Set-MpPreference failed (tamper protection or policy?)" : error;
            }
            return ok;
        }

        private bool TrySetGameDvr(bool disable, out string? error)
        {
            // Tweak multiple knobs for reliability. HKCU applies per-user; HKLM policy enforces globally.
            // Values:
            // - HKCU\\System\\GameConfigStore GameDVR_Enabled = 0/1
            // - HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR AppCaptureEnabled = 0/1
            // - HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR AllowGameDVR = 0/1 (1=allow, 0=disable)
            error = null;
            try
            {
                var ok1 = RegistryUtil.TrySetDword(Microsoft.Win32.RegistryHive.CurrentUser, "System/GameConfigStore", "GameDVR_Enabled", disable ? 0 : 1, out var e1);
                var ok2 = RegistryUtil.TrySetDword(Microsoft.Win32.RegistryHive.CurrentUser, "SOFTWARE/Microsoft/Windows/CurrentVersion/GameDVR", "AppCaptureEnabled", disable ? 0 : 1, out var e2);
                var ok3 = RegistryUtil.TrySetDword(Microsoft.Win32.RegistryHive.LocalMachine, "SOFTWARE/Policies/Microsoft/Windows/GameDVR", "AllowGameDVR", disable ? 0 : 1, out var e3);
                if (!(ok1 && ok2 && ok3))
                {
                    error = string.Join(" | ", new[] { e1, e2, e3 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool TryToggleDefenderScheduledTasks(bool disable, out string? error)
        {
            // Use schtasks to enable/disable built-in Defender tasks. Admin typically required.
            // Tasks under: \\Microsoft\\Windows\\Windows Defender\\
            //  - Windows Defender Scheduled Scan
            //  - Windows Defender Cache Maintenance
            //  - Windows Defender Cleanup
            //  - Windows Defender Verification
            error = null;
            var tasks = new[]
            {
                "\\\\Microsoft\\\\Windows\\\\Windows Defender\\\\Windows Defender Scheduled Scan",
                "\\\\Microsoft\\\\Windows\\\\Windows Defender\\\\Windows Defender Cache Maintenance",
                "\\\\Microsoft\\\\Windows\\\\Windows Defender\\\\Windows Defender Cleanup",
                "\\\\Microsoft\\\\Windows\\\\Windows Defender\\\\Windows Defender Verification"
            };
            foreach (var tn in tasks)
            {
                var args = $"/Change /TN \"{tn}\" /{(disable ? "Disable" : "Enable")}";
                if (!AdminUtil.TryRunProcess("schtasks", args, 8000, out var err))
                {
                    // accumulate but continue
                    error = string.IsNullOrWhiteSpace(error) ? err : ($"{error}; {err}");
                }
            }
            return string.IsNullOrWhiteSpace(error);
        }

        private bool TryOneDrivePause(out string? error)
        {
            // OneDrive pause is only honored by a running user-session instance.
            // Try multiple paths and arguments.
            error = null;
            // If OneDrive is not running, do not start it just to pause; consider this a no-op success
            try
            {
                var existing = System.Diagnostics.Process.GetProcessesByName("OneDrive");
                if (existing == null || existing.Length == 0)
                {
                    return true;
                }
            }
            catch { }
            var candidates = new[]
            {
                Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Microsoft/OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles%/Microsoft OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%/Microsoft OneDrive/OneDrive.exe"),
                "OneDrive.exe"
            };
            string? lastErr = null;
            foreach (var exe in candidates)
            {
                // Try pause with and without minutes parameter
                if (AdminUtil.TryRunProcess(exe, "/pause 3600", 5000, out error)) return true;
                lastErr = error;
                if (AdminUtil.TryRunProcess(exe, "/pause", 5000, out error)) return true;
                lastErr = error;
            }
            error = lastErr ?? "No OneDrive executable found";
            return false;
        }

        private void TryToggleOneDriveRunAutostart(bool enable)
        {
            try
            {
                const string runKeyCU = "SOFTWARE/Microsoft/Windows/CurrentVersion/Run";
                const string runKeyLM = "SOFTWARE/Microsoft/Windows/CurrentVersion/Run";
                const string valueName = "OneDrive";
                if (enable)
                {
                    if (_odRunWasPresent && !string.IsNullOrWhiteSpace(_odRunBackup))
                    {
                        if (!RegistryUtil.TrySetString(Microsoft.Win32.RegistryHive.CurrentUser, runKeyCU, valueName, _odRunBackup!, out var err))
                            _log.Warn($"Restore OneDrive Run key failed: {err}");
                        else
                            _log.Info("OneDrive Run key restored.");
                    }
                    if (_odRunWasPresentLM && !string.IsNullOrWhiteSpace(_odRunBackupLM))
                    {
                        if (!RegistryUtil.TrySetString(Microsoft.Win32.RegistryHive.LocalMachine, runKeyLM, valueName, _odRunBackupLM!, out var err2))
                            _log.Warn($"Restore OneDrive Run key (HKLM) failed: {err2}");
                        else
                            _log.Info("OneDrive Run key (HKLM) restored.");
                    }
                }
                else
                {
                    // Backup current value then delete to prevent auto-start
                    using var baseKeyCU = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
                    using var keyCU = baseKeyCU.OpenSubKey(runKeyCU, writable: true);
                    object? valCU = keyCU?.GetValue(valueName);
                    _odRunWasPresent = valCU != null;
                    _odRunBackup = valCU?.ToString();
                    if (!RegistryUtil.TryDeleteValue(Microsoft.Win32.RegistryHive.CurrentUser, runKeyCU, valueName, out var err))
                        _log.Warn($"Remove OneDrive Run key failed: {err}");
                    else
                        _log.Info("OneDrive Run autostart disabled.");

                    // Also disable HKLM Run if present (per-machine installs)
                    using var baseKeyLM = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                    using var keyLM = baseKeyLM.OpenSubKey(runKeyLM, writable: true);
                    object? valLM = keyLM?.GetValue(valueName);
                    _odRunWasPresentLM = valLM != null;
                    _odRunBackupLM = valLM?.ToString();
                    if (!RegistryUtil.TryDeleteValue(Microsoft.Win32.RegistryHive.LocalMachine, runKeyLM, valueName, out var errLM))
                        _log.Warn($"Remove OneDrive Run key (HKLM) failed: {errLM}");
                    else
                        _log.Info("OneDrive Run autostart (HKLM) disabled.");
                }
            }
            catch { }
        }

        private bool TryToggleOneDriveTasks(bool disable, out string? error)
        {
            // Task names vary by installation; try common ones and ignore errors
            error = null;
            var tasks = new[]
            {
                "\\Microsoft\\OneDrive\\OneDrive Standalone Update Task",
                "\\Microsoft\\OneDrive\\OneDrive Per-Machine Standalone Update Task",
                "\\Microsoft\\OneDrive\\OneDrive Updater Task"
            };
            bool okAny = false;
            foreach (var tn in tasks)
            {
                var args = $"/Change /TN \"{tn}\" /{(disable ? "Disable" : "Enable")}";
                if (AdminUtil.TryRunProcess("schtasks", args, 7000, out var err))
                {
                    okAny = true;
                }
                else
                {
                    // accumulate but non-fatal
                    error = string.IsNullOrWhiteSpace(error) ? err : ($"{error}; {err}");
                }
            }
            try
            {
                // Broad catch-all: enumerate tasks and disable/enable any containing OneDrive in the path
                if (AdminUtil.TryRunProcess("schtasks", "/Query /FO CSV /V", 12000, out var output) && !string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1)) // skip header
                    {
                        try
                        {
                            var parts = line.Split(',').Select(s => s.Trim('"')).ToArray();
                            if (parts.Length < 2) continue;
                            var taskName = parts[0];
                            if (taskName.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var arg = $"/Change /TN \"{taskName}\" /{(disable ? "Disable" : "Enable")}";
                                if (AdminUtil.TryRunProcess("schtasks", arg, 7000, out var err2)) okAny = true;
                                else error = string.IsNullOrWhiteSpace(error) ? err2 : ($"{error}; {err2}");
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return okAny;
        }

        private bool TryOneDriveResume(out string? error)
        {
            error = null;
            var candidates = new[]
            {
                Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Microsoft/OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles%/Microsoft OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%/Microsoft OneDrive/OneDrive.exe"),
                "OneDrive.exe"
            };
            foreach (var exe in candidates)
            {
                bool isAdmin = AdminUtil.IsAdministrator();
                // Ensure a user-context instance. If elevated, start via explorer.exe to avoid admin token.
                if (isAdmin)
                {
                    TryLaunchUnelevated(exe, "/background");
                }
                else
                {
                    try { AdminUtil.TryRunProcess(exe, "/background", 5000, out _); } catch { }
                }
                System.Threading.Thread.Sleep(800);
                if (AdminUtil.TryRunProcess(exe, "/resume", 5000, out error)) return true;
            }
            return false;
        }

        private bool TryLaunchUnelevated(string exePath, string args)
        {
            try
            {
                // Use Task Scheduler to start OneDrive under the user context (limited rights), then delete the task
                var quotedExe = exePath.Contains(' ') ? $"\"{exePath}\"" : exePath;
                var tr = $"{quotedExe} {args}".Trim();
                var taskName = "DJWinOptimizer_StartOneDrive_Once";
                var startTime = DateTime.Now.AddSeconds(10).ToString("HH:mm");
                var createArgs = $"/Create /F /SC ONCE /ST {startTime} /RL LIMITED /TN {taskName} /TR \"{tr}\"";
                if (!AdminUtil.TryRunProcess("schtasks", createArgs, 8000, out _)) return false;
                // Run immediately; /Run ignores ST and starts now
                AdminUtil.TryRunProcess("schtasks", $"/Run /TN {taskName}", 5000, out _);
                // Best-effort delete after brief delay
                System.Threading.Thread.Sleep(1000);
                AdminUtil.TryRunProcess("schtasks", $"/Delete /F /TN {taskName}", 5000, out _);
                return true;
            }
            catch { return false; }
        }

        private bool TryOneDriveStop(out string? error)
        {
            // Prefer graceful shutdown
            error = null;
            var candidates = new[]
            {
                Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Microsoft/OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles%/Microsoft OneDrive/OneDrive.exe"),
                Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%/Microsoft OneDrive/OneDrive.exe"),
                "OneDrive.exe"
            };
            foreach (var exe in candidates)
            {
                if (AdminUtil.TryRunProcess(exe, "/shutdown", 6000, out error))
                {
                    System.Threading.Thread.Sleep(800);
                    if (System.Diagnostics.Process.GetProcessesByName("OneDrive").Length == 0) return true;
                }
            }
            // Fallback: taskkill (may require admin if different session)
            if (AdminUtil.TryRunProcess("taskkill", "/IM OneDrive.exe /F", 6000, out error))
            {
                System.Threading.Thread.Sleep(500);
                return System.Diagnostics.Process.GetProcessesByName("OneDrive").Length == 0;
            }
            return false;
        }
    }
}
