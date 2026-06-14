using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PerformanceHub.Core.Models;

namespace PerformanceHub.Core.Interfaces
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? ex = null);
    }

    public interface IProfileManager
    {
        IEnumerable<Profile> GetAll();
        Profile Create(string name);
        void Save(Profile profile);
        void Delete(string name);
        Profile? GetByName(string name);
        bool ApplyProfile(Profile profile);
        bool ApplyProfileByName(string name);
        Profile? ActiveProfile { get; }
        // Portable Import/Export
        Profile? Import(string filePath);
        bool Export(string name, string destinationPath);
    }

    public interface IPowerPlanService
    {
        bool TrySetActive(string? guid, out string? error);
        bool TryClone(string baseGuid, string newName, out string? newGuid, out string? error);
        string? GetActiveGuid();
        IEnumerable<(string Guid, string Name, bool Active)> GetAvailablePlans();
    }

    public interface IServiceManager
    {
        void Apply(ServiceToggles toggles);
        void Revert(ServiceToggles toggles);
    }

    public interface IProcessPriorityService
    {
        void Apply(Dictionary<string, string>? exeToPriority);
        void Revert(Dictionary<string, string>? exeToPriority);
    }

    public interface IAutoSwitchEngine
    {
        void Start();
        void Stop();
        bool Running { get; }
        string? LastTrigger { get; }
    }

    public interface IHotkeyManager : IDisposable
    {
        bool Register(int id, Keys modifiers, Keys key, Action callback);
        void UnregisterAll();
    }

    public interface ITimerResolutionManager : IDisposable
    {
        void SetOneMillisecond(bool enable);
        bool IsOneMillisecond { get; }
    }

    public interface IGameBarManager
    {
        bool SetEnabled(bool enabled, out string? error);
    }

    public interface IProcessLauncher
    {
        void Launch(IEnumerable<ProgramAction> actions);
        void Kill(IEnumerable<ProgramAction> actions);
    }

    public interface IPreFlightChecker
    {
        IReadOnlyList<string> Run(Profile profile);
    }

    public interface IAudioManager
    {
        bool ApplyPreset(AudioOptimizations opts, out string? error);
        void CheckAudioDevices();
        bool TryRecoverAudioGraph(out string? error);
    }

    public interface IPackageManager
    {
        IEnumerable<PackageApplication> GetAvailableApplications();
        bool IsWingetAvailable();
        bool IsChocolateyAvailable();
        List<PackageManagerResult> ExecuteActions(IEnumerable<PackageManagerAction> actions);
        bool InstallChocolatey();
    }

    public interface ISystemTweaksManager
    {
        IEnumerable<SystemTweak> GetAvailableTweaks();
        List<TweakResult> ExecuteActions(IEnumerable<TweakAction> actions);
        bool IsTweakApplied(string tweakId);
    }

    public interface IVibranceService
    {
        bool TrySetVibrance(int level, out string? error);
        int? GetCurrentVibrance();
        bool IsAvailable();
    }
}
