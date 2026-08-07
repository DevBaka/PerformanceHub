using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Settings;

/// <summary>Platform-specific backend for the curated Windows-settings toggles. On platforms where a
/// setting has no equivalent (currently: all of them, on Linux - see <see cref="WindowsSettingMetadata"/>),
/// the backend is a no-op that logs and returns "unknown" rather than throwing, so a profile authored on
/// Windows can still be applied on Linux without crashing.</summary>
public interface ISettingsToggleBackend
{
    bool? GetCurrentState(WindowsSetting setting);
    void Apply(WindowsSetting setting, bool enabled, ILogSink log);
}
