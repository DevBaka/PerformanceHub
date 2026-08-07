using Deskrig.Core.Logging;

namespace Deskrig.Core.Audio;

/// <summary>Platform-specific default-audio-device backend (CoreAudio on Windows, PipeWire/PulseAudio via
/// `pactl` on Linux), selected by <see cref="AudioDeviceService"/> at construction time.</summary>
public interface IAudioBackend
{
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    string? GetDefaultOutputId();
    string? GetDefaultInputId();
    void SetDefaultOutput(string? deviceId, ILogSink log);
    void SetDefaultInput(string? deviceId, ILogSink log);
}
