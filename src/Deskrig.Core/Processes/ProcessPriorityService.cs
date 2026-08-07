using System.Diagnostics;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

/// <summary>Sets process priority for already-running processes matched by name (independent of who launched them).</summary>
public sealed class ProcessPriorityService
{
    public void Apply(IEnumerable<ProcessPriorityEntry> entries, ILogSink log)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ProcessName)) continue;
            var name = Path.GetFileNameWithoutExtension(entry.ProcessName);
            var procs = SafeGetProcessesByName(name);
            if (procs.Length == 0) continue;

            var desired = ProcessLauncherService.ParsePriority(entry.Priority);
            foreach (var p in procs)
            {
                try
                {
                    if (p.HasExited) continue;
                    p.PriorityClass = desired;
                    log.Info($"Priorität {desired} gesetzt für {p.ProcessName} (PID {p.Id}).");
                }
                catch (Exception ex)
                {
                    log.Warn($"Priorität für {p.ProcessName} (PID {p.Id}) konnte nicht gesetzt werden: {ex.Message}");
                }
            }
        }
    }

    private static Process[] SafeGetProcessesByName(string name)
    {
        try { return Process.GetProcessesByName(name); } catch { return Array.Empty<Process>(); }
    }
}
