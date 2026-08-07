using Deskrig.Core.Logging;

namespace Deskrig.Core.Audio;

/// <summary>Reads/sets the system's default playback and capture devices. Delegates to an
/// <see cref="IAudioBackend"/> picked for the running platform (CoreAudio on Windows, pactl on Linux).</summary>
public sealed class AudioDeviceService
{
    private readonly IAudioBackend _backend;

    public AudioDeviceService() : this(CreateBackend()) { }

    internal AudioDeviceService(IAudioBackend backend) => _backend = backend;

    private static IAudioBackend CreateBackend()
    {
#if DESKRIG_WINDOWS
        return new WindowsAudioBackend();
#elif DESKRIG_LINUX
        if (OperatingSystem.IsLinux()) return new PactlAudioBackend();
        throw new PlatformNotSupportedException("Audiogeräte-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#else
        throw new PlatformNotSupportedException("Audiogeräte-Verwaltung wird auf diesem Betriebssystem nicht unterstützt.");
#endif
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => _backend.GetOutputDevices();
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => _backend.GetInputDevices();
    public string? GetDefaultOutputId() => _backend.GetDefaultOutputId();
    public string? GetDefaultInputId() => _backend.GetDefaultInputId();
    public void SetDefaultOutput(string? deviceId, ILogSink log) => _backend.SetDefaultOutput(deviceId, log);
    public void SetDefaultInput(string? deviceId, ILogSink log) => _backend.SetDefaultInput(deviceId, log);
}
