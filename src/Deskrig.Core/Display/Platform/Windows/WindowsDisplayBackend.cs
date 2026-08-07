using System.Drawing;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;
using WindowsDisplayAPI.DisplayConfig;
using WindowsDisplayAPI.Native.DisplayConfig;

namespace Deskrig.Core.Display;

/// <summary>
/// Reads and writes the Windows display topology via the CCD API (QueryDisplayConfig / SetDisplayConfig),
/// wrapped by the WindowsDisplayAPI library. Every monitor is identified by its stable CCD target device
/// path (EDID-derived), never by adapter/source/target index, so profiles survive re-plugging monitors
/// into different ports.
/// </summary>
internal sealed class WindowsDisplayBackend : IDisplayBackend
{
    /// <summary>Enumerates every currently connected monitor (active or not), by stable hardware id.</summary>
    public IReadOnlyList<DisplayInfo> GetCurrentTopology()
    {
        // GetAllPaths returns every path Windows has ever cached (all adapters, virtual/placeholder
        // targets, duplicates) - it's an inventory, not the truth about what's on right now. We use it only
        // to discover physically-attached-but-currently-off monitors, filtered to IsAvailable targets, then
        // deduplicated by stable hardware id. GetActivePaths is authoritative for the current state and
        // always wins. Both are called with virtualModeAware=false - passing true suppresses mode/signal
        // information (position, resolution, refresh rate all come back empty).
        var byId = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in PathInfo.GetAllPaths(false))
        {
            foreach (var targetInfo in path.TargetsInfo)
            {
                var dt = targetInfo.DisplayTarget;
                bool available;
                try { available = dt.IsAvailable; } catch { available = false; }
                if (!available) continue;

                var hwId = SafeDevicePath(dt);
                if (byId.ContainsKey(hwId)) continue;
                byId[hwId] = BuildDisplayInfo(path, targetInfo, dt, hwId, forceInactive: true);
            }
        }

        foreach (var path in PathInfo.GetActivePaths(false))
        {
            foreach (var targetInfo in path.TargetsInfo)
            {
                var dt = targetInfo.DisplayTarget;
                var hwId = SafeDevicePath(dt);
                byId[hwId] = BuildDisplayInfo(path, targetInfo, dt, hwId, forceInactive: false);
            }
        }

        return byId.Values.ToList();
    }

    private static DisplayInfo BuildDisplayInfo(PathInfo path, PathTargetInfo targetInfo, PathDisplayTarget dt, string hwId, bool forceInactive)
    {
        bool hasMode = path.IsModeInformationAvailable;
        Point position = default;
        Size resolution = default;
        if (hasMode)
        {
            try { position = path.Position; resolution = path.Resolution; }
            catch { hasMode = false; }
        }

        double refreshHz = 0;
        if (targetInfo.IsSignalInformationAvailable)
        {
            try { refreshHz = targetInfo.FrequencyInMillihertz / 1000.0; } catch { /* ignore */ }
        }

        string groupKey = $"{path.DisplaySource.Adapter.AdapterId}:{path.DisplaySource.SourceId}";
        bool isActive = !forceInactive && path.IsInUse && hasMode;
        bool isPrimary = false;
        if (isActive)
        {
            try { isPrimary = path.IsGDIPrimary; } catch { isPrimary = position == Point.Empty; }
        }

        int currentDpi = 0, recommendedDpi = 0, maxDpi = 0;
        if (isActive)
        {
            // DPI-scale properties throw if the source has no monitor currently driven off it.
            try
            {
                currentDpi = (int)path.DisplaySource.CurrentDPIScale;
                recommendedDpi = (int)path.DisplaySource.RecommendedDPIScale;
                maxDpi = (int)path.DisplaySource.MaximumDPIScale;
            }
            catch { /* leave at 0 - unavailable */ }
        }

        return new DisplayInfo
        {
            HardwareId = hwId,
            FriendlyName = SafeFriendlyName(dt, hwId),
            IsActive = isActive,
            IsPrimary = isPrimary,
            PositionX = position.X,
            PositionY = position.Y,
            Width = resolution.Width,
            Height = resolution.Height,
            RefreshRateHz = refreshHz,
            CurrentDpiScalePercent = currentDpi,
            RecommendedDpiScalePercent = recommendedDpi,
            MaximumDpiScalePercent = maxDpi,
            CloneGroupKey = groupKey,
        };
    }

    /// <summary>
    /// Every resolution/refresh-rate combination this specific monitor+driver actually advertises as
    /// supported (via the legacy GDI enumeration, which - unlike CCD - reports plain Hz numbers). Works for
    /// currently-off monitors too, since it's a property of the physical target, not of whatever source
    /// happens to be feeding it right now. Used to populate the editor's dropdowns with only "known good"
    /// values instead of letting the user type something the monitor will reject.
    /// </summary>
    public IReadOnlyList<(int Width, int Height, int RefreshHz)> GetPossibleModes(string hardwareId)
    {
        try
        {
            foreach (var target in PathDisplayTarget.GetDisplayTargets())
            {
                if (!string.Equals(SafeDevicePath(target), hardwareId, StringComparison.OrdinalIgnoreCase)) continue;
                var device = target.ToDisplayDevice();
                return device.GetPossibleSettings()
                    .Select(s => (s.Resolution.Width, s.Resolution.Height, s.Frequency))
                    .Distinct()
                    .OrderByDescending(m => m.Width * m.Height)
                    .ThenByDescending(m => m.Frequency)
                    .ToList();
            }
        }
        catch { /* best effort */ }
        return Array.Empty<(int, int, int)>();
    }

    /// <summary>The highest DPI scale this monitor's current source reports supporting, in percent - only
    /// available while the monitor is active (the scale is a property of the source driving it).</summary>
    public int? GetMaxDpiScalePercent(string hardwareId)
    {
        try
        {
            foreach (var path in PathInfo.GetActivePaths(false))
                foreach (var t in path.TargetsInfo)
                    if (string.Equals(SafeDevicePath(t.DisplayTarget), hardwareId, StringComparison.OrdinalIgnoreCase))
                        return (int)path.DisplaySource.MaximumDPIScale;
        }
        catch { /* best effort */ }
        return null;
    }

    /// <summary>Applies a display profile. Displays not currently connected are skipped (reported as missing).</summary>
    public DisplayApplyResult Apply(DisplayProfile profile, ILogSink log, bool dryRun = false)
    {
        const int maxAttempts = 4;
        try
        {
            // SetDisplayConfig briefly resets the GPU output pipeline (visible as a flicker/TDR-like blip on
            // some drivers) even when the requested topology is already exactly what's active - e.g. a profile
            // reapplied on every game launch. Skip the call entirely when nothing would actually change.
            if (!dryRun && MatchesCurrentTopology(profile))
            {
                log.Info($"Display-Profil '{profile.Name}' entspricht bereits der aktiven Topologie, überspringe SetDisplayConfig.");
                ApplyDpiScales(profile, log);
                return new DisplayApplyResult(true, Array.Empty<string>());
            }

            bool triedDeactivateNudge = false;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var (newPaths, missing, movedHardwareIds) = BuildPathInfos(profile, log);

                if (newPaths.Count == 0)
                {
                    if (missing.Count > 0)
                        log.Warn($"Display-Profil '{profile.Name}': nicht angeschlossen, übersprungen: {string.Join(", ", missing)}");
                    log.Warn($"Display-Profil '{profile.Name}' ergibt keine anwendbaren Pfade (keine passenden Monitore angeschlossen).");
                    return new DisplayApplyResult(false, missing);
                }

                // ValidatePathInfos can either return false OR throw (e.g. DuplicateModeException) for an
                // invalid combination - notably when un-cloning two displays that currently still share one
                // physical source: BuildLiveLookup hands both the same live source (accurately reflecting
                // Windows' current state), so the two paths end up requesting conflicting modes on that one
                // source. Either outcome means the same thing here and gets the same treatment.
                bool valid;
                string? validationError = null;
                try { valid = PathInfo.ValidatePathInfos(newPaths, true); }
                catch (Exception ex) { valid = false; validationError = ex.Message; }

                if (!valid)
                {
                    // Reactivating a currently-off monitor (especially across multiple GPUs/adapters), or
                    // un-cloning two displays that currently share one source, can hand us a stale/conflicting
                    // source assignment, or Windows' own Extend heuristic sometimes only brings up a subset of
                    // connected targets on the first try. Forcing a default Extend topology and retrying a few
                    // times (with a short settle delay) resolves this reliably in practice - confirmed against
                    // a real 2-adapter/4-monitor setup, both for reactivation and for un-cloning.
                    if (attempt < maxAttempts)
                    {
                        log.Warn($"Display-Profil '{profile.Name}': Topologie ungültig (Versuch {attempt}/{maxAttempts}){(validationError != null ? $" ({validationError})" : "")}, versuche Basis-Zuweisung (Extend) und wiederhole.");
                        try { PathInfo.ApplyTopology(DisplayConfigTopologyId.Extend, true); }
                        catch (Exception ex) { log.Warn($"ApplyTopology(Extend) fehlgeschlagen: {ex.Message}"); }
                        Thread.Sleep(400);
                        continue;
                    }

                    // Last resort: some drivers flatly refuse to hand a still-clone-sharing display a new
                    // source in one atomic call (confirmed - even a direct native SetDisplayConfig call
                    // rejects it with "Invalid paths information", not just this library's validation), but
                    // accept it once the display has been through an intermediate "off" state first. So:
                    // briefly deactivate exactly the displays that needed a source change, then retry once.
                    if (!triedDeactivateNudge && movedHardwareIds.Count > 0)
                    {
                        triedDeactivateNudge = true;
                        log.Warn($"Display-Profil '{profile.Name}': Wechsel von geteilter Quelle direkt nicht möglich, deaktiviere betroffene Displays kurz und versuche erneut.");
                        if (TryDeactivateAndSettle(movedHardwareIds, log))
                        {
                            attempt = 0; // restart the attempt counter for the real topology
                            continue;
                        }
                    }

                    if (missing.Count > 0)
                        log.Warn($"Display-Profil '{profile.Name}': nicht angeschlossen, übersprungen: {string.Join(", ", missing)}");
                    log.Error($"Display-Profil '{profile.Name}': Windows lehnt diese Kombination aus Aktiv/Position/Auflösung ab (auch nach {maxAttempts} Versuchen).{(validationError != null ? $" Letzter Fehler: {validationError}" : "")}");
                    return new DisplayApplyResult(false, missing);
                }

                if (missing.Count > 0)
                    log.Warn($"Display-Profil '{profile.Name}': nicht angeschlossen, übersprungen: {string.Join(", ", missing)}");

                if (dryRun)
                {
                    log.Info($"[Dry-Run] Display-Profil '{profile.Name}' waere anwendbar ({newPaths.Count} Pfad(e), {missing.Count} fehlend).");
                    return new DisplayApplyResult(true, missing);
                }

                PathInfo.ApplyPathInfos(newPaths, true, true, false);
                log.Info($"Display-Profil '{profile.Name}' angewendet ({newPaths.Count} Pfad(e)).");
                ApplyDpiScales(profile, log);
                return new DisplayApplyResult(true, missing);
            }

            return new DisplayApplyResult(false, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            log.Error($"Display-Profil '{profile.Name}' konnte nicht angewendet werden", ex);
            return new DisplayApplyResult(false, Array.Empty<string>());
        }
    }

    /// <summary>Deactivates exactly the given displays (by hardware id) while leaving everything else active
    /// and untouched, as an intermediate step - see the "last resort" comment in <see cref="Apply"/>.</summary>
    private static bool TryDeactivateAndSettle(IReadOnlyCollection<string> hardwareIdsToDeactivate, ILogSink log)
    {
        try
        {
            var toDrop = new HashSet<string>(hardwareIdsToDeactivate, StringComparer.OrdinalIgnoreCase);
            var newPaths = new List<PathInfo>();

            foreach (var path in PathInfo.GetActivePaths(false))
            {
                var keptTargets = path.TargetsInfo.Where(t => !toDrop.Contains(SafeDevicePath(t.DisplayTarget))).ToArray();
                if (keptTargets.Length == 0) continue; // this whole path is one of the ones being turned off
                if (keptTargets.Length == path.TargetsInfo.Length) { newPaths.Add(path); continue; } // untouched

                newPaths.Add(new PathInfo(path.DisplaySource, path.Position, path.Resolution, path.PixelFormat, keptTargets, 0));
            }

            if (newPaths.Count == 0 || !PathInfo.ValidatePathInfos(newPaths, true)) return false;

            PathInfo.ApplyPathInfos(newPaths, true, true, false);
            Thread.Sleep(500);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"Zwischenschritt (Displays kurz deaktivieren) fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>True if every active display in the profile already matches the currently active topology
    /// (same monitors, position, resolution, refresh rate, primary) - and no other display is active.</summary>
    private bool MatchesCurrentTopology(DisplayProfile profile)
    {
        var current = GetCurrentTopology();
        var currentActive = current.Where(d => d.IsActive).ToList();
        var wantedActive = profile.Displays.Where(d => d.Active).ToList();

        if (currentActive.Count != wantedActive.Count) return false;

        foreach (var wanted in wantedActive)
        {
            var match = currentActive.FirstOrDefault(c => string.Equals(c.HardwareId, wanted.HardwareId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return false;
            if (match.IsPrimary != wanted.Primary) return false;
            if (match.PositionX != wanted.PositionX || match.PositionY != wanted.PositionY) return false;
            if (match.Width != wanted.Width || match.Height != wanted.Height) return false;
            if (wanted.RefreshRateHz > 0 && Math.Abs(match.RefreshRateHz - wanted.RefreshRateHz) > 0.5) return false;
        }
        return true;
    }

    /// <summary>
    /// Sets per-display DPI scale via PathDisplaySource.CurrentDPIScale - a separate, independent call from
    /// SetDisplayConfig (topology), so this runs regardless of whether the topology itself changed. Windows
    /// only exposes a fixed set of percentages (100/125/.../500) and caps at what the monitor/GPU reports as
    /// its maximum - requests get rounded to the nearest valid step and clamped to that maximum.
    /// </summary>
    private static void ApplyDpiScales(DisplayProfile profile, ILogSink log)
    {
        var wanted = profile.Displays.Where(d => d.Active && d.DpiScalePercent.HasValue).ToList();
        if (wanted.Count == 0) return;

        var sourceByHwId = new Dictionary<string, PathDisplaySource>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PathInfo.GetActivePaths(false))
            foreach (var t in path.TargetsInfo)
            {
                var hwId = SafeDevicePath(t.DisplayTarget);
                if (!string.IsNullOrWhiteSpace(hwId)) sourceByHwId[hwId] = path.DisplaySource;
            }

        foreach (var entry in wanted)
        {
            var name = entry.FriendlyNameHint ?? entry.HardwareId;
            if (!sourceByHwId.TryGetValue(entry.HardwareId, out var source))
            {
                log.Warn($"DPI-Skalierung für '{name}' übersprungen (Display nicht aktiv/nicht gefunden).");
                continue;
            }

            var target = NearestValidDpiScale(entry.DpiScalePercent!.Value);
            var max = source.MaximumDPIScale;
            if ((int)target > (int)max)
            {
                log.Warn($"'{name}': {(int)target}% wird von diesem Display nicht unterstützt, verwende Maximum {(int)max}%.");
                target = max;
            }

            try
            {
                source.CurrentDPIScale = target;
                log.Info($"'{name}': DPI-Skalierung auf {(int)target}% gesetzt.");
            }
            catch (Exception ex)
            {
                log.Warn($"DPI-Skalierung für '{name}' konnte nicht gesetzt werden: {ex.Message}");
            }
        }
    }

    private static DisplayConfigSourceDPIScale NearestValidDpiScale(int requestedPercent)
    {
        var values = (DisplayConfigSourceDPIScale[])Enum.GetValues(typeof(DisplayConfigSourceDPIScale));
        return values.OrderBy(v => Math.Abs((int)v - requestedPercent)).First();
    }

    private static (List<PathInfo> Paths, List<string> Missing, List<string> MovedHardwareIds) BuildPathInfos(DisplayProfile profile, ILogSink log)
    {
        var lookup = BuildLiveLookup();
        var missing = new List<string>();
        var newPaths = new List<PathInfo>();

        var groups = profile.Displays
            .Where(d => d.Active)
            .GroupBy(d => d.Group != 0 ? $"grp-{d.Group}" : $"solo-{d.HardwareId}")
            .Select(g => g.ToList())
            .ToList();

        var (shiftX, shiftY) = ComputeOriginShift(groups, log);
        var sourceOverrides = ResolveSharedSourceCollisions(groups, lookup, log);

        foreach (var members in groups)
        {
            var targetInfos = new List<PathTargetInfo>();
            PathDisplaySource? source = null;
            DisplayConfigPixelFormat pixelFormat = DisplayConfigPixelFormat.PixelFormat32Bpp;

            foreach (var m in members)
            {
                if (!lookup.Entries.TryGetValue(m.HardwareId, out var liveEntry))
                {
                    missing.Add(m.FriendlyNameHint ?? m.HardwareId);
                    continue;
                }

                source ??= sourceOverrides.TryGetValue(m.HardwareId, out var overrideSource) ? overrideSource : liveEntry.Source;

                if (liveEntry.PixelFormat != DisplayConfigPixelFormat.NotSpecified)
                    pixelFormat = liveEntry.PixelFormat;
                var signal = ResolveSignalInfo(liveEntry, m.Width, m.Height, m.RefreshRateHz, log);
                targetInfos.Add(new PathTargetInfo(liveEntry.Target, signal, DisplayConfigRotation.Identity, DisplayConfigScaling.Identity, false));
            }

            if (source == null || targetInfos.Count == 0) continue;

            var first = members[0];
            newPaths.Add(new PathInfo(
                source,
                new Point(first.PositionX + shiftX, first.PositionY + shiftY),
                new Size(first.Width, first.Height),
                pixelFormat,
                targetInfos.ToArray(),
                0));
        }

        return (newPaths, missing, sourceOverrides.Keys.ToList());
    }

    /// <summary>
    /// Un-cloning two displays that currently still share one physical source means two different groups
    /// both resolve to that same live source - only one PathInfo may actually use it. For every such
    /// collision, exactly one member keeps the shared source (whichever has no alternative, if any - it has
    /// no other option) and the rest are moved to a distinct alternative source from the "all paths"
    /// database, if one is available. If more than one colliding member has no alternative at all, that's a
    /// genuine hardware/driver limit (typically: not enough source slots on the adapter) - left unresolved,
    /// surfaced as the normal validation failure.
    /// </summary>
    private static Dictionary<string, PathDisplaySource> ResolveSharedSourceCollisions(
        List<List<DisplayProfileEntry>> groups, LiveLookup lookup, ILogSink log)
    {
        var overrides = new Dictionary<string, PathDisplaySource>(StringComparer.OrdinalIgnoreCase);

        var anchors = groups
            .Select(g => g.FirstOrDefault(m => lookup.Entries.ContainsKey(m.HardwareId)))
            .Where(m => m != null)
            .Select(m => m!)
            .ToList();

        // Every other group's current source is off-limits too, not just the one this cluster shares -
        // otherwise a "free" alternative might just be another display's already-claimed source.
        var allAnchorSourceKeys = anchors.Select(m => SourceKey(lookup.Entries[m.HardwareId].Source)).ToHashSet();

        foreach (var cluster in anchors.GroupBy(m => SourceKey(lookup.Entries[m.HardwareId].Source)))
        {
            var sharedKey = cluster.Key;
            var members = cluster
                .Select(m => (Member: m, Alternatives: (lookup.AllCandidates.TryGetValue(m.HardwareId, out var alts) ? alts : new List<(PathDisplaySource Source, PathDisplayTarget Target)>())
                    .Where(a => !allAnchorSourceKeys.Contains(SourceKey(a.Source))).ToList()))
                .ToList();
            if (members.Count <= 1) continue;

            // Exactly one member keeps the shared source - prefer one with no alternative (it has no
            // other choice); otherwise just the first, arbitrarily. Everyone else tries to move.
            var stayer = members.FirstOrDefault(x => x.Alternatives.Count == 0).Member ?? members[0].Member;
            var claimedThisCluster = new HashSet<string> { sharedKey };

            foreach (var (member, alternatives) in members)
            {
                if (ReferenceEquals(member, stayer)) continue;

                var alt = alternatives.FirstOrDefault(a => !claimedThisCluster.Contains(SourceKey(a.Source)));
                if (alt.Source == null)
                {
                    log.Warn($"'{member.FriendlyNameHint ?? member.HardwareId}': keine freie Quelle verfügbar, um von der geteilten Quelle zu wechseln.");
                    continue;
                }

                log.Info($"'{member.FriendlyNameHint ?? member.HardwareId}': teilt sich die aktuelle Quelle noch mit einem anderen Display, wechsle auf eine freie Quelle.");
                overrides[member.HardwareId] = alt.Source;
                claimedThisCluster.Add(SourceKey(alt.Source));
            }
        }

        return overrides;
    }

    /// <summary>
    /// Windows requires exactly one active path to sit at (0,0) - that's its internal definition of the
    /// primary/desktop-origin monitor. A profile whose stored coordinates don't happen to include (0,0)
    /// (e.g. after excluding whichever display used to be primary) gets flatly rejected by SetDisplayConfig
    /// with no useful diagnostic. So: shift the whole layout so the display marked Primary lands at (0,0),
    /// preserving every relative offset - the same thing Windows does internally when you drag monitors
    /// around in Settings and one of them crosses the origin.
    /// </summary>
    private static (int ShiftX, int ShiftY) ComputeOriginShift(List<List<DisplayProfileEntry>> groups, ILogSink log)
    {
        if (groups.Count == 0) return (0, 0);

        var anchor = groups.FirstOrDefault(g => g.Any(m => m.Primary))?.First(m => m.Primary)
                     ?? groups.SelectMany(g => g).FirstOrDefault(m => m.PositionX == 0 && m.PositionY == 0);

        if (anchor == null)
        {
            anchor = groups[0][0];
            log.Warn($"Kein Display als primär markiert - '{anchor.FriendlyNameHint ?? anchor.HardwareId}' wird als Ursprung (0,0) verwendet.");
        }

        if (anchor.PositionX == 0 && anchor.PositionY == 0) return (0, 0);

        log.Info($"Position normalisiert: '{anchor.FriendlyNameHint ?? anchor.HardwareId}' auf (0,0) gesetzt, restliche Displays entsprechend verschoben.");
        return (-anchor.PositionX, -anchor.PositionY);
    }

    /// <summary>
    /// Hand-computing arbitrary custom timings via SetDisplayConfig is unreliable across drivers/GPUs, but
    /// the target's own advertised (GDI) possible-settings list already contains every timing the monitor
    /// and driver actually negotiated as valid - matching against that list, rather than inventing one, is
    /// what makes an exact resolution/refresh-rate change reliable. Preference order: (1) reuse the live
    /// signal verbatim if it already matches exactly - guaranteed valid, zero risk; (2) look up the exact
    /// requested resolution+refresh in the target's supported modes; (3) fall back to the monitor's
    /// EDID-preferred mode and warn that the exact request couldn't be honored.
    /// </summary>
    private static PathTargetSignalInfo ResolveSignalInfo(LiveDisplayEntry live, int width, int height, double refreshHz, ILogSink log)
    {
        var name = SafeFriendlyName(live.Target, SafeDevicePath(live.Target));

        if (live.SignalInfo is { } signal && live.CurrentWidth == width && live.CurrentHeight == height &&
            (refreshHz <= 0 || Math.Abs(live.CurrentRefreshHz - refreshHz) < 0.5))
            return signal;

        if (width > 0 && height > 0)
        {
            try
            {
                var device = live.Target.ToDisplayDevice();
                var match = device.GetPossibleSettings()
                    .Where(s => s.Resolution.Width == width && s.Resolution.Height == height)
                    .OrderBy(s => refreshHz > 0 ? Math.Abs(s.Frequency - refreshHz) : 0)
                    .FirstOrDefault();
                if (match != null && (refreshHz <= 0 || Math.Abs(match.Frequency - refreshHz) < 1.0))
                    return new PathTargetSignalInfo(match, match.Resolution);

                log.Warn($"'{name}': {width}x{height}@{refreshHz:0.##}Hz wird von diesem Display/Treiber nicht als exakter Modus unterstützt, nutze bevorzugten Modus des Monitors.");
            }
            catch (Exception ex)
            {
                log.Warn($"'{name}': unterstützte Modi konnten nicht gelesen werden ({ex.Message}), nutze bevorzugten Modus des Monitors.");
            }
        }

        return live.Target.PreferredSignalMode;
    }

    private sealed record LiveDisplayEntry(PathDisplaySource Source, PathDisplayTarget Target, PathTargetSignalInfo? SignalInfo, DisplayConfigPixelFormat PixelFormat, int CurrentWidth, int CurrentHeight, double CurrentRefreshHz);

    /// <summary>
    /// Source/target/signal combinations are only guaranteed self-consistent within a single Query call - the
    /// "all paths" database can list several different possible source/target pairings for the same physical
    /// monitor, and mixing a source from one query with a target from another produces a topology Windows
    /// rejects. So: prefer the exact pairing Windows is using right now (from GetActivePaths, always valid for
    /// already-connected displays); only fall back to the "all paths" inventory for currently-off monitors.
    /// </summary>
    private sealed record LiveLookup(
        Dictionary<string, LiveDisplayEntry> Entries,
        Dictionary<string, List<(PathDisplaySource Source, PathDisplayTarget Target)>> AllCandidates);

    private static LiveLookup BuildLiveLookup()
    {
        var dict = new Dictionary<string, LiveDisplayEntry>(StringComparer.OrdinalIgnoreCase);
        var usedSources = new HashSet<string>();

        foreach (var path in PathInfo.GetActivePaths(false))
        {
            bool hasMode = path.IsModeInformationAvailable;
            var pixelFormat = hasMode ? SafePixelFormat(path) : DisplayConfigPixelFormat.NotSpecified;
            Size resolution = default;
            if (hasMode)
            {
                try { resolution = path.Resolution; } catch { hasMode = false; }
            }
            foreach (var t in path.TargetsInfo)
            {
                var hwId = SafeDevicePath(t.DisplayTarget);
                if (string.IsNullOrWhiteSpace(hwId)) continue;
                PathTargetSignalInfo? signal = null;
                double refreshHz = 0;
                if (t.IsSignalInformationAvailable)
                {
                    try { signal = t.SignalInfo; refreshHz = t.FrequencyInMillihertz / 1000.0; } catch { signal = null; }
                }
                dict[hwId] = new LiveDisplayEntry(path.DisplaySource, t.DisplayTarget, signal, pixelFormat, resolution.Width, resolution.Height, refreshHz);
            }
            usedSources.Add(SourceKey(path.DisplaySource));
        }

        // All connected monitors commonly share one adapter with a handful of source slots (e.g. 0-3).
        // The "all paths" database lists several possible source pairings per target - built for every
        // hardware id (not just currently-off ones), because un-cloning two displays that still share a
        // source also needs an alternative for one of them even though both are "active" right now.
        var candidatesByHwId = new Dictionary<string, List<(PathDisplaySource Source, PathDisplayTarget Target)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PathInfo.GetAllPaths(false))
        {
            foreach (var t in path.TargetsInfo)
            {
                var hwId = SafeDevicePath(t.DisplayTarget);
                if (string.IsNullOrWhiteSpace(hwId)) continue;
                bool available;
                try { available = t.DisplayTarget.IsAvailable; } catch { available = false; }
                if (!available) continue;

                if (!candidatesByHwId.TryGetValue(hwId, out var list))
                    candidatesByHwId[hwId] = list = new List<(PathDisplaySource, PathDisplayTarget)>();
                list.Add((path.DisplaySource, t.DisplayTarget));
            }
        }

        // Fill in currently-off targets (missing from the active pass above), preferring a source not
        // already claimed by an active display - blindly taking the first candidate risks assigning two
        // simultaneous paths the same (adapter, source), which Windows rejects outright.
        foreach (var (hwId, candidates) in candidatesByHwId)
        {
            if (dict.ContainsKey(hwId)) continue;
            var pick = candidates.FirstOrDefault(c => !usedSources.Contains(SourceKey(c.Source)));
            if (pick.Source == null) pick = candidates[0]; // no free source found - best effort, will be reported if invalid
            dict[hwId] = new LiveDisplayEntry(pick.Source, pick.Target, null, DisplayConfigPixelFormat.NotSpecified, 0, 0, 0);
            usedSources.Add(SourceKey(pick.Source));
        }

        return new LiveLookup(dict, candidatesByHwId);
    }

    private static string SourceKey(PathDisplaySource source) => $"{source.Adapter.AdapterId}:{source.SourceId}";

    private static DisplayConfigPixelFormat SafePixelFormat(PathInfo path)
    {
        try { return path.PixelFormat; } catch { return DisplayConfigPixelFormat.NotSpecified; }
    }

    private static string SafeDevicePath(PathDisplayTarget dt)
    {
        try
        {
            var p = dt.DevicePath;
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        catch { /* fall through */ }
        return $"ADAPTER-{dt.Adapter.AdapterId}-TARGET-{dt.TargetId}";
    }

    private static string SafeFriendlyName(PathDisplayTarget dt, string fallback)
    {
        try
        {
            var n = dt.FriendlyName;
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }
        catch { /* fall through */ }
        return fallback;
    }
}
