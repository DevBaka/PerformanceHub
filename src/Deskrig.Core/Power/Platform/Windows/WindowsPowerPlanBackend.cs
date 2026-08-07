using System.Diagnostics;
using System.Text.RegularExpressions;
using Deskrig.Core.Logging;

namespace Deskrig.Core.Power;

internal sealed class WindowsPowerPlanBackend : IPowerPlanBackend
{
    public bool TrySetActive(string? guid, ILogSink log)
    {
        if (string.IsNullOrWhiteSpace(guid)) return true;

        var match = Regex.Match(guid, "([0-9a-fA-F-]{36})");
        if (!match.Success) { log.Warn($"Ungültige Power-Plan-GUID: '{guid}'."); return false; }
        guid = match.Groups[1].Value;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var res = Run("powercfg.exe", $"/S {guid}");
            if (res.ExitCode == 0)
            {
                for (int verify = 0; verify < 3; verify++)
                {
                    if (verify > 0) Thread.Sleep(150);
                    if (string.Equals(GetActiveGuid(), guid, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Info($"Power-Plan gesetzt: {guid}.");
                        return true;
                    }
                }
            }
            Thread.Sleep(300);
        }

        log.Warn($"Power-Plan '{guid}' konnte nicht aktiviert werden (powercfg fehlgeschlagen oder Schema nicht vorhanden).");
        return false;
    }

    public string? GetActiveGuid()
    {
        var res = Run("powercfg.exe", "/GetActiveScheme");
        var match = Regex.Match(res.Output, "([0-9a-fA-F-]{36})");
        return match.Success ? match.Groups[1].Value : null;
    }

    public IReadOnlyList<(string Guid, string Name, bool Active)> GetAvailablePlans()
    {
        var list = new List<(string, string, bool)>();
        var res = Run("powercfg.exe", "/L");
        foreach (var line in res.Output.Split('\r', '\n'))
        {
            var m = Regex.Match(line, @"([0-9a-fA-F-]{36})\s*\(([^)]+)\)\s*(\*)?");
            if (m.Success)
                list.Add((m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Success));
        }
        return list;
    }

    private static (int ExitCode, string Output) Run(string file, string args)
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
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
