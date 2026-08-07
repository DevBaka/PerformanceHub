using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

/// <summary>Win32PrioritySeparation has no Linux equivalent (the closest concept, CFS scheduler tuning via
/// sysctl kernel.sched_*, isn't a comparable two-preset toggle) - logged no-op, UI hides the option
/// entirely on Linux, same treatment as the Windows-only settings toggles.</summary>
internal sealed class LinuxProcessorSchedulingBackend : IProcessorSchedulingBackend
{
    public void Apply(ProcessorSchedulingMode mode, ILogSink log)
        => log.Warn("Prozessor-Scheduling (Programme/Hintergrunddienste) wird unter Linux nicht unterstützt, übersprungen.");

    public int? GetCurrentRawValue() => null;
}
