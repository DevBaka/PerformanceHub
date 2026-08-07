namespace Deskrig.Core.Services;

/// <summary>Platform-neutral service run state - deliberately not the BCL's Windows-only
/// System.ServiceProcess.ServiceControllerStatus, so this type (and everything built on it: snapshots,
/// the engine, the CLI) works the same on every supported platform.</summary>
public enum ServiceRunState
{
    Unknown,
    Stopped,
    StartPending,
    StopPending,
    Running,
    Paused,
}
