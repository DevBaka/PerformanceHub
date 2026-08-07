using Deskrig.Core.Interop;
using Deskrig.Core.Logging;

namespace Deskrig.Core.Power;

/// <summary>
/// Power-plan backend for Linux via `powerprofilesctl` (power-profiles-daemon - ships by default on GNOME
/// and KDE). Linux only has three fixed profiles ("power-saver"/"balanced"/"performance") instead of
/// Windows' arbitrary GUID-keyed schemes, but the model already treats the plan id as an opaque string, so
/// no model change is needed - profile authors just put one of those three names where a GUID used to go.
/// </summary>
internal sealed class PowerProfilesCtlBackend : IPowerPlanBackend
{
    public bool TrySetActive(string? planId, ILogSink log)
    {
        if (string.IsNullOrWhiteSpace(planId)) return true;
        planId = planId.Trim();

        var (exitCode, _, stdErr) = ProcessRunner.Run("powerprofilesctl", $"set {planId}");
        if (exitCode == 0)
        {
            log.Info($"Power-Plan gesetzt: {planId}.");
            return true;
        }

        log.Warn($"Power-Plan '{planId}' konnte nicht aktiviert werden: {stdErr}".TrimEnd() +
                 (ProcessRunner.ToolExists("powerprofilesctl") ? "" : " (power-profiles-daemon nicht installiert?)"));
        return false;
    }

    public string? GetActiveGuid()
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("powerprofilesctl", "get");
        if (exitCode != 0) return null;
        var name = stdOut.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public IReadOnlyList<(string Guid, string Name, bool Active)> GetAvailablePlans()
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("powerprofilesctl", "list");
        var result = new List<(string, string, bool)>();
        if (exitCode != 0) return result;

        foreach (var rawLine in stdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue; // skip indented "Driver:" etc. lines
            bool active = line.TrimStart().StartsWith('*');
            var name = line.TrimStart('*', ' ').TrimEnd(':').Trim();
            if (name.Length == 0) continue;
            result.Add((name, name, active));
        }
        return result;
    }
}
