using System;
using System.Diagnostics;
using System.Security.Principal;

namespace PerformanceHub.Utils
{
    public static class AdminUtil
    {
        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool TryRunProcess(string fileName, string arguments, int timeoutMs, out string? error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) { error = "Failed to start process"; return false; }
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    error = "Timed out";
                    return false;
                }
                if (p.ExitCode != 0)
                {
                    error = p.StandardError.ReadToEnd();
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
    }
}
