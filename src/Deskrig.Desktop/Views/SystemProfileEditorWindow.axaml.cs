using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Deskrig.Core.Audio;
using Deskrig.Core.Models;
using Deskrig.Core.Persistence;
using Deskrig.Core.Power;
using Deskrig.Core.Settings;
using Deskrig.Desktop.Infrastructure;

namespace Deskrig.Desktop.Views;

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

    private object? _dragItem;
    private Point _dragStart;

    public SystemProfile? Result { get; private set; }

    public SystemProfileEditorWindow(ProfileRepository<DisplayProfile> displayRepo, SystemProfile? existing)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        ProgramsGrid.ItemsSource = _programs;
        KillList.ItemsSource = _kill;
        ServicesGrid.ItemsSource = _services;
        PriorityGrid.ItemsSource = _priorities;
        ServicesHeader.Text = OperatingSystem.IsLinux() ? "Dienste (systemd)" : "Windows-Dienste";
        PowerSectionHeader.Text = OperatingSystem.IsLinux() ? "Power-Plan" : "Power-Plan & Prozessor-Scheduling";
        SchedulingRow.IsVisible = !OperatingSystem.IsLinux();

        SetupProgramsDragDrop();
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
        var applicable = WindowsSettingMetadata.All.Where(kv => !OperatingSystem.IsLinux() || kv.Value.SupportedOnLinux).ToList();
        SettingsSection.IsVisible = applicable.Count > 0;

        foreach (var (setting, info) in applicable)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var flags = (info.RequiresExplorerRestart ? " [Neustart der Shell]" : "") + (info.RequiresReboot ? " [Neustart nötig]" : "") + (info.BestEffort ? " [Best-Effort]" : "");
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
        catch { /* backend unavailable - leave list with just NoChange */ }
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

    private async void AddProgram_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow();
        var ok = await picker.ShowDialog<bool>(this);
        if (ok && picker.SelectedPath != null)
            _programs.Add(new LaunchProgramAction { Path = picker.SelectedPath, Priority = "Normal" });
    }

    private void RemoveProgram_Click(object? sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is LaunchProgramAction item) _programs.Remove(item);
    }

    private async void AddKillProcess_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow();
        var ok = await picker.ShowDialog<bool>(this);
        if (ok && picker.SelectedPath != null)
            _kill.Add(System.IO.Path.GetFileNameWithoutExtension(picker.SelectedPath));
    }

    private async void AddKillProcessManual_Click(object? sender, RoutedEventArgs e)
    {
        var name = await Dialogs.PromptTextAsync(this, "Prozessname:");
        if (!string.IsNullOrWhiteSpace(name)) _kill.Add(name.Trim());
    }

    private void RemoveKillProcess_Click(object? sender, RoutedEventArgs e)
    {
        if (KillList.SelectedItem is string item) _kill.Remove(item);
    }

    private async void AddService_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new ServicePickerWindow();
        var ok = await picker.ShowDialog<bool>(this);
        if (ok && picker.SelectedServiceName != null)
            _services.Add(new ServiceAction { ServiceName = picker.SelectedServiceName, DesiredState = ServiceDesiredState.NoChange });
    }

    private void RemoveService_Click(object? sender, RoutedEventArgs e)
    {
        if (ServicesGrid.SelectedItem is ServiceAction item) _services.Remove(item);
    }

    private async void AddPriority_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow();
        var ok = await picker.ShowDialog<bool>(this);
        if (ok && picker.SelectedPath != null)
            _priorities.Add(new ProcessPriorityEntry { ProcessName = System.IO.Path.GetFileNameWithoutExtension(picker.SelectedPath), Priority = "Normal" });
    }

    private void RemovePriority_Click(object? sender, RoutedEventArgs e)
    {
        if (PriorityGrid.SelectedItem is ProcessPriorityEntry item) _priorities.Remove(item);
    }

    // --- Drag & drop reordering for the programs grid ---

    private void SetupProgramsDragDrop()
    {
        ProgramsGrid.AddHandler(PointerPressedEvent, ProgramsGrid_PointerPressed, RoutingStrategies.Tunnel);
        ProgramsGrid.AddHandler(PointerMovedEvent, ProgramsGrid_PointerMoved, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(ProgramsGrid, true);
        ProgramsGrid.AddHandler(DragDrop.DropEvent, ProgramsGrid_Drop);
        ProgramsGrid.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Move);
    }

    private void ProgramsGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>();
        _dragItem = row?.DataContext;
    }

    private async void ProgramsGrid_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed != true || _dragItem == null) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4) return;

        var data = new DataObject();
        data.Set("program", _dragItem);
        var dragged = _dragItem;
        _dragItem = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void ProgramsGrid_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("program") is not LaunchProgramAction dragged) return;
        var targetRow = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>();
        if (targetRow?.DataContext is not LaunchProgramAction target || ReferenceEquals(dragged, target)) return;

        var oldIndex = _programs.IndexOf(dragged);
        var newIndex = _programs.IndexOf(target);
        if (oldIndex >= 0 && newIndex >= 0) _programs.Move(oldIndex, newIndex);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            await Dialogs.WarnAsync(this, "Bitte einen Namen angeben.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(HotkeyBox.Text) && !HotkeyText.IsValid(HotkeyBox.Text))
        {
            await Dialogs.WarnAsync(this, "Ungültiger Hotkey. Beispiel: Ctrl+Alt+D1");
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
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
