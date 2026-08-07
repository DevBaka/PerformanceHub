using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Settings;

/// <summary>Reads/applies the curated set of togglable Windows settings. Delegates to an
/// <see cref="ISettingsToggleBackend"/> picked for the running platform - a genuine registry-backed
/// implementation on Windows, a logged no-op on Linux (see <see cref="WindowsSettingMetadata"/>).</summary>
public sealed class SettingsToggleService
{
    private readonly ISettingsToggleBackend _backend;

    public SettingsToggleService() : this(CreateBackend()) { }

    internal SettingsToggleService(ISettingsToggleBackend backend) => _backend = backend;

    private static ISettingsToggleBackend CreateBackend()
#if DESKRIG_WINDOWS
        => new WindowsSettingsToggleBackend();
#else
        => new LinuxSettingsToggleBackend();
#endif

    public bool? GetCurrentState(WindowsSetting setting) => _backend.GetCurrentState(setting);
    public void Apply(WindowsSetting setting, bool enabled, ILogSink log) => _backend.Apply(setting, enabled, log);
}
