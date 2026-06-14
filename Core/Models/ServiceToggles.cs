namespace PerformanceHub.Core.Models
{
    public class ServiceToggles
    {
        public bool DisableDefenderRealtime { get; set; }
        public bool BlockScheduledScans { get; set; }
        public bool PauseOneDrive { get; set; }
        public bool StopOneDrive { get; set; }
        public bool DisableSearchIndex { get; set; }
        public bool DisableSysMain { get; set; }
        public bool DisableGameDvr { get; set; }
        public bool DisablePrintSpooler { get; set; }
        public bool PauseWindowsUpdates { get; set; }
        public bool DisableXboxServices { get; set; }
        public bool ReduceTelemetry { get; set; }
        public bool DisableConsumerFeatures { get; set; }
        public bool DisableActivityHistory { get; set; }
    }
}
