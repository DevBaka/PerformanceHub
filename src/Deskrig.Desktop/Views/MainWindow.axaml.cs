using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Deskrig.Core.Models;
using Deskrig.Desktop.Infrastructure;
using Deskrig.Desktop.ViewModels;

namespace Deskrig.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private GlobalHotkeyManager? _hotkeys;
    private TrayIconManager? _tray;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _vm = new MainViewModel(App.Log);
        DataContext = _vm;

        _tray = new TrayIconManager(
            getProfiles: () => _vm.SystemProfiles.ToList(),
            applyProfile: p => _vm.ApplySystemProfile(p),
            restorePrevious: () => _vm.RestorePrevious(),
            showWindow: () => { Show(); WindowState = WindowState.Normal; Activate(); },
            exit: () => { _reallyExit = true; Close(); });

        Closing += MainWindow_Closing;
        Opened += (_, _) =>
        {
            _hotkeys = new GlobalHotkeyManager(this);
            RegisterHotkeys();
        };

        App.Log.Info("Deskrig-Fenster geöffnet.");
    }

    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.LogText)) return;
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(_vm.LogText);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        foreach (var profile in _vm.SystemProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Hotkey)) continue;
            var p = profile;
            if (!_hotkeys.TryRegister(profile.Hotkey, () => _vm.ApplySystemProfile(p), out var error))
                App.Log.Warn(error ?? $"Hotkey für '{profile.Name}' konnte nicht registriert werden.");
        }
    }

    // --- Display profiles ---

    private void DisplayProfileList_DoubleTapped(object? sender, TappedEventArgs e) => _ = EditDisplayProfileAsync();

    private void ApplyDisplayProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile != null) _vm.ApplyDisplayProfile(_vm.SelectedDisplayProfile);
    }

    private void DryRunDisplayProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile != null) _vm.ApplyDisplayProfile(_vm.SelectedDisplayProfile, dryRun: true);
    }

    private void NewDisplayProfileFromCurrent_Click(object? sender, RoutedEventArgs e) => _ = OpenDisplayEditorAsync(null);

    private void NewDisplayProfileEmpty_Click(object? sender, RoutedEventArgs e)
        => _ = OpenDisplayEditorAsync(new DisplayProfile { Name = "Neues Profil" });

    private void EditDisplayProfile_Click(object? sender, RoutedEventArgs e) => _ = EditDisplayProfileAsync();

    private Task EditDisplayProfileAsync()
        => _vm.SelectedDisplayProfile != null ? OpenDisplayEditorAsync(_vm.SelectedDisplayProfile) : Task.CompletedTask;

    private async Task OpenDisplayEditorAsync(DisplayProfile? existing)
    {
        var win = new DisplayProfileEditorWindow(_vm.DisplayManager, existing);
        var ok = await win.ShowDialog<bool>(this);
        if (ok && win.Result != null)
        {
            if (existing != null && existing.Name != win.Result.Name)
                _vm.DisplayRepo.Delete(existing.Name);
            _vm.DisplayRepo.Save(win.Result);
            _vm.ReloadDisplayProfiles();
        }
    }

    private async void DeleteDisplayProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile == null) return;
        if (await Dialogs.ConfirmAsync(this, $"Display-Profil '{_vm.SelectedDisplayProfile.Name}' wirklich löschen?"))
        {
            _vm.DisplayRepo.Delete(_vm.SelectedDisplayProfile.Name);
            _vm.ReloadDisplayProfiles();
        }
    }

    // --- System profiles ---

    private void SystemProfileList_DoubleTapped(object? sender, TappedEventArgs e) => _ = EditSystemProfileAsync();

    private void ApplySystemProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile != null) _vm.ApplySystemProfile(_vm.SelectedSystemProfile);
    }

    private void DryRunSystemProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile != null) _vm.ApplySystemProfile(_vm.SelectedSystemProfile, dryRun: true);
    }

    private void NewSystemProfile_Click(object? sender, RoutedEventArgs e) => _ = OpenSystemEditorAsync(null);

    private void EditSystemProfile_Click(object? sender, RoutedEventArgs e) => _ = EditSystemProfileAsync();

    private Task EditSystemProfileAsync()
        => _vm.SelectedSystemProfile != null ? OpenSystemEditorAsync(_vm.SelectedSystemProfile) : Task.CompletedTask;

    private void DuplicateSystemProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(_vm.SelectedSystemProfile);
        var copy = System.Text.Json.JsonSerializer.Deserialize<SystemProfile>(json)!;
        copy.Name = _vm.SelectedSystemProfile.Name + " (Kopie)";
        _vm.SystemRepo.Save(copy);
        _vm.ReloadSystemProfiles();
    }

    private async Task OpenSystemEditorAsync(SystemProfile? existing)
    {
        var win = new SystemProfileEditorWindow(_vm.DisplayRepo, existing);
        var ok = await win.ShowDialog<bool>(this);
        if (ok && win.Result != null)
        {
            if (existing != null && existing.Name != win.Result.Name)
                _vm.SystemRepo.Delete(existing.Name);
            _vm.SystemRepo.Save(win.Result);
            _vm.ReloadSystemProfiles();
            RegisterHotkeys();
            _tray?.RebuildMenu();
        }
    }

    private async void DeleteSystemProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile == null) return;
        if (await Dialogs.ConfirmAsync(this, $"System-Profil '{_vm.SelectedSystemProfile.Name}' wirklich löschen?"))
        {
            _vm.SystemRepo.Delete(_vm.SelectedSystemProfile.Name);
            _vm.ReloadSystemProfiles();
            RegisterHotkeys();
            _tray?.RebuildMenu();
        }
    }

    private void RestorePrevious_Click(object? sender, RoutedEventArgs e) => _vm.RestorePrevious();

    // --- Lifecycle: closing the window minimizes to tray; only the tray "Beenden" entry really exits. ---

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_reallyExit)
        {
            _hotkeys?.Dispose();
            _tray?.Dispose();
            return;
        }
        e.Cancel = true;
        Hide();
    }
}
