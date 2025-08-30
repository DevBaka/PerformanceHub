using DJWinOptimizer.Utils;

namespace DJWinOptimizer.Settings
{
    public class AppSettings
    {
        public string? DefaultProfileOnExit { get; set; } = "Balanced";
        public bool StartMinimizedToTray { get; set; } = false;
        public bool AutoStartAutoSwitch { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public HotkeysSettings Hotkeys { get; set; } = new HotkeysSettings();
        public MonitoringSettings Monitoring { get; set; } = new MonitoringSettings();

        public static AppSettings Load()
        {
            var s = JsonUtil.Load<AppSettings>(PortablePaths.SettingsFile) ?? new AppSettings();
            Save(s);
            return s;
        }

        public static void Save(AppSettings s) => JsonUtil.Save(PortablePaths.SettingsFile, s);
    }

    public class HotkeysSettings
    {
        // store as human-readable combos, e.g., "Ctrl+Alt+A"
        public string ToggleAutoSwitch { get; set; } = "Ctrl+Alt+A";
        public string ShowHideWindow { get; set; } = "Ctrl+Alt+W";
        public string ApplySelectedProfile { get; set; } = "Ctrl+Alt+P";
    }

    public class MonitoringSettings
    {
        // Utilization thresholds (%)
        public float CpuWarn { get; set; } = 90f;
        public float CpuCrit { get; set; } = 98f;
        public float DiskWarn { get; set; } = 90f;
        public float DiskCrit { get; set; } = 98f;

        // Temperatures (°C)
        public float CpuTempWarn { get; set; } = 85f;
        public float CpuTempCrit { get; set; } = 90f;
        public float GpuTempWarn { get; set; } = 85f;
        public float GpuTempCrit { get; set; } = 90f;

        // Driver latency tab thresholds based on % time
        public float DpcWarn { get; set; } = 8f;
        public float DpcCrit { get; set; } = 20f;
        public float IsrWarn { get; set; } = 3f;
        public float IsrCrit { get; set; } = 10f;

        // Driver latency Events/s thresholds for row coloring (applies to DPC/Interrupt and *.sys/*.dll)
        public float DriverEventsWarn { get; set; } = 200f;
        public float DriverEventsCrit { get; set; } = 1000f;

        // Driver latency refresh interval (milliseconds) for ETW aggregation snapshots
        public int DriverLatencyRefreshMs { get; set; } = 1000;

        // Show generic system ETW rows (PerfInfo, Thread, DiskIO, etc.) in the Driver Latencies view
        public bool ShowSystemEtwRows { get; set; } = true;
    }
}
