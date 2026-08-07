namespace ProfileDeck.Core.Models;

public sealed class SystemProfile
{
    public string Name { get; set; } = "Unnamed";
    public string? Description { get; set; }

    /// <summary>Name of a DisplayProfile to apply together with this profile. Optional.</summary>
    public string? DisplayProfileName { get; set; }

    /// <summary>Global hotkey, e.g. "Ctrl+Alt+D1". Optional.</summary>
    public string? Hotkey { get; set; }

    public List<LaunchProgramAction> ProgramsToLaunch { get; set; } = new();
    public List<string> ProcessesToKill { get; set; } = new();
    public List<ServiceAction> Services { get; set; } = new();
    public List<SettingToggleAction> SettingToggles { get; set; } = new();
    public string? PowerPlanGuid { get; set; }
    public ProcessorSchedulingMode? ProcessorScheduling { get; set; }
    public List<ProcessPriorityEntry> ProcessPriorities { get; set; } = new();
    public string? DefaultAudioOutputId { get; set; }
    public string? DefaultAudioInputId { get; set; }
}

public sealed class LaunchProgramAction
{
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int DelayAfterMs { get; set; }
    public string? Priority { get; set; } // Idle/BelowNormal/Normal/AboveNormal/High/Realtime
    public bool RunAsAdmin { get; set; }
    public bool WaitForWindow { get; set; }
    public int WaitForWindowTimeoutMs { get; set; } = 15000;
    public bool SkipIfAlreadyRunning { get; set; }
}

public sealed class ServiceAction
{
    public string ServiceName { get; set; } = "";
    public ServiceDesiredState DesiredState { get; set; } = ServiceDesiredState.NoChange;
    public ServiceStartupType? StartupType { get; set; }
}

public enum ServiceDesiredState { NoChange, Start, Stop }
public enum ServiceStartupType { Automatic, Manual, Disabled }

public sealed class SettingToggleAction
{
    public WindowsSetting Setting { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>Known, curated set of togglable Windows settings (deliberately not free-form registry access).</summary>
public enum WindowsSetting
{
    VisualEffectsAndAnimations,
    Transparency,
    GameMode,
    GameBar,
    HardwareAcceleratedGpuScheduling,
    FocusAssist,
    WindowsUpdateActiveHoursAuto,
}

public enum ProcessorSchedulingMode { ProgramsFocused, BackgroundServicesFocused }

public sealed class ProcessPriorityEntry
{
    public string ProcessName { get; set; } = "";
    public string Priority { get; set; } = "Normal"; // Idle/BelowNormal/Normal/AboveNormal/High/Realtime
}
