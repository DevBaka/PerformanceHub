using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Services;

/// <summary>Starts/stops services and reads their status/startup type. Delegates to an
/// <see cref="IServiceControlBackend"/> picked for the running platform (sc.exe/ServiceController on
/// Windows, systemctl on Linux).</summary>
public sealed class ServiceControlService
{
    private readonly IServiceControlBackend _backend;

    public ServiceControlService() : this(CreateBackend()) { }

    internal ServiceControlService(IServiceControlBackend backend) => _backend = backend;

    private static IServiceControlBackend CreateBackend()
    {
#if DESKRIG_WINDOWS
        return new WindowsServiceControlBackend();
#elif DESKRIG_LINUX
        if (OperatingSystem.IsLinux()) return new SystemctlServiceControlBackend();
        throw new PlatformNotSupportedException("Dienste-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#else
        throw new PlatformNotSupportedException("Dienste-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#endif
    }

    public void Apply(IEnumerable<ServiceAction> actions, ILogSink log) => _backend.Apply(actions, log);
    public string? GetStartupType(string serviceName) => _backend.GetStartupType(serviceName);
    public ServiceRunState? GetStatus(string serviceName) => _backend.GetStatus(serviceName);

    public IReadOnlyList<(string Name, string DisplayName, ServiceRunState Status, string? StartupType)> ListAll()
        => _backend.ListAll();
}
