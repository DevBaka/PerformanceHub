using Deskrig.Core.Models;

namespace Deskrig.Core.Settings;

/// <summary>SupportedOnLinux is always false today - every one of these settings is a Windows registry/API
/// concept with no Linux equivalent (see README "Linux"). Kept as a per-setting flag rather than a single
/// platform check so the UI logic ("hide unsupported toggles") doesn't need to know *why* each one doesn't
/// apply, and so a future setting that genuinely has a Linux equivalent can flip it without touching callers.</summary>
public sealed record WindowsSettingInfo(string DisplayName, bool RequiresExplorerRestart, bool RequiresReboot, bool BestEffort, bool SupportedOnLinux = false);

/// <summary>UI-facing metadata for each togglable setting, so the editor can flag "needs restart" actions
/// and hide settings that don't apply on the running platform.</summary>
public static class WindowsSettingMetadata
{
    public static readonly IReadOnlyDictionary<WindowsSetting, WindowsSettingInfo> All = new Dictionary<WindowsSetting, WindowsSettingInfo>
    {
        [WindowsSetting.VisualEffectsAndAnimations] = new("Visuelle Effekte & Animationen", false, false, false),
        [WindowsSetting.Transparency] = new("Transparenzeffekte", true, false, false),
        [WindowsSetting.GameMode] = new("Spielmodus", false, false, false),
        [WindowsSetting.GameBar] = new("Xbox Game Bar", true, false, false),
        [WindowsSetting.HardwareAcceleratedGpuScheduling] = new("Hardwarebeschleunigtes GPU-Scheduling", false, true, false),
        [WindowsSetting.FocusAssist] = new("Benachrichtigungen (Focus Assist, Näherung)", false, false, true),
        [WindowsSetting.WindowsUpdateActiveHoursAuto] = new("Windows Update: aktive Stunden automatisch", false, false, true),
    };
}
