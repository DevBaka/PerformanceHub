using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Display;

/// <summary>
/// Reads and writes the current display topology and applies/captures <see cref="DisplayProfile"/>s.
/// Delegates the actual OS calls to an <see cref="IDisplayBackend"/> picked for the running platform (CCD
/// API on Windows; on Linux, kscreen-doctor when available - KDE/KWin's native output-management CLI,
/// significantly more reliable there than xrandr - otherwise xrandr, see
/// <see cref="LinuxDisplayBackendFactory"/>) - every monitor is identified by a stable, hardware-derived
/// id, never by adapter/port index, so profiles survive re-plugging monitors into different ports.
/// </summary>
public sealed class DisplayManager
{
    private readonly IDisplayBackend _backend;

    public DisplayManager() : this(CreateBackend()) { }

    internal DisplayManager(IDisplayBackend backend) => _backend = backend;

    private static IDisplayBackend CreateBackend()
    {
#if DESKRIG_WINDOWS
        return new WindowsDisplayBackend();
#elif DESKRIG_LINUX
        if (OperatingSystem.IsLinux()) return LinuxDisplayBackendFactory.Create();
        throw new PlatformNotSupportedException("Display-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#else
        throw new PlatformNotSupportedException("Display-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#endif
    }

    /// <summary>Enumerates every currently connected monitor (active or not), by stable hardware id.</summary>
    public IReadOnlyList<DisplayInfo> GetCurrentTopology() => _backend.GetCurrentTopology();

    /// <summary>Every resolution/refresh-rate combination this specific monitor actually advertises as
    /// supported. Used to populate the editor's dropdowns with only "known good" values.</summary>
    public IReadOnlyList<(int Width, int Height, int RefreshHz)> GetPossibleModes(string hardwareId)
        => _backend.GetPossibleModes(hardwareId);

    /// <summary>The highest DPI/scale percentage this monitor's active source reports supporting, or null
    /// if unavailable (e.g. inactive display, or not applicable on this platform).</summary>
    public int? GetMaxDpiScalePercent(string hardwareId) => _backend.GetMaxDpiScalePercent(hardwareId);

    /// <summary>Applies a display profile. Displays not currently connected are skipped (reported as missing).</summary>
    public DisplayApplyResult Apply(DisplayProfile profile, ILogSink log, bool dryRun = false)
        => _backend.Apply(profile, log, dryRun);

    /// <summary>Builds a DisplayProfile snapshot of the currently active topology (for "save current" / snapshots).
    /// Platform-neutral: works purely off <see cref="GetCurrentTopology"/>, no OS-specific calls.</summary>
    public DisplayProfile CaptureCurrentAsProfile(string name)
    {
        var current = GetCurrentTopology();
        var groupIds = new Dictionary<string, int>();
        int nextGroup = 1;

        var profile = new DisplayProfile { Name = name };
        foreach (var d in current)
        {
            int group = 0;
            if (d.IsActive)
            {
                if (!groupIds.TryGetValue(d.CloneGroupKey, out group))
                {
                    // Only assign a shared group number if more than one active display uses this source.
                    var siblingCount = current.Count(x => x.IsActive && x.CloneGroupKey == d.CloneGroupKey);
                    group = siblingCount > 1 ? nextGroup++ : 0;
                    groupIds[d.CloneGroupKey] = group;
                }
            }

            profile.Displays.Add(new DisplayProfileEntry
            {
                HardwareId = d.HardwareId,
                FriendlyNameHint = d.FriendlyName,
                Active = d.IsActive,
                Primary = d.IsPrimary,
                PositionX = d.PositionX,
                PositionY = d.PositionY,
                Width = d.Width,
                Height = d.Height,
                RefreshRateHz = d.RefreshRateHz,
                Group = group,
                DpiScalePercent = d.IsActive && d.CurrentDpiScalePercent > 0 ? d.CurrentDpiScalePercent : null,
            });
        }
        return profile;
    }
}
