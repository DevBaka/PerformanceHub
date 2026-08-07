using System.Diagnostics;
using System.ServiceProcess;
using ProfileDeck.Core.Logging;
using ProfileDeck.Core.Models;

namespace ProfileDeck.Core.Services;

public sealed class ServiceControlService
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
        var res = RunSc($"qc {serviceName}");
        if (res == null) return null;
        if (res.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "Disabled";
        if (res.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase)) return "Automatic";
        if (res.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "Manual";
        return null;
    }

    public ServiceControllerStatus? GetStatus(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            return sc.Status;
        }
        catch { return null; }
    }

    public IReadOnlyList<(string Name, string DisplayName, ServiceControllerStatus Status, string? StartupType)> ListAll()
    {
        var result = new List<(string, string, ServiceControllerStatus, string?)>();
        foreach (var sc in ServiceController.GetServices())
        {
            try { result.Add((sc.ServiceName, sc.DisplayName, sc.Status, GetStartupType(sc.ServiceName))); }
            catch { /* skip services we can't query */ }
        }
        return result;
    }

    private void SetRunning(string serviceName, bool start, ILogSink log)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var desired = start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
            sc.Refresh();
            if (sc.Status == desired) return;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (start) sc.Start(); else sc.Stop();
                    sc.WaitForStatus(desired, TimeSpan.FromSeconds(7));
                    log.Info($"Dienst '{serviceName}' {(start ? "gestartet" : "gestoppt")}.");
                    return;
                }
                catch (Exception ex)
                {
                    log.Warn($"Dienst '{serviceName}' {(start ? "starten" : "stoppen")} (Versuch {attempt}) fehlgeschlagen: {ex.Message}");
                    Thread.Sleep(400);
                    sc.Refresh();
                }
            }
            log.Warn($"Dienst '{serviceName}' konnte nach mehreren Versuchen nicht {(start ? "gestartet" : "gestoppt")} werden.");
        }
        catch (Exception ex)
        {
            log.Warn($"Dienst '{serviceName}' nicht gefunden oder unzugänglich: {ex.Message}");
        }
    }

    private void TrySetStartupType(string serviceName, ServiceStartupType type, ILogSink log)
    {
        var arg = type switch
        {
            ServiceStartupType.Automatic => "auto",
            ServiceStartupType.Manual => "demand",
            ServiceStartupType.Disabled => "disabled",
            _ => "demand",
        };
        var res = RunSc($"config {serviceName} start= {arg}");
        if (res == null)
            log.Warn($"Starttyp für Dienst '{serviceName}' konnte nicht auf '{type}' gesetzt werden (Admin-Rechte nötig?).");
    }

    private static string? RunSc(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            return p.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }
}
