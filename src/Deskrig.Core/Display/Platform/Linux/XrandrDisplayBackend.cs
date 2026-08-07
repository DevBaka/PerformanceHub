using System.Text;
using System.Text.RegularExpressions;
using Deskrig.Core.Interop;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Display;

/// <summary>
/// Reads and writes the X11 display topology via `xrandr`. Every monitor is identified by an id derived
/// from its EDID (manufacturer/product/serial) where available - the same idea as the Windows CCD target
/// device path - so profiles survive re-plugging monitors into different ports; falls back to the xrandr
/// output port name (e.g. "DP-1") when a monitor doesn't expose EDID.
///
/// Only works under X11/XWayland - xrandr has no view of a native Wayland compositor's own output state.
/// If the session looks like native Wayland and xrandr reports nothing useful, <see cref="Apply"/> fails
/// with a clear, loud log message instead of silently doing nothing (a wlr-randr/kscreen-doctor backend
/// for native Wayland can be added later behind the same <see cref="IDisplayBackend"/> interface).
/// </summary>
internal sealed class XrandrDisplayBackend : IDisplayBackend
{
    private static readonly Regex OutputHeaderRegex = new(
        @"^(?<name>\S+)\s+(?<state>connected|disconnected)\s*(?<primary>primary\s+)?(?:(?<w>\d+)x(?<h>\d+)\+(?<x>\d+)\+(?<y>\d+))?",
        RegexOptions.Compiled);

    private static readonly Regex ModeLineRegex = new(
        @"^\s+(?<w>\d+)x(?<h>\d+)[i]?\s+(?<rates>.+)$", RegexOptions.Compiled);

    private static readonly Regex RateTokenRegex = new(
        @"(?<rate>\d+(?:\.\d+)?)(?<flags>[*+]*)", RegexOptions.Compiled);

    public IReadOnlyList<DisplayInfo> GetCurrentTopology() => ParseOutputs().Values.ToList();

    public IReadOnlyList<(int Width, int Height, int RefreshHz)> GetPossibleModes(string hardwareId)
    {
        if (!_modesByHardwareId.TryGetValue(hardwareId, out var modes)) return Array.Empty<(int, int, int)>();
        return modes.Select(m => (m.Width, m.Height, (int)Math.Round(m.RefreshHz)))
            .Distinct()
            .OrderByDescending(m => m.Item1 * m.Item2)
            .ThenByDescending(m => m.Item3)
            .ToList();
    }

    /// <summary>X11 has no discrete DPI-scale-percent concept comparable to Windows CCD - always unknown.</summary>
    public int? GetMaxDpiScalePercent(string hardwareId) => null;

    public DisplayApplyResult Apply(DisplayProfile profile, ILogSink log, bool dryRun = false)
    {
        if (!ProcessRunner.ToolExists("xrandr"))
        {
            log.Error($"Display-Profil '{profile.Name}' konnte nicht angewendet werden: 'xrandr' nicht gefunden. " +
                      "Unter einem nativen Wayland-Compositor (ohne XWayland) wird Display-Umschalten aktuell nicht unterstützt.");
            return new DisplayApplyResult(false, Array.Empty<string>());
        }

        var current = ParseOutputs();
        var missing = new List<string>();
        var wantedActive = profile.Displays.Where(d => d.Active).ToList();

        var byHwId = current.Values.ToDictionary(d => d.HardwareId, StringComparer.OrdinalIgnoreCase);
        var portByHwId = _portByHardwareId;

        // Every display the profile marks active must resolve to a currently-connected output; anything
        // connected but not requested active gets explicitly turned off, mirroring Windows' SetDisplayConfig
        // semantics where the new path set fully replaces the topology.
        var resolvedActive = new List<(DisplayProfileEntry Entry, string Port)>();
        foreach (var entry in wantedActive)
        {
            if (portByHwId.TryGetValue(entry.HardwareId, out var port))
                resolvedActive.Add((entry, port));
            else
                missing.Add(entry.FriendlyNameHint ?? entry.HardwareId);
        }

        if (resolvedActive.Count == 0)
        {
            log.Warn($"Display-Profil '{profile.Name}' ergibt keine anwendbaren Ausgänge (keine passenden Monitore angeschlossen).");
            return new DisplayApplyResult(false, missing);
        }

        var (shiftX, shiftY) = ComputeOriginShift(resolvedActive, log);

        if (!dryRun && MatchesCurrentTopology(resolvedActive, current, shiftX, shiftY))
        {
            log.Info($"Display-Profil '{profile.Name}' entspricht bereits der aktiven Topologie, überspringe xrandr.");
            return new DisplayApplyResult(true, missing);
        }

        var groups = resolvedActive
            .GroupBy(r => r.Entry.Group != 0 ? $"grp-{r.Entry.Group}" : $"solo-{r.Port}")
            .Select(g => g.ToList())
            .ToList();

        string BuildOnArgs(bool includeScale)
        {
            var args = new StringBuilder();
            foreach (var group in groups)
            {
                var anchor = group[0];
                AppendOutputArgs(args, anchor.Port, anchor.Entry, shiftX, shiftY, sameAsPort: null, includeScale);
                foreach (var follower in group.Skip(1))
                    AppendOutputArgs(args, follower.Port, follower.Entry, shiftX, shiftY, sameAsPort: anchor.Port, includeScale);
            }
            return args.ToString().Trim();
        }

        // Anything currently connected and active that isn't part of the requested topology at all is
        // switched off, same as a Windows profile implicitly turning off displays it doesn't list as active.
        var keepPorts = resolvedActive.Select(r => r.Port).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offArgs = new StringBuilder();
        foreach (var (port, hwId) in _hardwareIdByPort)
        {
            if (!keepPorts.Contains(port) && current.TryGetValue(hwId, out var d) && d.IsActive)
                offArgs.Append($" --output {Escape(port)} --off");
        }

        var wantsScale = resolvedActive.Any(r => r.Entry.DpiScalePercent is int pct && pct != 100 && pct > 0);

        if (dryRun)
        {
            log.Info($"[Dry-Run] Display-Profil '{profile.Name}' wäre anwendbar (xrandr{offArgs} {BuildOnArgs(true)}).");
            return new DisplayApplyResult(true, missing);
        }

        // Turning outputs off and repositioning/resizing the remaining ones in a single xrandr call can
        // make the X server reject the whole thing with "BadMatch" on RRSetScreenSize when the new virtual
        // screen needs to *shrink* - the outputs being turned off are still occupying their old (larger)
        // position when xrandr tries to compute the new, smaller screen size in the same request. Doing the
        // "off" outputs as their own call first (screen can stay large for a moment, that's fine) means the
        // second call only ever has to size the screen around what's actually left.
        //
        // Some X servers (nested/virtual ones especially - Gamescope, Xephyr, VNC) reject RRSetScreenSize
        // outright and/or don't honor --off at all, no matter how the request is split up - that's a server
        // limitation we can't work around from here. Failing to turn off the no-longer-wanted displays is
        // treated as a warning, not a hard failure: the outputs that *should* be active still get set up
        // below, so the profile ends up in the best state actually achievable instead of nothing at all.
        if (offArgs.Length > 0)
        {
            var (offExit, _, offErr) = ProcessRunner.Run("xrandr", offArgs.ToString().Trim(), timeoutMs: 15000);
            if (offExit != 0)
                log.Warn($"Display-Profil '{profile.Name}': nicht benötigte Ausgänge konnten nicht abgeschaltet werden (fahre trotzdem fort). {offErr}".TrimEnd());
        }

        var (exitCode, _, stdErr) = ProcessRunner.Run("xrandr", BuildOnArgs(true), timeoutMs: 15000);
        if (exitCode != 0 && wantsScale)
        {
            // Retry without --scale - a requested DPI scale can itself demand more virtual screen space
            // than the server is willing/able to allocate, which is a separate failure mode from turning
            // outputs off/on. Better to land the requested resolution/position/primary and warn about the
            // scale than to reject the whole profile over a cosmetic detail.
            log.Warn($"Display-Profil '{profile.Name}': Anordnung mit Skalierung abgelehnt, versuche ohne Skalierung erneut.");
            var (retryExit, _, retryErr) = ProcessRunner.Run("xrandr", BuildOnArgs(false), timeoutMs: 15000);
            if (retryExit == 0)
            {
                log.Warn($"Display-Profil '{profile.Name}': Skalierung konnte nicht angewendet werden, übersprungen.");
                exitCode = 0;
                stdErr = "";
            }
            else
            {
                stdErr = retryErr;
            }
        }
        if (exitCode != 0)
        {
            log.Error($"Display-Profil '{profile.Name}': xrandr lehnte die Anordnung ab. {stdErr}".TrimEnd());
            return new DisplayApplyResult(false, missing);
        }

        if (missing.Count > 0)
            log.Warn($"Display-Profil '{profile.Name}': nicht angeschlossen, übersprungen: {string.Join(", ", missing)}");
        log.Info($"Display-Profil '{profile.Name}' angewendet ({resolvedActive.Count} Ausgang/Ausgänge via xrandr).");
        return new DisplayApplyResult(true, missing);
    }

    private static void AppendOutputArgs(StringBuilder args, string port, DisplayProfileEntry entry, int shiftX, int shiftY, string? sameAsPort, bool includeScale)
    {
        args.Append($" --output {Escape(port)}");
        if (entry.Width > 0 && entry.Height > 0)
        {
            args.Append(entry.RefreshRateHz > 0
                ? $" --mode {entry.Width}x{entry.Height} --rate {entry.RefreshRateHz:0.##}"
                : $" --mode {entry.Width}x{entry.Height}");
        }
        args.Append($" --pos {entry.PositionX + shiftX}x{entry.PositionY + shiftY}");
        args.Append(entry.Primary ? " --primary" : "");
        if (sameAsPort != null) args.Append($" --same-as {Escape(sameAsPort)}");
        if (!includeScale) return;

        // Best-effort approximation only - X11 has no equivalent to Windows' discrete DPI-scale steps.
        // A "DpiScalePercent" of 100 maps to no scaling (1.0x); other values scale the framebuffer pixel
        // size, which is a crude stand-in for real per-monitor HiDPI text scaling.
        if (entry.DpiScalePercent is int pct && pct != 100 && pct > 0)
        {
            var factor = 100.0 / pct;
            args.Append($" --scale {factor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}x{factor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}");
        }
    }

    private static string Escape(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static (int ShiftX, int ShiftY) ComputeOriginShift(List<(DisplayProfileEntry Entry, string Port)> resolvedActive, ILogSink log)
    {
        var anchor = resolvedActive.FirstOrDefault(r => r.Entry.Primary).Entry
                     ?? resolvedActive.FirstOrDefault(r => r.Entry.PositionX == 0 && r.Entry.PositionY == 0).Entry;
        if (anchor == null)
        {
            anchor = resolvedActive[0].Entry;
            log.Warn($"Kein Display als primär markiert - '{anchor.FriendlyNameHint ?? anchor.HardwareId}' wird als Ursprung (0,0) verwendet.");
        }
        if (anchor.PositionX == 0 && anchor.PositionY == 0) return (0, 0);
        return (-anchor.PositionX, -anchor.PositionY);
    }

    private bool MatchesCurrentTopology(List<(DisplayProfileEntry Entry, string Port)> resolvedActive, Dictionary<string, DisplayInfo> current, int shiftX, int shiftY)
    {
        var currentActive = current.Values.Where(d => d.IsActive).ToList();
        if (currentActive.Count != resolvedActive.Count) return false;

        foreach (var (entry, _) in resolvedActive)
        {
            if (!current.TryGetValue(entry.HardwareId, out var match) || !match.IsActive) return false;
            if (match.IsPrimary != entry.Primary) return false;
            if (match.PositionX != entry.PositionX + shiftX || match.PositionY != entry.PositionY + shiftY) return false;
            if (entry.Width > 0 && (match.Width != entry.Width || match.Height != entry.Height)) return false;
            if (entry.RefreshRateHz > 0 && Math.Abs(match.RefreshRateHz - entry.RefreshRateHz) > 0.5) return false;
        }
        return true;
    }

    // --- parsing -------------------------------------------------------------------------------------

    private Dictionary<string, string> _portByHardwareId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _hardwareIdByPort = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<(int Width, int Height, double RefreshHz)>> _modesByHardwareId = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, DisplayInfo> ParseOutputs()
    {
        var (exitCode, stdOut, stdErr) = ProcessRunner.Run("xrandr", "--query");
        var result = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
        _portByHardwareId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _hardwareIdByPort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _modesByHardwareId = new Dictionary<string, List<(int, int, double)>>(StringComparer.OrdinalIgnoreCase);

        if (exitCode != 0)
            return result; // no X11/xrandr available - caller (Apply) reports this clearly

        var edidByPort = EdidUtil.ReadFromSysfs();
        foreach (var (port, hex) in ParseEdidsFromXrandrProps())
            edidByPort.TryAdd(port, hex);
        string? currentPort = null;
        var currentModes = new List<(int Width, int Height, double RefreshHz)>();
        bool curActive = false, curPrimary = false;
        int curX = 0, curY = 0, curW = 0, curH = 0;
        double curRefreshHz = 0;

        void Flush()
        {
            if (currentPort == null) return;
            var hwId = EdidUtil.ResolveHardwareId(currentPort, edidByPort);
            var friendlyName = EdidUtil.ResolveFriendlyName(currentPort, edidByPort);
            _portByHardwareId[hwId] = currentPort;
            _hardwareIdByPort[currentPort] = hwId;
            _modesByHardwareId[hwId] = currentModes;
            result[hwId] = new DisplayInfo
            {
                HardwareId = hwId,
                FriendlyName = friendlyName,
                IsActive = curActive,
                IsPrimary = curPrimary,
                PositionX = curX,
                PositionY = curY,
                Width = curW,
                Height = curH,
                RefreshRateHz = curRefreshHz,
                CloneGroupKey = curActive ? $"{curX}:{curY}" : "",
                ConnectorName = currentPort,
            };
        }

        foreach (var rawLine in stdOut.Split('\n'))
        {
            if (rawLine.Length == 0) continue;
            if (!char.IsWhiteSpace(rawLine[0]))
            {
                var m = OutputHeaderRegex.Match(rawLine);
                if (!m.Success) continue;

                Flush();
                currentModes = new List<(int, int, double)>();
                curRefreshHz = 0;

                bool connected = m.Groups["state"].Value == "connected";
                bool hasGeometry = m.Groups["w"].Success;

                if (!connected) { currentPort = null; continue; } // disconnected outputs carry no useful state

                currentPort = m.Groups["name"].Value;
                curActive = hasGeometry;
                curPrimary = m.Groups["primary"].Success;
                curX = hasGeometry ? int.Parse(m.Groups["x"].Value) : 0;
                curY = hasGeometry ? int.Parse(m.Groups["y"].Value) : 0;
                curW = hasGeometry ? int.Parse(m.Groups["w"].Value) : 0;
                curH = hasGeometry ? int.Parse(m.Groups["h"].Value) : 0;
            }
            else if (currentPort != null)
            {
                var mm = ModeLineRegex.Match(rawLine);
                if (!mm.Success) continue;
                int w = int.Parse(mm.Groups["w"].Value), h = int.Parse(mm.Groups["h"].Value);
                foreach (Match rt in RateTokenRegex.Matches(mm.Groups["rates"].Value))
                {
                    var rate = double.Parse(rt.Groups["rate"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    currentModes.Add((w, h, rate));
                    if (rt.Groups["flags"].Value.Contains('*') && curW == w && curH == h)
                        curRefreshHz = rate;
                }
            }
        }
        Flush();
        return result;
    }

    /// <summary>
    /// Maps xrandr port name -> raw EDID hex blob straight from xrandr's own EDID property. Only a fallback
    /// for whatever <see cref="EdidUtil.ReadFromSysfs"/> didn't have (no permission, not on Linux/DRM, ...) -
    /// under a nested/virtualized X server (gamescope, Xephyr, VNC, ...) this property is often not forwarded
    /// at all even though the kernel has perfectly good EDID for the real, physical connector underneath, so
    /// sysfs is tried first and is normally sufficient on its own.
    /// </summary>
    private static Dictionary<string, string> ParseEdidsFromXrandrProps()
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("xrandr", "--props");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (exitCode != 0) return result;

        string? currentPort = null;
        bool inEdid = false;
        var hex = new StringBuilder();

        void FlushEdid()
        {
            if (currentPort != null && hex.Length > 0) result[currentPort] = hex.ToString();
            hex.Clear();
        }

        foreach (var rawLine in stdOut.Split('\n'))
        {
            if (rawLine.Length == 0) continue;
            if (!char.IsWhiteSpace(rawLine[0]))
            {
                FlushEdid();
                inEdid = false;
                var m = OutputHeaderRegex.Match(rawLine);
                currentPort = m.Success ? m.Groups["name"].Value : null;
                continue;
            }

            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("EDID:", StringComparison.OrdinalIgnoreCase)) { inEdid = true; continue; }
            if (inEdid)
            {
                if (trimmed.Length > 0 && trimmed.All(Uri.IsHexDigit)) hex.Append(trimmed);
                else inEdid = false; // next property started
            }
        }
        FlushEdid();
        return result;
    }

}
