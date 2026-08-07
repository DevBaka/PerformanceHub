using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using Deskrig.Core.Logging;

namespace Deskrig.Core.Audio;

internal sealed class WindowsAudioBackend : IAudioBackend
{
    private readonly CoreAudioController _controller = new();

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => GetDevices(DeviceType.Playback);
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => GetDevices(DeviceType.Capture);

    private List<AudioDeviceInfo> GetDevices(DeviceType type)
        => _controller.GetDevices(type, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.Id.ToString(), d.FullName, d.IsDefaultDevice))
            .ToList();

    public string? GetDefaultOutputId() => _controller.GetDefaultDevice(DeviceType.Playback, Role.Multimedia)?.Id.ToString();
    public string? GetDefaultInputId() => _controller.GetDefaultDevice(DeviceType.Capture, Role.Multimedia)?.Id.ToString();

    public void SetDefaultOutput(string? deviceId, ILogSink log) => SetDefault(deviceId, DeviceType.Playback, log);
    public void SetDefaultInput(string? deviceId, ILogSink log) => SetDefault(deviceId, DeviceType.Capture, log);

    private void SetDefault(string? deviceId, DeviceType type, ILogSink log)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        if (!Guid.TryParse(deviceId, out var guid))
        {
            log.Warn($"Ungültige Audiogeräte-Id: '{deviceId}'.");
            return;
        }

        try
        {
            var device = _controller.GetDevice(guid, DeviceState.Active);
            if (device == null)
            {
                log.Warn($"Audiogerät '{deviceId}' nicht gefunden oder nicht aktiv - übersprungen.");
                return;
            }

            _controller.SetDefaultDevice(device);
            _controller.SetDefaultCommunicationsDevice(device);
            log.Info($"Standard-{(type == DeviceType.Playback ? "Wiedergabe" : "Aufnahme")}gerät gesetzt: {device.FullName}.");
        }
        catch (Exception ex)
        {
            log.Warn($"Audiogerät konnte nicht gesetzt werden: {ex.Message}");
        }
    }
}
