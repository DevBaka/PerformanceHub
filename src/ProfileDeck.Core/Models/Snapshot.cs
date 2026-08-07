namespace ProfileDeck.Core.Models;

/// <summary>Captured system state before a profile is applied, so it can be undone with "Restore Previous".</summary>
public sealed class Snapshot
{
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string? PreviousSystemProfileName { get; set; }

    public List<DisplayProfileEntry> Displays { get; set; } = new();
    public string? PowerPlanGuid { get; set; }
    public ProcessorSchedulingMode? ProcessorScheduling { get; set; }
    public Dictionary<string, bool> SettingToggles { get; set; } = new(); // WindowsSetting name -> previous Enabled
    public Dictionary<string, ServiceSnapshotState> ServiceStates { get; set; } = new();
    public string? DefaultAudioOutputId { get; set; }
    public string? DefaultAudioInputId { get; set; }
}

public sealed class ServiceSnapshotState
{
    public string StartupType { get; set; } = "";
    public bool WasRunning { get; set; }
}
