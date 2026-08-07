using System.Collections.ObjectModel;
using System.Text;
using ProfileDeck.Core.Display;
using ProfileDeck.Core.Engine;
using ProfileDeck.Core.Logging;
using ProfileDeck.Core.Models;
using ProfileDeck.Core.Persistence;
using ProfileDeck.Wpf.Infrastructure;

namespace ProfileDeck.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public DisplayManager DisplayManager { get; } = new();
    public ProfileRepository<DisplayProfile> DisplayRepo { get; } = new(AppPaths.DisplayProfilesDir, p => p.Name);
    public ProfileRepository<SystemProfile> SystemRepo { get; } = new(AppPaths.SystemProfilesDir, p => p.Name);
    public SystemProfileEngine Engine { get; }
    public ILogSink Log { get; }

    public ObservableCollection<DisplayProfile> DisplayProfiles { get; } = new();
    public ObservableCollection<SystemProfile> SystemProfiles { get; } = new();

    private readonly StringBuilder _logBuilder = new();
    private readonly Queue<int> _logLineLengths = new();
    private const int MaxLogLines = 1000;

    private string _logText = "";
    /// <summary>Plain-text log, newest at the bottom, so it can be selected/copied like any other text.</summary>
    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    private DisplayProfile? _selectedDisplayProfile;
    public DisplayProfile? SelectedDisplayProfile
    {
        get => _selectedDisplayProfile;
        set => SetField(ref _selectedDisplayProfile, value);
    }

    private SystemProfile? _selectedSystemProfile;
    public SystemProfile? SelectedSystemProfile
    {
        get => _selectedSystemProfile;
        set => SetField(ref _selectedSystemProfile, value);
    }

    private string _statusText = "Bereit.";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    // Guards against overlapping SetDisplayConfig calls (e.g. impatient double/triple-clicking while a switch
    // is still settling) - stacking another topology change on top of one the GPU driver hasn't finished
    // processing yet has been observed to hang the display engine hard enough to force a full Windows reboot.
    private bool _isApplyingDisplay;

    public MainViewModel(ILogSink log)
    {
        Log = log;
        Engine = new SystemProfileEngine(log, DisplayManager, DisplayRepo);
        Log.EntryLogged += entry =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => AppendLogLine(entry));

        ReloadDisplayProfiles();
        ReloadSystemProfiles();
    }

    private void AppendLogLine(LogEntry entry)
    {
        var line = $"[{entry.TimestampUtc.ToLocalTime():HH:mm:ss}] {entry.Level}: {entry.Message}\n";
        _logBuilder.Append(line);
        _logLineLengths.Enqueue(line.Length);

        while (_logLineLengths.Count > MaxLogLines)
            _logBuilder.Remove(0, _logLineLengths.Dequeue());

        LogText = _logBuilder.ToString();
    }

    public void ReloadDisplayProfiles()
    {
        var selectedName = SelectedDisplayProfile?.Name;
        DisplayProfiles.Clear();
        foreach (var p in DisplayRepo.GetAll()) DisplayProfiles.Add(p);
        SelectedDisplayProfile = DisplayProfiles.FirstOrDefault(p => p.Name == selectedName) ?? DisplayProfiles.FirstOrDefault();
    }

    public void ReloadSystemProfiles()
    {
        var selectedName = SelectedSystemProfile?.Name;
        SystemProfiles.Clear();
        foreach (var p in SystemRepo.GetAll()) SystemProfiles.Add(p);
        SelectedSystemProfile = SystemProfiles.FirstOrDefault(p => p.Name == selectedName) ?? SystemProfiles.FirstOrDefault();
    }

    public void ApplyDisplayProfile(DisplayProfile profile, bool dryRun = false)
    {
        if (_isApplyingDisplay)
        {
            Log.Warn($"Display-Profil '{profile.Name}' ignoriert - vorheriger Wechsel läuft noch, bitte kurz warten.");
            return;
        }

        _isApplyingDisplay = true;
        try
        {
            StatusText = $"Wende Display-Profil '{profile.Name}' an...";
            var result = DisplayManager.Apply(profile, Log, dryRun);
            StatusText = result.Success ? $"'{profile.Name}' angewendet." : $"'{profile.Name}' fehlgeschlagen.";
        }
        finally
        {
            _isApplyingDisplay = false;
        }
    }

    public void ApplySystemProfile(SystemProfile profile, bool dryRun = false)
    {
        // System-Profile can carry a Display-Profile and apply it via the Engine, so this needs the same guard.
        if (_isApplyingDisplay)
        {
            Log.Warn($"System-Profil '{profile.Name}' ignoriert - vorheriger Wechsel läuft noch, bitte kurz warten.");
            return;
        }

        _isApplyingDisplay = true;
        try
        {
            StatusText = $"Wende System-Profil '{profile.Name}' an...";
            var result = Engine.Apply(profile, dryRun);
            StatusText = result.Success ? $"'{profile.Name}' angewendet." : $"'{profile.Name}' fehlgeschlagen.";
        }
        finally
        {
            _isApplyingDisplay = false;
        }
    }

    public void RestorePrevious()
    {
        if (_isApplyingDisplay)
        {
            Log.Warn("Wiederherstellung ignoriert - vorheriger Wechsel läuft noch, bitte kurz warten.");
            return;
        }

        _isApplyingDisplay = true;
        try
        {
            StatusText = "Stelle vorherigen Zustand wieder her...";
            var result = Engine.RestorePrevious();
            StatusText = result.Success ? "Wiederhergestellt." : "Wiederherstellung fehlgeschlagen.";
        }
        finally
        {
            _isApplyingDisplay = false;
        }
    }
}
