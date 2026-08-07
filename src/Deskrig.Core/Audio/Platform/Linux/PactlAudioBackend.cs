using Deskrig.Core.Interop;
using Deskrig.Core.Logging;

namespace Deskrig.Core.Audio;

/// <summary>
/// Default-audio-device backend for Linux via `pactl` - works against both PulseAudio and PipeWire (through
/// its pulse-compatible layer, which ships by default on effectively every current desktop distro). Device
/// "Id" is the pactl sink/source name (stable across reboots, unlike a numeric index).
/// </summary>
internal sealed class PactlAudioBackend : IAudioBackend
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => GetDevices("sinks", GetDefaultOutputId());
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => GetDevices("sources", GetDefaultInputId());

    public string? GetDefaultOutputId() => RunSingleLine("get-default-sink");
    public string? GetDefaultInputId() => RunSingleLine("get-default-source");

    public void SetDefaultOutput(string? deviceId, ILogSink log) => SetDefault(deviceId, "sink", "Wiedergabe", log);
    public void SetDefaultInput(string? deviceId, ILogSink log) => SetDefault(deviceId, "source", "Aufnahme", log);

    private static void SetDefault(string? deviceId, string kind, string labelDe, ILogSink log)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        var (exitCode, _, stdErr) = ProcessRunner.Run("pactl", $"set-default-{kind} {Quote(deviceId)}");
        if (exitCode == 0)
            log.Info($"Standard-{labelDe}gerät gesetzt: {deviceId}.");
        else
            log.Warn($"Audiogerät '{deviceId}' konnte nicht gesetzt werden: {stdErr}".TrimEnd());
    }

    private static List<AudioDeviceInfo> GetDevices(string kind, string? defaultId)
    {
        var descriptions = GetDescriptions(kind);
        var (exitCode, stdOut, _) = ProcessRunner.Run("pactl", $"list short {kind}");
        var result = new List<AudioDeviceInfo>();
        if (exitCode != 0) return result;

        foreach (var line in stdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length < 2) continue;
            var name = cols[1];
            // Monitor sources (echo of a sink's output, named "<sink>.monitor") aren't real capture
            // devices - exclude them from the "input devices" list, same as Windows only lists Capture.
            if (kind == "sources" && name.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase)) continue;

            result.Add(new AudioDeviceInfo(name, descriptions.GetValueOrDefault(name, name), name == defaultId));
        }
        return result;
    }

    /// <summary>pactl's human-readable "Description:" per device, keyed by device name - falls back to the
    /// raw name (already returned by the caller) if unavailable.</summary>
    private static Dictionary<string, string> GetDescriptions(string kind)
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("pactl", $"list {kind}");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (exitCode != 0) return result;

        string? name = null;
        foreach (var rawLine in stdOut.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                name = line["Name:".Length..].Trim();
            else if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase) && name != null)
                result[name] = line["Description:".Length..].Trim();
        }
        return result;
    }

    private static string? RunSingleLine(string command)
    {
        var (exitCode, stdOut, _) = ProcessRunner.Run("pactl", command);
        if (exitCode != 0) return null;
        var line = stdOut.Trim();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
