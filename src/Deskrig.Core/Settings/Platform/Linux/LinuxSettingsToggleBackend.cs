using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Settings;

/// <summary>
/// None of the curated Windows settings (Registry-backed toggles, HAGS, FocusAssist, Windows Update active
/// hours) have a sensible Linux equivalent - the UI hides them entirely on Linux (see
/// <see cref="WindowsSettingMetadata.SupportedOnLinux"/>), but a profile.json authored on Windows might
/// still list one, so applying it here is a clean, logged no-op instead of an exception.
/// </summary>
internal sealed class LinuxSettingsToggleBackend : ISettingsToggleBackend
{
    public bool? GetCurrentState(WindowsSetting setting) => null;

    public void Apply(WindowsSetting setting, bool enabled, ILogSink log)
        => log.Warn($"Windows-Einstellung '{WindowsSettingMetadata.All[setting].DisplayName}' wird unter Linux nicht unterstützt, übersprungen.");
}
