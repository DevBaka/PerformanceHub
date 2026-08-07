using System.Globalization;
using System.Text.Json;
using Deskrig.Core.Interop;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Display;

/// <summary>
/// Reads and writes the display topology via KDE's `kscreen-doctor`, talking to KWin directly over its
/// native output-management protocol - the same thing KDE's own Display Configuration settings page uses.
///
/// This exists because xrandr (<see cref="XrandrDisplayBackend"/>) turned out to be unreliable under a
/// KDE Plasma Wayland session: XWayland's RandR layer there is a read-only/synthetic reflection (wrong
/// resolutions, wrong refresh rates observed against real hardware) - write requests (`--off`, `--pos`,
/// `--fb`, ...) get silently accepted (exit 0) without changing anything real, and screen-size changes are
/// flatly rejected with BadMatch. kscreen-doctor writes actually take effect (verified: moved a real output
/// and confirmed via `kscreen-doctor -j` that it moved). Selected over xrandr whenever it's available and
/// responds with real output data - see <see cref="IsAvailable"/>.
///
/// Identity (<see cref="DisplayInfo.HardwareId"/>/FriendlyName) still comes from <see cref="EdidUtil"/>
/// (DRM/KMS sysfs) - kscreen-doctor's own JSON doesn't expose EDID, and connector names ("DP-1",
/// "HDMI-A-1", ...) match 1:1 between sysfs and kscreen-doctor since both ultimately name the same kernel
/// DRM connectors.
/// </summary>
internal sealed class KscreenDisplayBackend : IDisplayBackend
{
    /// <summary>True if kscreen-doctor is installed and actually returns real output data - the cheapest
    /// reliable signal that we're on a KWin session where it'll actually work, without hardcoding a check
    /// against $XDG_CURRENT_DESKTOP.</summary>
    public static bool IsAvailable()
    {
        if (!ProcessRunner.ToolExists("kscreen-doctor")) return false;
        var (exitCode, stdOut, _) = ProcessRunner.Run("kscreen-doctor", "-j", timeoutMs: 5000);
        if (exitCode != 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(stdOut);
            return doc.RootElement.TryGetProperty("outputs", out var outputs) && outputs.GetArrayLength() > 0;
        }
        catch { return false; }
    }

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

    /// <summary>KDE lets scale be set fairly freely rather than reporting a hardware-imposed maximum -
    /// unlike Windows' CCD, there's nothing meaningful to cap against.</summary>
    public int? GetMaxDpiScalePercent(string hardwareId) => null;

    public DisplayApplyResult Apply(DisplayProfile profile, ILogSink log, bool dryRun = false)
    {
        if (!ProcessRunner.ToolExists("kscreen-doctor"))
        {
            log.Error($"Display-Profil '{profile.Name}' konnte nicht angewendet werden: 'kscreen-doctor' nicht gefunden.");
            return new DisplayApplyResult(false, Array.Empty<string>());
        }

        var current = ParseOutputs();
        var missing = new List<string>();
        var wantedActive = profile.Displays.Where(d => d.Active).ToList();

        var resolvedActive = new List<(DisplayProfileEntry Entry, string Port)>();
        foreach (var entry in wantedActive)
        {
            if (_portByHardwareId.TryGetValue(entry.HardwareId, out var port))
                resolvedActive.Add((entry, port));
            else
                missing.Add(entry.FriendlyNameHint ?? entry.HardwareId);
        }

        if (resolvedActive.Count == 0)
        {
            log.Warn($"Display-Profil '{profile.Name}' ergibt keine anwendbaren Ausgänge (keine passenden Monitore angeschlossen).");
            return new DisplayApplyResult(false, missing);
        }

        if (!dryRun && MatchesCurrentTopology(resolvedActive, current))
        {
            log.Info($"Display-Profil '{profile.Name}' entspricht bereits der aktiven Topologie, überspringe kscreen-doctor.");
            return new DisplayApplyResult(true, missing);
        }

        var groups = resolvedActive
            .GroupBy(r => r.Entry.Group != 0 ? $"grp-{r.Entry.Group}" : $"solo-{r.Port}")
            .Select(g => g.ToList())
            .ToList();

        var tokens = new List<string>();

        // Anything currently enabled that isn't part of the requested topology gets disabled, same as a
        // Windows profile implicitly turning off displays it doesn't list as active.
        var keepPorts = resolvedActive.Select(r => r.Port).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (port, hwId) in _hardwareIdByPort)
        {
            if (!keepPorts.Contains(port) && current.TryGetValue(hwId, out var d) && d.IsActive)
                tokens.Add($"output.{port}.disable");
        }

        foreach (var group in groups)
        {
            var anchor = group[0];
            AppendOutputTokens(tokens, anchor.Port, anchor.Entry, replicationSourcePort: null, log);
            foreach (var follower in group.Skip(1))
                AppendOutputTokens(tokens, follower.Port, follower.Entry, replicationSourcePort: anchor.Port, log);
        }

        // KDE has no single boolean "primary" flag - a lower "priority" number means more primary (1 =
        // top). Assigning our profile's Primary entry priority 1 and numbering the rest afterwards keeps
        // KDE's own concept (used for taskbar placement, "identify displays" numbering, etc.) in sync with
        // what the profile asked for.
        var primaryPort = resolvedActive.FirstOrDefault(r => r.Entry.Primary).Port ?? resolvedActive[0].Port;
        int nextPriority = 2;
        foreach (var (_, port) in resolvedActive)
            tokens.Add($"output.{port}.priority.{(string.Equals(port, primaryPort, StringComparison.OrdinalIgnoreCase) ? 1 : nextPriority++)}");

        if (dryRun)
        {
            log.Info($"[Dry-Run] Display-Profil '{profile.Name}' wäre anwendbar (kscreen-doctor {string.Join(' ', tokens)}).");
            return new DisplayApplyResult(true, missing);
        }

        // Unlike xrandr, kscreen-doctor is explicitly documented to apply every token in one call
        // atomically - no need to split disable/enable into separate invocations to dodge an ordering bug.
        var (exitCode, _, stdErr) = ProcessRunner.Run("kscreen-doctor", string.Join(' ', tokens), timeoutMs: 15000);
        if (exitCode != 0)
        {
            log.Error($"Display-Profil '{profile.Name}': kscreen-doctor lehnte die Anordnung ab. {stdErr}".TrimEnd());
            return new DisplayApplyResult(false, missing);
        }

        if (missing.Count > 0)
            log.Warn($"Display-Profil '{profile.Name}': nicht angeschlossen, übersprungen: {string.Join(", ", missing)}");
        log.Info($"Display-Profil '{profile.Name}' angewendet ({resolvedActive.Count} Ausgang/Ausgänge via kscreen-doctor).");
        return new DisplayApplyResult(true, missing);
    }

    private void AppendOutputTokens(List<string> tokens, string port, DisplayProfileEntry entry, string? replicationSourcePort, ILogSink log)
    {
        tokens.Add($"output.{port}.enable");

        if (entry.Width > 0 && entry.Height > 0)
        {
            _modesByHardwareId.TryGetValue(entry.HardwareId, out var modes);
            var match = (modes ?? new())
                .Where(m => m.Width == entry.Width && m.Height == entry.Height)
                .OrderBy(m => entry.RefreshRateHz > 0 ? Math.Abs(m.RefreshHz - entry.RefreshRateHz) : 0)
                .FirstOrDefault();
            // kscreen-doctor resolves "output.<name>.mode.<name>" against the mode's own advertised name
            // string (e.g. "1920x1080@60") - reusing that verbatim instead of formatting our own avoids any
            // rounding/precision mismatch that would make it fail to match a real mode.
            if (match.ModeName != null)
            {
                tokens.Add($"output.{port}.mode.{match.ModeName}");
            }
            else
            {
                // Silently keeping whatever mode is already active would leave the *position* below applied
                // for a resolution the display was never actually switched to - exactly the bug that once
                // left gaps between monitors the mouse couldn't cross (positions computed for a stale/wrong
                // captured resolution, real displays still at their native, narrower one). Loud beats quiet.
                var name = entry.FriendlyNameHint ?? entry.HardwareId;
                log.Warn($"'{name}': {entry.Width}x{entry.Height} wird von diesem Display nicht unterstützt, Auflösung bleibt unverändert.");
            }
        }

        tokens.Add($"output.{port}.position.{entry.PositionX},{entry.PositionY}");

        if (entry.DpiScalePercent is int pct && pct > 0)
            tokens.Add($"output.{port}.scale.{(pct / 100.0).ToString("0.####", CultureInfo.InvariantCulture)}");

        if (replicationSourcePort != null)
            tokens.Add($"output.{port}.replicationSource.{replicationSourcePort}");
    }

    private bool MatchesCurrentTopology(List<(DisplayProfileEntry Entry, string Port)> resolvedActive, Dictionary<string, DisplayInfo> current)
    {
        var currentActive = current.Values.Where(d => d.IsActive).ToList();
        if (currentActive.Count != resolvedActive.Count) return false;

        foreach (var (entry, _) in resolvedActive)
        {
            if (!current.TryGetValue(entry.HardwareId, out var match) || !match.IsActive) return false;
            if (match.IsPrimary != entry.Primary) return false;
            if (match.PositionX != entry.PositionX || match.PositionY != entry.PositionY) return false;
            if (entry.Width > 0 && (match.Width != entry.Width || match.Height != entry.Height)) return false;
            if (entry.RefreshRateHz > 0 && Math.Abs(match.RefreshRateHz - entry.RefreshRateHz) > 0.5) return false;
            if (entry.DpiScalePercent is int pct && pct > 0 && Math.Abs(match.CurrentDpiScalePercent - pct) > 1) return false;
        }
        return true;
    }

    // --- parsing -------------------------------------------------------------------------------------

    private Dictionary<string, string> _portByHardwareId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _hardwareIdByPort = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<(int Width, int Height, double RefreshHz, string ModeName)>> _modesByHardwareId = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, DisplayInfo> ParseOutputs()
    {
        var result = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
        _portByHardwareId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _hardwareIdByPort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _modesByHardwareId = new Dictionary<string, List<(int, int, double, string)>>(StringComparer.OrdinalIgnoreCase);

        var (exitCode, stdOut, _) = ProcessRunner.Run("kscreen-doctor", "-j");
        if (exitCode != 0) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(stdOut); }
        catch { return result; }
        using var _ = doc;

        if (!doc.RootElement.TryGetProperty("outputs", out var outputs)) return result;

        var edidByPort = EdidUtil.ReadFromSysfs();

        // Two passes: KDE's "primary-ness" (lowest "priority" among enabled outputs) can only be known
        // once every output has been read, so the raw per-output data is collected first.
        var raw = new List<(string Port, bool Connected, bool Enabled, int X, int Y, int W, int H, double RefreshHz, double Scale, int Priority)>();

        foreach (var o in outputs.EnumerateArray())
        {
            var port = o.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(port)) continue;

            bool connected = o.TryGetProperty("connected", out var c) && c.GetBoolean();
            if (!connected) continue;

            bool enabled = o.TryGetProperty("enabled", out var en) && en.GetBoolean();
            int x = 0, y = 0, w = 0, h = 0;
            double refreshHz = 0;
            double scale = o.TryGetProperty("scale", out var sc) ? sc.GetDouble() : 1.0;
            int priority = o.TryGetProperty("priority", out var pr) ? pr.GetInt32() : int.MaxValue;

            if (o.TryGetProperty("pos", out var pos))
            {
                x = pos.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
                y = pos.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
            }

            var modes = new List<(int Width, int Height, double RefreshHz, string ModeName)>();
            var currentModeId = o.TryGetProperty("currentModeId", out var cmId) ? cmId.GetString() : null;
            if (o.TryGetProperty("modes", out var modesArr))
            {
                foreach (var m in modesArr.EnumerateArray())
                {
                    var mid = m.TryGetProperty("id", out var midEl) ? midEl.GetString() : null;
                    if (!m.TryGetProperty("size", out var size)) continue;
                    int mw = size.TryGetProperty("width", out var mwEl) ? mwEl.GetInt32() : 0;
                    int mh = size.TryGetProperty("height", out var mhEl) ? mhEl.GetInt32() : 0;
                    double mr = m.TryGetProperty("refreshRate", out var rr) ? rr.GetDouble() : 0;
                    var mname = m.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    mname ??= $"{mw}x{mh}@{mr:0}";
                    modes.Add((mw, mh, mr, mname));
                    if (mid != null && mid == currentModeId) { w = mw; h = mh; refreshHz = mr; }
                }
            }

            var hwId = EdidUtil.ResolveHardwareId(port, edidByPort);
            _portByHardwareId[hwId] = port;
            _hardwareIdByPort[port] = hwId;
            _modesByHardwareId[hwId] = modes;

            raw.Add((port, connected, enabled, x, y, w, h, refreshHz, scale, priority));
        }

        var primaryPort = raw.Where(r => r.Enabled).OrderBy(r => r.Priority).Select(r => (string?)r.Port).FirstOrDefault();

        foreach (var r in raw)
        {
            var hwId = _hardwareIdByPort[r.Port];
            var friendlyName = EdidUtil.ResolveFriendlyName(r.Port, edidByPort);
            var scalePercent = (int)Math.Round(r.Scale * 100);

            result[hwId] = new DisplayInfo
            {
                HardwareId = hwId,
                FriendlyName = friendlyName,
                IsActive = r.Enabled,
                IsPrimary = r.Enabled && string.Equals(r.Port, primaryPort, StringComparison.OrdinalIgnoreCase),
                PositionX = r.X,
                PositionY = r.Y,
                Width = r.W,
                Height = r.H,
                RefreshRateHz = r.RefreshHz,
                CurrentDpiScalePercent = scalePercent,
                RecommendedDpiScalePercent = scalePercent,
                CloneGroupKey = r.Enabled ? $"{r.X}:{r.Y}" : "",
                ConnectorName = r.Port,
            };
        }

        return result;
    }
}
