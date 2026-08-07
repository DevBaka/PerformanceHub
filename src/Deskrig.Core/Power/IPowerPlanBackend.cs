using Deskrig.Core.Logging;

namespace Deskrig.Core.Power;

/// <summary>Platform-specific power-plan backend (powercfg.exe on Windows, powerprofilesctl on Linux).
/// The "guid" name is kept from the Windows side for the public API, but on Linux it's really just the
/// opaque plan identifier ("power-saver"/"balanced"/"performance") - the model already treats it as a
/// plain string either way.</summary>
public interface IPowerPlanBackend
{
    bool TrySetActive(string? guid, ILogSink log);
    string? GetActiveGuid();
    IReadOnlyList<(string Guid, string Name, bool Active)> GetAvailablePlans();
}
