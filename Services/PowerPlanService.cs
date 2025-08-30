using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DJWinOptimizer.Core.Interfaces;

namespace DJWinOptimizer.Services
{
    public class PowerPlanService : IPowerPlanService
    {
        private readonly ILogger _log;
        public PowerPlanService(ILogger log) { _log = log; }

        public bool TrySetActive(string? guid, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(guid)) return true; // nothing to change
            try
            {
                // Normalize GUID: extract first 36-char GUID if extra braces/text are present
                var m = Regex.Match(guid, "([0-9a-fA-F-]{36})");
                if (m.Success) guid = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(guid)) { error = "Invalid GUID"; return false; }

                // Ensure the scheme exists on this system (some defaults are hidden or missing); try to import via duplicatescheme
                EnsurePlanExists(guid);
                var syntaxes = new[] { "/S {0}", "-setactive {0}", "/setactive {0}" };
                var powercfg = GetPowerCfgPath();
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    foreach (var fmt in syntaxes)
                    {
                        var args = string.Format(fmt, guid);
                        var res = Proc(powercfg, args, 8000);
                        if (res.exit == 0)
                        {
                            // Allow some time for the change to propagate, verify a few times
                            string? active = null;
                            for (int verify = 0; verify < 3; verify++)
                            {
                                if (verify > 0) System.Threading.Thread.Sleep(150);
                                active = GetActiveGuid();
                                if (active != null && string.Equals(active, guid, StringComparison.OrdinalIgnoreCase))
                                {
                                    _log.Info($"Power plan set to {guid} using '{powercfg} {args}' (attempt {attempt}, verify {verify+1}).");
                                    return true;
                                }
                            }
                            _log.Warn($"{powercfg} {args} reported success but active GUID is '{active ?? "<null>"}' (wanted {guid}).");
                        }
                        else
                        {
                            _log.Warn($"{powercfg} {args} failed (attempt {attempt}) ec={res.exit}: {res.errp}\n{res.outp}");
                        }
                    }
                    System.Threading.Thread.Sleep(350);
                }
                error = "powercfg failed or active scheme did not change after retries";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("Failed to set power plan", ex);
                return false;
            }
        }

        public bool TryClone(string baseGuid, string newName, out string? newGuid, out string? error)
        {
            newGuid = null; error = null;
            try
            {
                // powercfg -duplicatescheme baseGuid
                var dup = Proc(GetPowerCfgPath(), $"-duplicatescheme {baseGuid}");
                if (dup.exit != 0) { error = dup.outp; return false; }
                var match = Regex.Match(dup.outp + dup.errp, "([0-9a-fA-F-]{36})");
                if (match.Success) newGuid = match.Groups[1].Value;
                if (newGuid == null) { error = "Could not parse GUID"; return false; }
                // rename
                var ren = Proc(GetPowerCfgPath(), $"-changename {newGuid} \"{newName}\"");
                if (ren.exit != 0) { error = ren.outp + ren.errp; return false; }
                _log.Info($"Cloned power plan {baseGuid} -> {newGuid} '{newName}'");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("Clone power plan failed", ex);
                return false;
            }
        }

        public string? GetActiveGuid()
        {
            try
            {
                var res = Proc(GetPowerCfgPath(), "/GetActiveScheme", 8000);
                var match = Regex.Match(res.outp + res.errp, "([0-9a-fA-F-]{36})");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public IEnumerable<(string Guid, string Name, bool Active)> GetAvailablePlans()
        {
            var list = new List<(string Guid, string Name, bool Active)>();
            try
            {
                var res = Proc(GetPowerCfgPath(), "/L", 8000);
                // Locale-agnostic parsing: look for GUID + name in parentheses + optional '*'
                // Example (en): "Power Scheme GUID: <GUID>  (High performance) *"
                // Example (de): "Energieschema-GUID: <GUID>  (<Name>) *"
                var text = (res.outp + "\n" + res.errp);
                foreach (var line in text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Regex.Match(line, @"([0-9a-fA-F-]{36})\s*\(([^\)]+)\)\s*(\*)?");
                    if (m.Success)
                    {
                        var guid = m.Groups[1].Value;
                        var name = m.Groups[2].Value.Trim();
                        var active = m.Groups[3].Success;
                        list.Add((guid, name, active));
                    }
                }
                if (list.Count == 0)
                {
                    _log.Warn("powercfg /L returned no parsable schemes. Output:" + Environment.NewLine + text);
                }
            }
            catch { }
            return list;
        }

        private static (int exit, string outp, string errp) Proc(string file, string args, int timeoutMs = 4000)
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            var e = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);
            return (p.ExitCode, o, e);
        }

        private void EnsurePlanExists(string guid)
        {
            try
            {
                var exists = false;
                foreach (var (g, _, _) in GetAvailablePlans())
                {
                    if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                }
                if (exists) return;
                var res = Proc(GetPowerCfgPath(), $"-duplicatescheme {guid}", 8000);
                if (res.exit == 0)
                {
                    _log.Info($"Imported missing power scheme {guid} via powercfg -duplicatescheme.");
                }
                else
                {
                    _log.Warn($"Failed to import missing scheme {guid}: {res.errp}\n{res.outp}");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"EnsurePlanExists error for {guid}: {ex.Message}");
            }
        }

        private static string GetPowerCfgPath()
        {
            var windir = Environment.GetEnvironmentVariable("windir") ?? "C\\Windows";
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                // 32-bit process on 64-bit OS: use Sysnative to reach 64-bit System32
                var sysnative = System.IO.Path.Combine(windir, "Sysnative", "powercfg.exe");
                if (System.IO.File.Exists(sysnative)) return sysnative;
            }
            var system32 = System.IO.Path.Combine(windir, "System32", "powercfg.exe");
            return system32;
        }
    }
}
