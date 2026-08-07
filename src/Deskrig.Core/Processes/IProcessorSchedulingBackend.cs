using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

public interface IProcessorSchedulingBackend
{
    void Apply(ProcessorSchedulingMode mode, ILogSink log);
    int? GetCurrentRawValue();
}
