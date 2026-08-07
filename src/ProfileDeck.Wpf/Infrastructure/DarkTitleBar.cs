using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ProfileDeck.Wpf.Infrastructure;

/// <summary>Makes the native (OS-drawn) title bar follow the app's dark theme instead of staying light.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window)
    {
        void ApplyNow()
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { /* best effort - older Windows builds don't support this attribute */ }
        }

        if (window.IsLoaded) ApplyNow();
        else window.SourceInitialized += (_, _) => ApplyNow();
    }
}
