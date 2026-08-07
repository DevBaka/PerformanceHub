using System.Runtime.InteropServices;
using Microsoft.Win32;
using Deskrig.Core.Interop;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Settings;

/// <summary>
/// Applies a curated, fixed set of Windows settings (deliberately not free-form registry access).
/// FocusAssist and WindowsUpdateActiveHoursAuto rely on undocumented storage Microsoft doesn't
/// guarantee - best effort, may not take effect on every Windows build.
/// </summary>
internal sealed class WindowsSettingsToggleBackend : ISettingsToggleBackend
{
    public bool? GetCurrentState(WindowsSetting setting)
    {
        try
        {
            return setting switch
            {
                WindowsSetting.VisualEffectsAndAnimations => RegistryUtil.TryGetDword(RegistryHive.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate") != 0,
                WindowsSetting.Transparency => RegistryUtil.TryGetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") != 0,
                WindowsSetting.GameMode => RegistryUtil.TryGetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") != 0,
                WindowsSetting.GameBar => RegistryUtil.TryGetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "Enabled") != 0,
                WindowsSetting.HardwareAcceleratedGpuScheduling => RegistryUtil.TryGetDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") == 2,
                WindowsSetting.FocusAssist => RegistryUtil.TryGetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") != 0,
                WindowsSetting.WindowsUpdateActiveHoursAuto => RegistryUtil.TryGetDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "IsSmartActiveHoursEnabled") != 0,
                _ => null,
            };
        }
        catch { return null; }
    }

    public void Apply(WindowsSetting setting, bool enabled, ILogSink log)
    {
        try
        {
            bool ok = setting switch
            {
                WindowsSetting.VisualEffectsAndAnimations => SetVisualEffects(enabled, log),
                WindowsSetting.Transparency => SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", enabled, log),
                WindowsSetting.GameMode => SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", enabled, log),
                WindowsSetting.GameBar => SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "Enabled", enabled, log),
                WindowsSetting.HardwareAcceleratedGpuScheduling => SetDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", enabled ? 2 : 1, log, "HwSchMode"),
                WindowsSetting.FocusAssist => SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", !enabled, log), // FocusAssist ON -> toasts OFF
                WindowsSetting.WindowsUpdateActiveHoursAuto => SetDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "IsSmartActiveHoursEnabled", enabled, log),
                _ => false,
            };
            if (ok)
                log.Info($"Windows-Einstellung '{WindowsSettingMetadata.All[setting].DisplayName}' auf {(enabled ? "an" : "aus")} gesetzt.");
        }
        catch (Exception ex)
        {
            log.Warn($"Windows-Einstellung '{setting}' konnte nicht gesetzt werden: {ex.Message}");
        }
    }

    private static bool SetDword(RegistryHive hive, string path, string name, bool enabled, ILogSink log, string? labelOverride = null)
        => SetDword(hive, path, name, enabled ? 1 : 0, log, labelOverride);

    private static bool SetDword(RegistryHive hive, string path, string name, int value, ILogSink log, string? labelOverride = null)
    {
        if (!RegistryUtil.TrySetDword(hive, path, name, value, out var error))
        {
            log.Warn($"Registry-Schreibfehler ({labelOverride ?? name}): {error}");
            return false;
        }
        // HKCU personalization/shell settings written via raw registry writes get silently reverted by
        // Explorer/DWM's own cache unless we broadcast WM_SETTINGCHANGE - the same notification the
        // Settings app sends after a UI-driven change, so the shell treats ours as equally authoritative.
        if (hive == RegistryHive.CurrentUser)
            NativeMethods.BroadcastSettingChange(path.Contains("Personalize") ? "ImmersiveColorSet" : "Policy");
        return true;
    }

    // Visual effects: SPI_SETUIEFFECTS applies live (no logoff/restart needed) and updates the registry-backed
    // UserPreferencesMask for us - hand-editing that binary blob directly is fragile and unnecessary.
    private static bool SetVisualEffects(bool enabled, ILogSink log)
    {
        const uint SPI_SETUIEFFECTS = 0x103F;
        const uint SPIF_UPDATEINIFILE = 0x01;
        const uint SPIF_SENDCHANGE = 0x02;

        bool ok = NativeMethods.SystemParametersInfo(SPI_SETUIEFFECTS, 0, (IntPtr)(enabled ? 1 : 0), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        // MinAnimate lives under a separate struct-based call; the simple DWORD mirror keeps older apps that
        // read the registry directly (instead of asking Win32) in sync.
        RegistryUtil.TrySetDword(RegistryHive.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", enabled ? 1 : 0, out _);
        if (!ok)
            log.Warn("SystemParametersInfo(SPI_SETUIEFFECTS) fehlgeschlagen.");
        return ok;
    }

    private static class NativeMethods
    {
        private const int HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr result);

        internal static void BroadcastSettingChange(string area)
        {
            try { SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, area, SMTO_ABORTIFHUNG, 200, out _); }
            catch { /* best effort */ }
        }
    }
}
