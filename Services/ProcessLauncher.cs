using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;

namespace DJWinOptimizer.Services
{
    public class ProcessLauncher : IProcessLauncher
    {
        private readonly ILogger _log;
        public ProcessLauncher(ILogger log) { _log = log; }

        public void Launch(IEnumerable<ProgramAction> actions)
        {
            foreach (var a in actions)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(a.Path)) { _log.Warn("Launch skipped: empty path"); continue; }

                    var path = a.Path;
                    var exists = File.Exists(path);
                    // Allow launching by just exe name in PATH; File.Exists would fail, but Process will try. We still keep a warning.
                    if (!exists)
                    {
                        _log.Warn($"Launch file not found on disk: '{path}'. Attempting to start by name via shell.");
                    }

                    // Determine process name to check
                    string procNameToCheck = !string.IsNullOrWhiteSpace(a.CheckProcessName)
                        ? a.CheckProcessName!
                        : SafeGetFileNameWithoutExt(path);
                    procNameToCheck = TrimExe(procNameToCheck);

                    if (a.SkipIfRunning && IsProcessRunning(procNameToCheck))
                    {
                        _log.Info($"Skip launch for '{path}' because process '{procNameToCheck}' is already running.");
                        if (a.DelayMsAfterStart > 0) SafeSleep(a.DelayMsAfterStart);
                        continue;
                    }

                    // Build ProcessStartInfo with script support
                    var psi = BuildStartInfo(path, a.Args, a.WorkingDirectory);
                    var p = Process.Start(psi);
                    if (p == null) { _log.Warn($"Failed to start '{path}'"); continue; }

                    if (a.Wait)
                    {
                        // Legacy: wait briefly for exit (kept for compatibility)
                        try { p.WaitForExit(5000); } catch { }
                    }

                    if (a.WaitForRunningTimeoutMs > 0 && !string.IsNullOrWhiteSpace(procNameToCheck))
                    {
                        var ok = WaitUntilRunning(procNameToCheck, a.WaitForRunningTimeoutMs);
                        if (!ok)
                            _log.Warn($"Process '{procNameToCheck}' did not appear within {a.WaitForRunningTimeoutMs}ms after launching '{path}'.");
                    }

                    _log.Info($"Launched '{path}' {a.Args}");
                    if (a.DelayMsAfterStart > 0) SafeSleep(a.DelayMsAfterStart);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Launch error for '{a.Path}': {ex.Message}");
                }
            }
        }

        public void Kill(IEnumerable<ProgramAction> actions)
        {
            foreach (var a in actions)
            {
                try
                {
                    var name = a.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(entireProcessTree: true); _log.Info($"Killed {p.ProcessName} ({p.Id})"); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"Kill error for '{a.ProcessName}': {ex.Message}");
                }
            }
        }

        private static string TrimExe(string name)
            => string.IsNullOrWhiteSpace(name) ? name : (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name);

        private static string SafeGetFileNameWithoutExt(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return string.Empty;
                var fn = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fn)) return string.Empty;
                return fn.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fn[..^4] : Path.GetFileNameWithoutExtension(fn);
            }
            catch { return string.Empty; }
        }

        private static bool IsProcessRunning(string processNameNoExt)
        {
            if (string.IsNullOrWhiteSpace(processNameNoExt)) return false;
            try { return Process.GetProcessesByName(processNameNoExt).Length > 0; } catch { return false; }
        }

        private static bool WaitUntilRunning(string processNameNoExt, int timeoutMs)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var step = 100;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (IsProcessRunning(processNameNoExt)) return true;
                    System.Threading.Thread.Sleep(step);
                }
            }
            catch { }
            return false;
        }

        private static void SafeSleep(int ms)
        {
            try { System.Threading.Thread.Sleep(Math.Max(0, ms)); } catch { }
        }

        private static ProcessStartInfo BuildStartInfo(string path, string? args, string? workingDir)
        {
            var ext = string.Empty;
            try { ext = Path.GetExtension(path) ?? string.Empty; } catch { }
            ext = ext.ToLowerInvariant();
            var psi = new ProcessStartInfo();
            psi.UseShellExecute = false;
            if (!string.IsNullOrWhiteSpace(workingDir)) psi.WorkingDirectory = workingDir!;

            switch (ext)
            {
                case ".bat":
                case ".cmd":
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = $"/c \"{path}\" {args}".Trim();
                    break;
                case ".ps1":
                    psi.FileName = "powershell.exe";
                    psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\" {args}".Trim();
                    break;
                default:
                    psi.FileName = path;
                    psi.Arguments = args ?? string.Empty;
                    break;
            }
            return psi;
        }
    }
}
