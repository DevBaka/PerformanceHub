using Deskrig.Core.Logging;

namespace Deskrig.Core.Power;

/// <summary>Reads/sets the active power plan. Delegates to an <see cref="IPowerPlanBackend"/> picked for
/// the running platform (powercfg.exe on Windows, powerprofilesctl on Linux).</summary>
public sealed class PowerPlanService
{
    private readonly IPowerPlanBackend _backend;

    public PowerPlanService() : this(CreateBackend()) { }

    internal PowerPlanService(IPowerPlanBackend backend) => _backend = backend;

    private static IPowerPlanBackend CreateBackend()
    {
#if DESKRIG_WINDOWS
        return new WindowsPowerPlanBackend();
#elif DESKRIG_LINUX
        if (OperatingSystem.IsLinux()) return new PowerProfilesCtlBackend();
        throw new PlatformNotSupportedException("Power-Plan-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#else
        throw new PlatformNotSupportedException("Power-Plan-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#endif
    }

    public bool TrySetActive(string? guid, ILogSink log) => _backend.TrySetActive(guid, log);
    public string? GetActiveGuid() => _backend.GetActiveGuid();
    public IReadOnlyList<(string Guid, string Name, bool Active)> GetAvailablePlans() => _backend.GetAvailablePlans();
}
