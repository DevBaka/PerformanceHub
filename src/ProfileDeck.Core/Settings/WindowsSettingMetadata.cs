using ProfileDeck.Core.Models;

namespace ProfileDeck.Core.Settings;

public sealed record WindowsSettingInfo(string DisplayName, bool RequiresExplorerRestart, bool RequiresReboot, bool BestEffort);

/// <summary>UI-facing metadata for each togglable setting, so the editor can flag "needs restart" actions.</summary>
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
