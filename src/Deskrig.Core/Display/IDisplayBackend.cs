using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Display;

/// <summary>Platform-specific display topology backend, selected by <see cref="DisplayManager"/> at
/// construction time. One implementation per supported OS (CCD API on Windows, xrandr on Linux) -
/// everything platform-neutral (profile capture from a topology snapshot) lives in the facade instead.</summary>
public interface IDisplayBackend
{
    IReadOnlyList<DisplayInfo> GetCurrentTopology();
    IReadOnlyList<(int Width, int Height, int RefreshHz)> GetPossibleModes(string hardwareId);
    int? GetMaxDpiScalePercent(string hardwareId);
    DisplayApplyResult Apply(DisplayProfile profile, ILogSink log, bool dryRun);
}
