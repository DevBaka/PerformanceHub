using System;
using PerformanceHub.Core.Interfaces;
using PerformanceHub.Services;
using PerformanceHub.Utils;

namespace PerformanceHub.Core
{
    /// <summary>
    /// App-wide service locator for the prototype. In a larger app, prefer DI.
    /// </summary>
    public sealed class App
    {
        public static App? Instance { get; private set; }
        private static event Action<string>? OnLog;
        private static readonly List<string> _logBuffer = new();

        public static void Init()
        {
            if (Instance != null) return;
            PortablePaths.Initialize();
            Instance = new App();
        }

        public static void SubscribeToLogs(Action<string> onLog)
        {
            OnLog += onLog;
            // Send buffered logs
            lock (_logBuffer)
            {
                foreach (var logLine in _logBuffer)
                {
                    onLog(logLine);
                }
                _logBuffer.Clear();
            }
        }

        internal static void InvokeLog(string logLine)
        {
            lock (_logBuffer)
            {
                if (OnLog == null || OnLog.GetInvocationList().Length == 0)
                {
                    _logBuffer.Add(logLine);
                }
                else
                {
                    OnLog?.Invoke(logLine);
                }
            }
        }

        public ILogger Logger { get; }
        public IProfileManager Profiles { get; }
        public IPowerPlanService PowerPlans { get; }
        public IServiceManager ServiceManager { get; }
        public IProcessPriorityService ProcPriority { get; }
        public IAutoSwitchEngine AutoSwitch { get; }
        public IHotkeyManager Hotkeys { get; }
        public ITimerResolutionManager TimerResolution { get; }
        public IGameBarManager GameBar { get; }
        public IProcessLauncher Launcher { get; }
        public IPreFlightChecker Preflight { get; }
        public IAudioManager Audio { get; }
        public IPackageManager PackageManager { get; }
        public ISystemTweaksManager SystemTweaks { get; }
        public PerformanceHub.Settings.AppSettings Config { get; }

        private App()
        {
            Logger = new FileLogger();
            Config = PerformanceHub.Settings.AppSettings.Load();
            Profiles = new ProfileManager(Logger);
            PowerPlans = new PowerPlanService(Logger);
            ServiceManager = new WindowsServiceManager(Logger);
            ProcPriority = new ProcessPriorityService(Logger);
            AutoSwitch = new AutoSwitchEngine(Logger, Profiles, ProcPriority, PowerPlans);
            Hotkeys = new HotkeyManager(Logger);
            TimerResolution = new TimerResolutionManager(Logger);
            GameBar = new GameBarManager(Logger);
            Launcher = new ProcessLauncher(Logger);
            Preflight = new PreFlightChecker(Logger);
            Audio = new AudioManager(Logger);
            PackageManager = new PackageManager(Logger);
            SystemTweaks = new SystemTweaksManager(Logger);
        }

        public void Shutdown()
        {
            try
            {
                AutoSwitch.Stop();
                (AutoSwitch as IDisposable)?.Dispose();
                Hotkeys.Dispose();
                // Optionally restore default profile as failsafe
                if (!string.IsNullOrWhiteSpace(Config.DefaultProfileOnExit))
                {
                    Profiles.ApplyProfileByName(Config.DefaultProfileOnExit);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Shutdown error", ex);
            }
        }
    }
}
