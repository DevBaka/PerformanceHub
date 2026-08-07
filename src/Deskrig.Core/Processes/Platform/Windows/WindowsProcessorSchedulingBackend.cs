using Microsoft.Win32;
using Deskrig.Core.Interop;
using Deskrig.Core.Logging;
using Deskrig.Core.Models;

namespace Deskrig.Core.Processes;

/// <summary>
/// Toggles the "Programs" vs "Background services" processor scheduling option
/// (System Properties > Advanced > Performance > Advanced) via Win32PrioritySeparation.
/// </summary>
internal sealed class WindowsProcessorSchedulingBackend : IProcessorSchedulingBackend
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ValueName = "Win32PrioritySeparation";

    public void Apply(ProcessorSchedulingMode mode, ILogSink log)
    {
        // 38 (0x26) favors foreground programs (short, variable quanta); 24 (0x18) favors background
        // services (long, fixed quanta) - the same values System Properties writes for these two presets.
        int value = mode == ProcessorSchedulingMode.ProgramsFocused ? 38 : 24;
        if (RegistryUtil.TrySetDword(RegistryHive.LocalMachine, KeyPath, ValueName, value, out var error))
            log.Info($"Prozessor-Scheduling: {mode} (Win32PrioritySeparation={value}).");
        else
            log.Warn($"Prozessor-Scheduling konnte nicht gesetzt werden: {error}");
    }

    public int? GetCurrentRawValue() => RegistryUtil.TryGetDword(RegistryHive.LocalMachine, KeyPath, ValueName);
}
