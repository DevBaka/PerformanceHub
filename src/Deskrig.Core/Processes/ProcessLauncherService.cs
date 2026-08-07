using System.Diagnostics;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

public sealed class ProcessLauncherService
{
    public void Launch(IEnumerable<LaunchProgramAction> actions, ILogSink log)
    {
        foreach (var a in actions)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(a.Path))
                {
                    log.Warn("Programmstart übersprungen: leerer Pfad.");
                    continue;
                }

                var checkName = ProcessName(a.Path);
                if (a.SkipIfAlreadyRunning && IsRunning(checkName))
                {
                    log.Info($"'{a.Path}' übersprungen, Prozess '{checkName}' läuft bereits.");
                    Delay(a.DelayAfterMs);
                    continue;
                }

                var psi = BuildStartInfo(a);
                var process = Process.Start(psi);
                if (process == null)
                {
                    log.Warn($"Start fehlgeschlagen: '{a.Path}'.");
                    continue;
                }

                if (a.WaitForWindow)
                {
                    if (!WaitForWindow(process, checkName, a.WaitForWindowTimeoutMs))
                        log.Warn($"'{checkName}' hat innerhalb von {a.WaitForWindowTimeoutMs}ms kein Fenster geöffnet.");
                }

                if (!string.IsNullOrWhiteSpace(a.Priority))
                    TrySetPriority(process, a.Priority, log);

                log.Info($"Gestartet: '{a.Path}' {a.Arguments}".TrimEnd());
                Delay(a.DelayAfterMs);
            }
            catch (Exception ex)
            {
                log.Warn($"Fehler beim Starten von '{a.Path}': {ex.Message}");
            }
        }
    }

    public void Kill(IEnumerable<string> processNames, ILogSink log)
    {
        foreach (var raw in processNames)
        {
            var name = TrimExe(raw);
            if (string.IsNullOrWhiteSpace(name)) continue;
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length == 0) continue;
                foreach (var p in procs)
                {
                    try { p.Kill(entireProcessTree: true); log.Info($"Beendet: {p.ProcessName} (PID {p.Id})."); }
                    catch (Exception ex) { log.Warn($"Konnte '{p.ProcessName}' nicht beenden: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                log.Warn($"Fehler beim Beenden von '{name}': {ex.Message}");
            }
        }
    }

    private static ProcessStartInfo BuildStartInfo(LaunchProgramAction a)
    {
        var ext = SafeExtension(a.Path);
        var psi = new ProcessStartInfo();

        if (OperatingSystem.IsWindows())
        {
            switch (ext)
            {
                case ".bat":
                case ".cmd":
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = $"/c \"{a.Path}\" {a.Arguments}".Trim();
                    break;
                case ".ps1":
                    psi.FileName = "powershell.exe";
                    psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{a.Path}\" {a.Arguments}".Trim();
                    break;
                default:
                    psi.FileName = a.Path;
                    psi.Arguments = a.Arguments ?? string.Empty;
                    break;
            }
        }
        else
        {
            switch (ext)
            {
                case ".sh":
                    psi.FileName = "/bin/sh";
                    psi.Arguments = $"\"{a.Path}\" {a.Arguments}".Trim();
                    break;
                case ".ps1":
                    // pwsh (PowerShell Core) if installed - otherwise this launch step just fails cleanly
                    // and gets logged like any other missing-executable case.
                    psi.FileName = "pwsh";
                    psi.Arguments = $"-NoProfile -File \"{a.Path}\" {a.Arguments}".Trim();
                    break;
                default:
                    psi.FileName = a.Path;
                    psi.Arguments = a.Arguments ?? string.Empty;
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(a.WorkingDirectory))
            psi.WorkingDirectory = a.WorkingDirectory;
        else if (!string.IsNullOrWhiteSpace(a.Path) && File.Exists(a.Path))
            psi.WorkingDirectory = Path.GetDirectoryName(a.Path) ?? "";

        if (a.RunAsAdmin)
        {
            if (OperatingSystem.IsWindows())
            {
                psi.UseShellExecute = true;
                psi.Verb = "runas";
            }
            else
            {
                // pkexec (polkit) is the Linux equivalent of a UAC elevation prompt - wrap whatever we were
                // about to run instead of running it directly.
                var innerFile = psi.FileName;
                var innerArgs = psi.Arguments;
                psi.FileName = "pkexec";
                psi.Arguments = string.IsNullOrWhiteSpace(innerArgs) ? innerFile : $"{innerFile} {innerArgs}";
                psi.UseShellExecute = false;
            }
        }
        else
        {
            psi.UseShellExecute = false;
        }

        return psi;
    }

    private static void TrySetPriority(Process process, string priority, ILogSink log)
    {
        try
        {
            process.Refresh();
            if (process.HasExited) return;
            process.PriorityClass = ParsePriority(priority);
        }
        catch (Exception ex)
        {
            log.Warn($"Priorität '{priority}' konnte nicht gesetzt werden: {ex.Message}");
        }
    }

    internal static ProcessPriorityClass ParsePriority(string priority) => priority switch
    {
        "Realtime" => ProcessPriorityClass.RealTime,
        "High" => ProcessPriorityClass.High,
        "AboveNormal" => ProcessPriorityClass.AboveNormal,
        "BelowNormal" => ProcessPriorityClass.BelowNormal,
        "Idle" => ProcessPriorityClass.Idle,
        _ => ProcessPriorityClass.Normal,
    };

    private static bool WaitForWindow(Process process, string processName, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    if (p.MainWindowHandle != IntPtr.Zero) return true;
                }
                if (process.HasExited) return false;
            }
            catch { /* process may have exited between calls */ }
            Thread.Sleep(200);
        }
        return false;
    }

    private static bool IsRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        try { return Process.GetProcessesByName(processName).Length > 0; } catch { return false; }
    }

    private static void Delay(int ms)
    {
        if (ms > 0) Thread.Sleep(ms);
    }

    private static string ProcessName(string path) => TrimExe(SafeFileNameWithoutExtension(path));

    private static string SafeFileNameWithoutExtension(string path)
    {
        try { return Path.GetFileNameWithoutExtension(path) ?? ""; } catch { return ""; }
    }

    private static string SafeExtension(string path)
    {
        try { return Path.GetExtension(path)?.ToLowerInvariant() ?? ""; } catch { return ""; }
    }

    private static string TrimExe(string name)
        => string.IsNullOrWhiteSpace(name) ? name : (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name);
}
