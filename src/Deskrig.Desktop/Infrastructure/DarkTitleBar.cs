using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Deskrig.Desktop.Infrastructure;

/// <summary>Makes the native (OS-drawn) title bar follow the app's dark theme instead of staying light.
/// Windows-only - under Linux the window decorations come from the compositor/desktop theme, which the
/// user already controls independently, so this is a no-op there.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        void ApplyNow()
        {
            try
            {
                var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { /* best effort - older Windows builds don't support this attribute */ }
        }

        if (window.IsLoaded) ApplyNow();
        else window.Opened += (_, _) => ApplyNow();
    }
}
