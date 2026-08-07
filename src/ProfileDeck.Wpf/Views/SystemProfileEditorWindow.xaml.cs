using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProfileDeck.Core.Audio;
using ProfileDeck.Core.Models;
using ProfileDeck.Core.Persistence;
using ProfileDeck.Core.Power;
using ProfileDeck.Core.Settings;
using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

namespace ProfileDeck.Wpf.Views;

public partial class SystemProfileEditorWindow : Window
{
    private const string NoChange = "(nicht ändern)";

    private readonly ObservableCollection<LaunchProgramAction> _programs = new();
    private readonly ObservableCollection<string> _kill = new();
    private readonly ObservableCollection<ServiceAction> _services = new();
    private readonly ObservableCollection<ProcessPriorityEntry> _priorities = new();
    private readonly Dictionary<WindowsSetting, ComboBox> _settingCombos = new();

    private readonly Dictionary<string, string> _powerPlansByLabel = new();
    private readonly Dictionary<string, string> _audioOutputsByLabel = new();
    private readonly Dictionary<string, string> _audioInputsByLabel = new();

    private Point _dragStart;
    private object? _dragItem;

    public SystemProfile? Result { get; private set; }

    public SystemProfileEditorWindow(ProfileRepository<DisplayProfile> displayRepo, SystemProfile? existing)
    {
        InitializeComponent();
        Infrastructure.DarkTitleBar.Apply(this);

        ProgramsGrid.ItemsSource = _programs;
        KillList.ItemsSource = _kill;
        ServicesGrid.ItemsSource = _services;
        PriorityGrid.ItemsSource = _priorities;

        BuildSettingsPanel();
        PopulateDisplayProfiles(displayRepo);
        PopulatePowerPlans();
        PopulateScheduling();
        PopulateAudio();

        if (existing != null) LoadFrom(existing);
        else NameBox.Text = "Neues Profil";
    }

    private void BuildSettingsPanel()
    {
        foreach (var (setting, info) in WindowsSettingMetadata.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var flags = (info.RequiresExplorerRestart ? " [Explorer-Neustart]" : "") + (info.RequiresReboot ? " [Neustart nötig]" : "") + (info.BestEffort ? " [Best-Effort]" : "");
            row.Children.Add(new TextBlock { Text = info.DisplayName + flags, Width = 340, VerticalAlignment = VerticalAlignment.Center });
            var combo = new ComboBox { Width = 160 };
            combo.Items.Add(NoChange);
            combo.Items.Add("Ein");
            combo.Items.Add("Aus");
            combo.SelectedIndex = 0;
            row.Children.Add(combo);
            _settingCombos[setting] = combo;
            SettingsPanel.Children.Add(row);
        }
    }

    private void PopulateDisplayProfiles(ProfileRepository<DisplayProfile> displayRepo)
    {
        DisplayProfileCombo.Items.Add(NoChange);
        foreach (var p in displayRepo.GetAll()) DisplayProfileCombo.Items.Add(p.Name);
        DisplayProfileCombo.SelectedIndex = 0;
    }

    private void PopulatePowerPlans()
    {
        PowerPlanCombo.Items.Add(NoChange);
        try
        {
            foreach (var (guid, name, _) in new PowerPlanService().GetAvailablePlans())
            {
                var label = $"{name}";
                _powerPlansByLabel[label] = guid;
                PowerPlanCombo.Items.Add(label);
            }
        }
        catch { /* powercfg unavailable - leave list with just NoChange */ }
        PowerPlanCombo.SelectedIndex = 0;
    }

    private void PopulateScheduling()
    {
        SchedulingCombo.Items.Add(NoChange);
        SchedulingCombo.Items.Add("Programme im Vordergrund");
        SchedulingCombo.Items.Add("Hintergrunddienste");
        SchedulingCombo.SelectedIndex = 0;
    }

    private void PopulateAudio()
    {
        AudioOutputCombo.Items.Add(NoChange);
        AudioInputCombo.Items.Add(NoChange);
        try
        {
            var audio = new AudioDeviceService();
            foreach (var d in audio.GetOutputDevices())
            {
                _audioOutputsByLabel[d.Name] = d.Id;
                AudioOutputCombo.Items.Add(d.Name);
            }
            foreach (var d in audio.GetInputDevices())
            {
                _audioInputsByLabel[d.Name] = d.Id;
                AudioInputCombo.Items.Add(d.Name);
            }
        }
        catch { /* audio subsystem unavailable */ }
        AudioOutputCombo.SelectedIndex = 0;
        AudioInputCombo.SelectedIndex = 0;
    }

    private void LoadFrom(SystemProfile p)
    {
        NameBox.Text = p.Name;
        DescriptionBox.Text = p.Description ?? "";
        HotkeyBox.Text = p.Hotkey ?? "";
        if (p.DisplayProfileName != null && DisplayProfileCombo.Items.Contains(p.DisplayProfileName))
            DisplayProfileCombo.SelectedItem = p.DisplayProfileName;

        foreach (var a in p.ProgramsToLaunch) _programs.Add(a);
        foreach (var k in p.ProcessesToKill) _kill.Add(k);
        foreach (var s in p.Services) _services.Add(s);
        foreach (var pr in p.ProcessPriorities) _priorities.Add(pr);

        foreach (var toggle in p.SettingToggles)
        {
            if (_settingCombos.TryGetValue(toggle.Setting, out var combo))
                combo.SelectedItem = toggle.Enabled ? "Ein" : "Aus";
        }

        if (p.PowerPlanGuid != null)
        {
            var label = _powerPlansByLabel.FirstOrDefault(kv => kv.Value == p.PowerPlanGuid).Key;
            if (label != null) PowerPlanCombo.SelectedItem = label;
        }

        if (p.ProcessorScheduling.HasValue)
            SchedulingCombo.SelectedIndex = p.ProcessorScheduling == ProcessorSchedulingMode.ProgramsFocused ? 1 : 2;

        if (p.DefaultAudioOutputId != null)
        {
            var label = _audioOutputsByLabel.FirstOrDefault(kv => kv.Value == p.DefaultAudioOutputId).Key;
            if (label != null) AudioOutputCombo.SelectedItem = label;
        }
        if (p.DefaultAudioInputId != null)
        {
            var label = _audioInputsByLabel.FirstOrDefault(kv => kv.Value == p.DefaultAudioInputId).Key;
            if (label != null) AudioInputCombo.SelectedItem = label;
        }
    }

    private void AddProgram_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath != null)
            _programs.Add(new LaunchProgramAction { Path = picker.SelectedPath, Priority = "Normal" });
    }

    private void RemoveProgram_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is LaunchProgramAction item) _programs.Remove(item);
    }

    private void AddKillProcess_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath != null)
            _kill.Add(System.IO.Path.GetFileNameWithoutExtension(picker.SelectedPath));
    }

    private void AddKillProcessManual_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("Prozessname (ohne .exe):");
        if (!string.IsNullOrWhiteSpace(name)) _kill.Add(name.Trim());
    }

    private void RemoveKillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (KillList.SelectedItem is string item) _kill.Remove(item);
    }

    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ServicePickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedServiceName != null)
            _services.Add(new ServiceAction { ServiceName = picker.SelectedServiceName, DesiredState = ServiceDesiredState.NoChange });
    }

    private void RemoveService_Click(object sender, RoutedEventArgs e)
    {
        if (ServicesGrid.SelectedItem is ServiceAction item) _services.Remove(item);
    }

    private void AddPriority_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath != null)
            _priorities.Add(new ProcessPriorityEntry { ProcessName = System.IO.Path.GetFileNameWithoutExtension(picker.SelectedPath), Priority = "Normal" });
    }

    private void RemovePriority_Click(object sender, RoutedEventArgs e)
    {
        if (PriorityGrid.SelectedItem is ProcessPriorityEntry item) _priorities.Remove(item);
    }

    private static string? PromptText(string label)
    {
        var dlg = new Window
        {
            Title = "ProfileDeck",
            Width = 360,
            Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox();
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", Width = 70, IsCancel = true };
        ok.Click += (_, _) => dlg.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        Infrastructure.DarkTitleBar.Apply(dlg);
        return dlg.ShowDialog() == true ? box.Text : null;
    }

    // --- Drag & drop reordering for the programs grid ---

    private void ProgramsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        _dragItem = row?.Item;
    }

    private void ProgramsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(ProgramsGrid, _dragItem, DragDropEffects.Move);
        _dragItem = null;
    }

    private void ProgramsGrid_Drop(object sender, DragEventArgs e)
    {
        if (_dragItem is not LaunchProgramAction dragged) return;
        var targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (targetRow?.Item is not LaunchProgramAction target || ReferenceEquals(dragged, target)) return;

        var oldIndex = _programs.IndexOf(dragged);
        var newIndex = _programs.IndexOf(target);
        if (oldIndex >= 0 && newIndex >= 0) _programs.Move(oldIndex, newIndex);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Bitte einen Namen angeben.", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new SystemProfile
        {
            Name = NameBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            Hotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? null : HotkeyBox.Text.Trim(),
            DisplayProfileName = DisplayProfileCombo.SelectedItem as string == NoChange ? null : DisplayProfileCombo.SelectedItem as string,
            ProgramsToLaunch = _programs.ToList(),
            ProcessesToKill = _kill.ToList(),
            Services = _services.ToList(),
            ProcessPriorities = _priorities.ToList(),
        };

        if (!string.IsNullOrWhiteSpace(HotkeyBox.Text) && !Infrastructure.HotkeyText.IsValid(HotkeyBox.Text))
        {
            MessageBox.Show("Ungültiger Hotkey. Beispiel: Ctrl+Alt+D1", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var (setting, combo) in _settingCombos)
        {
            if (combo.SelectedItem is string s && s != NoChange)
                profile.SettingToggles.Add(new SettingToggleAction { Setting = setting, Enabled = s == "Ein" });
        }

        if (PowerPlanCombo.SelectedItem is string planLabel && planLabel != NoChange && _powerPlansByLabel.TryGetValue(planLabel, out var guid))
            profile.PowerPlanGuid = guid;

        profile.ProcessorScheduling = SchedulingCombo.SelectedIndex switch
        {
            1 => ProcessorSchedulingMode.ProgramsFocused,
            2 => ProcessorSchedulingMode.BackgroundServicesFocused,
            _ => null,
        };

        if (AudioOutputCombo.SelectedItem is string outLabel && outLabel != NoChange && _audioOutputsByLabel.TryGetValue(outLabel, out var outId))
            profile.DefaultAudioOutputId = outId;
        if (AudioInputCombo.SelectedItem is string inLabel && inLabel != NoChange && _audioInputsByLabel.TryGetValue(inLabel, out var inId))
            profile.DefaultAudioInputId = inId;

        Result = profile;
        DialogResult = true;
    }
}
