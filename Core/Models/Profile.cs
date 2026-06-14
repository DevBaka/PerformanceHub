using System.Collections.Generic;

namespace DJWinOptimizer.Core.Models
{
    public class Profile
    {
        public string Name { get; set; } = "Unnamed";
        public string? Description { get; set; }
        public string? PowerPlanGuid { get; set; }
        public ServiceToggles Services { get; set; } = new();
        public Dictionary<string, string>? ProcessPriorities { get; set; } = new(); // exeName -> High/AboveNormal/Normal
        // Legacy: single list of process names that trigger this profile (interpreted as OR)
        public List<string> Targets { get; set; } = new();
        // New: OR-list (any match triggers) and AND-list (all must be running)
        public List<string>? TargetsAny { get; set; }
        public List<string>? TargetsAll { get; set; }
        // Selection priority when multiple profiles match (higher wins). Default 0.
        public int Priority { get; set; }
        public AudioOptimizations Audio { get; set; } = new();
        public TimerResolutionMode TimerResolution { get; set; } = TimerResolutionMode.Stock;
        public ProgramSets Programs { get; set; } = new();
        public List<PackageManagerAction> PackageActions { get; set; } = new();
        public List<TweakAction> TweakActions { get; set; } = new();
    }

    public class AudioOptimizations
    {
        public bool EnableWasapiExclusive { get; set; }
        public bool PreferAsioIfAvailable { get; set; }
    }

    public enum TimerResolutionMode { Stock, OneMs }

    public class ProgramSets
    {
        public List<ProgramAction> LaunchOnEnter { get; set; } = new();
        public List<ProgramAction> KillOnExit { get; set; } = new();
    }

    public class ProgramAction
    {
        // For launch
        public string? Path { get; set; }
        public string? Args { get; set; }
        public bool Wait { get; set; }
        // If true, do not launch when a matching process is already running
        public bool SkipIfRunning { get; set; }
        // Optional working directory for the launched process
        public string? WorkingDirectory { get; set; }
        // After starting, optionally wait until the process appears (by name) up to timeout (ms). 0 = disabled
        public int WaitForRunningTimeoutMs { get; set; }
        // After finishing this action (launch or skip), delay before processing next action (ms). 0 = no delay
        public int DelayMsAfterStart { get; set; }
        // Override process name to check (otherwise inferred from Path's file name without extension)
        public string? CheckProcessName { get; set; }

        // For kill
        public string? ProcessName { get; set; }
    }
}
