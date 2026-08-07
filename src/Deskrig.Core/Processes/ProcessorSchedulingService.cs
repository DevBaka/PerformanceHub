using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

/// <summary>Toggles the "Programs" vs "Background services" processor scheduling preference. Delegates to
/// an <see cref="IProcessorSchedulingBackend"/> - Windows-only concept, logged no-op elsewhere.</summary>
public sealed class ProcessorSchedulingService
{
    private readonly IProcessorSchedulingBackend _backend;

    public ProcessorSchedulingService() : this(CreateBackend()) { }

    internal ProcessorSchedulingService(IProcessorSchedulingBackend backend) => _backend = backend;

    private static IProcessorSchedulingBackend CreateBackend()
#if DESKRIG_WINDOWS
        => new WindowsProcessorSchedulingBackend();
#else
        => new LinuxProcessorSchedulingBackend();
#endif

    public void Apply(ProcessorSchedulingMode mode, ILogSink log) => _backend.Apply(mode, log);
    public int? GetCurrentRawValue() => _backend.GetCurrentRawValue();
}
