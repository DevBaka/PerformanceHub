using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ProfileDeck.Core.Models;
using ProfileDeck.Wpf.Infrastructure;
using ProfileDeck.Wpf.ViewModels;

namespace ProfileDeck.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly GlobalHotkeyManager _hotkeys = new();
    private TrayIconManager? _tray;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        Infrastructure.DarkTitleBar.Apply(this);
        _vm = new MainViewModel(App.Log);
        DataContext = _vm;

        _tray = new TrayIconManager(
            getProfiles: () => _vm.SystemProfiles.ToList(),
            applyProfile: p => Dispatcher.Invoke(() => _vm.ApplySystemProfile(p)),
            restorePrevious: () => Dispatcher.Invoke(_vm.RestorePrevious),
            showWindow: () => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }),
            exit: () => Dispatcher.Invoke(() => { _reallyExit = true; Close(); }));

        RegisterHotkeys();
        App.Log.Info("ProfileDeck gestartet.");
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => LogTextBox.ScrollToEnd();

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LogText))
            Clipboard.SetText(_vm.LogText);
    }

    private void RegisterHotkeys()
    {
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

    private void DisplayProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditDisplayProfile_Click(sender, e);

    private void ApplyDisplayProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile != null) _vm.ApplyDisplayProfile(_vm.SelectedDisplayProfile);
    }

    private void DryRunDisplayProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile != null) _vm.ApplyDisplayProfile(_vm.SelectedDisplayProfile, dryRun: true);
    }

    private void NewDisplayProfileFromCurrent_Click(object sender, RoutedEventArgs e) => OpenDisplayEditor(null);

    private void NewDisplayProfileEmpty_Click(object sender, RoutedEventArgs e)
        => OpenDisplayEditor(new DisplayProfile { Name = "Neues Profil" });

    private void EditDisplayProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile != null) OpenDisplayEditor(_vm.SelectedDisplayProfile);
    }

    private void OpenDisplayEditor(DisplayProfile? existing)
    {
        var win = new DisplayProfileEditorWindow(_vm.DisplayManager, existing) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
        {
            if (existing != null && existing.Name != win.Result.Name)
                _vm.DisplayRepo.Delete(existing.Name);
            _vm.DisplayRepo.Save(win.Result);
            _vm.ReloadDisplayProfiles();
        }
    }

    private void DeleteDisplayProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDisplayProfile == null) return;
        if (Confirm($"Display-Profil '{_vm.SelectedDisplayProfile.Name}' wirklich löschen?"))
        {
            _vm.DisplayRepo.Delete(_vm.SelectedDisplayProfile.Name);
            _vm.ReloadDisplayProfiles();
        }
    }

    // --- System profiles ---

    private void SystemProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSystemProfile_Click(sender, e);

    private void ApplySystemProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile != null) _vm.ApplySystemProfile(_vm.SelectedSystemProfile);
    }

    private void DryRunSystemProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile != null) _vm.ApplySystemProfile(_vm.SelectedSystemProfile, dryRun: true);
    }

    private void NewSystemProfile_Click(object sender, RoutedEventArgs e) => OpenSystemEditor(null);

    private void EditSystemProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile != null) OpenSystemEditor(_vm.SelectedSystemProfile);
    }

    private void DuplicateSystemProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(_vm.SelectedSystemProfile);
        var copy = System.Text.Json.JsonSerializer.Deserialize<SystemProfile>(json)!;
        copy.Name = _vm.SelectedSystemProfile.Name + " (Kopie)";
        _vm.SystemRepo.Save(copy);
        _vm.ReloadSystemProfiles();
    }

    private void OpenSystemEditor(SystemProfile? existing)
    {
        var win = new SystemProfileEditorWindow(_vm.DisplayRepo, existing) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
        {
            if (existing != null && existing.Name != win.Result.Name)
                _vm.SystemRepo.Delete(existing.Name);
            _vm.SystemRepo.Save(win.Result);
            _vm.ReloadSystemProfiles();
            RegisterHotkeys();
            _tray?.RebuildMenu();
        }
    }

    private void DeleteSystemProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSystemProfile == null) return;
        if (Confirm($"System-Profil '{_vm.SelectedSystemProfile.Name}' wirklich löschen?"))
        {
            _vm.SystemRepo.Delete(_vm.SelectedSystemProfile.Name);
            _vm.ReloadSystemProfiles();
            RegisterHotkeys();
            _tray?.RebuildMenu();
        }
    }

    private void RestorePrevious_Click(object sender, RoutedEventArgs e) => _vm.RestorePrevious();

    private static bool Confirm(string message)
        => MessageBox.Show(message, "ProfileDeck", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // --- Lifecycle: closing the window minimizes to tray; only the tray "Beenden" entry really exits. ---

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_reallyExit)
        {
            _hotkeys.Dispose();
            _tray?.Dispose();
            return;
        }
        e.Cancel = true;
        Hide();
    }
}
