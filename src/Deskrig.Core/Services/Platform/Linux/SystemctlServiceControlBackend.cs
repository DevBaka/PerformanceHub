using Deskrig.Core.Interop;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Services;

/// <summary>
/// Service-control backend for Linux via `systemctl` (system scope). "ServiceName" in a profile is a
/// systemd unit name (e.g. "bluetooth.service"). State changes that fail for lack of privilege are retried
/// once through `pkexec` (the closest Linux equivalent to the Windows admin-manifest requirement - here
/// scoped to just the action that actually needs it, instead of the whole app running elevated).
/// </summary>
internal sealed class SystemctlServiceControlBackend : IServiceControlBackend
{
    public void Apply(IEnumerable<ServiceAction> actions, ILogSink log)
    {
        foreach (var a in actions)
        {
            if (string.IsNullOrWhiteSpace(a.ServiceName)) continue;

            if (a.StartupType.HasValue)
                TrySetStartupType(a.ServiceName, a.StartupType.Value, log);

            switch (a.DesiredState)
            {
                case ServiceDesiredState.Start: SetRunning(a.ServiceName, true, log); break;
                case ServiceDesiredState.Stop: SetRunning(a.ServiceName, false, log); break;
                case ServiceDesiredState.NoChange: break;
            }
        }
    }

    public string? GetStartupType(string serviceName)
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("systemctl", $"is-enabled {Quote(serviceName)}");
        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdOut)) return null;
        return stdOut.Trim().ToLowerInvariant() switch
        {
            "enabled" or "enabled-runtime" => "Automatic",
            "disabled" => "Manual",
            "masked" or "masked-runtime" => "Disabled",
            _ => null,
        };
    }

    public ServiceRunState? GetStatus(string serviceName)
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("systemctl", $"is-active {Quote(serviceName)}");
        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdOut)) return null;
        return stdOut.Trim().ToLowerInvariant() switch
        {
            "active" => ServiceRunState.Running,
            "inactive" or "failed" => ServiceRunState.Stopped,
            "activating" => ServiceRunState.StartPending,
            "deactivating" => ServiceRunState.StopPending,
            _ => ServiceRunState.Unknown,
        };
    }

    public IReadOnlyList<(string Name, string DisplayName, ServiceRunState Status, string? StartupType)> ListAll()
    {
        var result = new List<(string, string, ServiceRunState, string?)>();
        var (exitCode, stdOut, _) = ProcessRunner.Run("systemctl", "list-units --type=service --all --no-legend --no-pager --plain", timeoutMs: 15000);
        if (exitCode != 0) return result;

        foreach (var line in stdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            var unit = parts[0];
            var active = parts[2];
            var description = parts.Length >= 5 ? parts[4] : unit;

            var status = active.ToLowerInvariant() switch
            {
                "active" => ServiceRunState.Running,
                "activating" => ServiceRunState.StartPending,
                "deactivating" => ServiceRunState.StopPending,
                _ => ServiceRunState.Stopped,
            };
            result.Add((unit, description, status, GetStartupType(unit)));
        }
        return result;
    }

    private static void SetRunning(string serviceName, bool start, ILogSink log)
    {
        var verb = start ? "start" : "stop";
        var (exitCode, _, stdErr) = ProcessRunner.Run("systemctl", $"{verb} {Quote(serviceName)}");
        if (exitCode == 0)
        {
            log.Info($"Dienst '{serviceName}' {(start ? "gestartet" : "gestoppt")}.");
            return;
        }

        if (LooksLikePermissionError(stdErr) && ProcessRunner.ToolExists("pkexec"))
        {
            (exitCode, _, stdErr) = ProcessRunner.Run("pkexec", $"systemctl {verb} {Quote(serviceName)}", timeoutMs: 30000);
            if (exitCode == 0)
            {
                log.Info($"Dienst '{serviceName}' {(start ? "gestartet" : "gestoppt")} (pkexec).");
                return;
            }
        }

        log.Warn($"Dienst '{serviceName}' konnte nicht {(start ? "gestartet" : "gestoppt")} werden: {stdErr}".TrimEnd());
    }

    private static void TrySetStartupType(string serviceName, ServiceStartupType type, ILogSink log)
    {
        var verb = type switch
        {
            ServiceStartupType.Automatic => "enable",
            ServiceStartupType.Manual => "disable",
            ServiceStartupType.Disabled => "mask",
            _ => "disable",
        };
        var (exitCode, _, stdErr) = ProcessRunner.Run("systemctl", $"{verb} {Quote(serviceName)}");
        if (exitCode == 0) return;

        if (LooksLikePermissionError(stdErr) && ProcessRunner.ToolExists("pkexec"))
        {
            (exitCode, _, stdErr) = ProcessRunner.Run("pkexec", $"systemctl {verb} {Quote(serviceName)}", timeoutMs: 30000);
            if (exitCode == 0) return;
        }

        log.Warn($"Starttyp für Dienst '{serviceName}' konnte nicht auf '{type}' gesetzt werden: {stdErr}".TrimEnd());
    }

    private static bool LooksLikePermissionError(string stdErr)
        => stdErr.Contains("Interactive authentication required", StringComparison.OrdinalIgnoreCase)
        || stdErr.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
        || stdErr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase);

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
