namespace Deskrig.Core.Display;

/// <summary>
/// Picks the working Linux display backend at runtime rather than hardcoding a desktop-environment check:
/// kscreen-doctor (KDE/KWin's native output-management CLI) whenever it's installed and actually returns
/// real output data, since it's demonstrably more reliable there than xrandr (see
/// <see cref="KscreenDisplayBackend"/> for why - xrandr's XWayland RandR layer under KWin turned out to be
/// a read-only/synthetic view that silently no-ops write requests). Falls back to xrandr otherwise, which
/// covers X11 sessions and non-KDE desktops.
/// </summary>
internal static class LinuxDisplayBackendFactory
{
    public static IDisplayBackend Create()
        => KscreenDisplayBackend.IsAvailable() ? new KscreenDisplayBackend() : new XrandrDisplayBackend();
}
