using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Services;

/// <summary>Platform-specific service-control backend (sc.exe/ServiceController on Windows, systemctl on
/// Linux), selected by <see cref="ServiceControlService"/> at construction time.</summary>
public interface IServiceControlBackend
{
    void Apply(IEnumerable<ServiceAction> actions, ILogSink log);
    string? GetStartupType(string serviceName);
    ServiceRunState? GetStatus(string serviceName);
    IReadOnlyList<(string Name, string DisplayName, ServiceRunState Status, string? StartupType)> ListAll();
}
