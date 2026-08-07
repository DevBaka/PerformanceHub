using System.Diagnostics;

namespace Deskrig.Core.Interop;

/// <summary>Small shared helper for the various Linux backends that drive their platform state entirely
/// through CLI tools (xrandr, pactl, powerprofilesctl, systemctl) - mirrors the ad-hoc process-running
/// pattern the Windows side already uses for powercfg.exe/sc.exe, just shared instead of duplicated.</summary>
internal static class ProcessRunner
{
    public static (int ExitCode, string StdOut, string StdErr) Run(string file, string args, int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var stdOut = p.StandardOutput.ReadToEnd();
            var stdErr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (-1, stdOut, "Timeout");
            }
            return (p.ExitCode, stdOut, stdErr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>True if the given CLI tool exists on PATH (checked once via `which`-style lookup).</summary>
    public static bool ToolExists(string file)
    {
        try
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            return paths.Any(dir => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, file)));
        }
        catch { return false; }
    }
}
