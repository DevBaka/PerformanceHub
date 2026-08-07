using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using ProfileDeck.Core.Display;
using ProfileDeck.Core.Models;

namespace ProfileDeck.Wpf.Views;

public partial class DisplayProfileEditorWindow : Window
{
    private readonly DisplayManager _displayManager;
    private readonly ObservableCollection<DisplayProfileEntry> _rows = new();
    private bool _syncingPrimary;
    public DisplayProfile? Result { get; private set; }

    public DisplayProfileEditorWindow(DisplayManager displayManager, DisplayProfile? existing)
    {
        InitializeComponent();
        Infrastructure.DarkTitleBar.Apply(this);
        _displayManager = displayManager;
        Grid.ItemsSource = _rows;
        _rows.CollectionChanged += Rows_CollectionChanged;

        if (existing != null)
        {
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description ?? "";
            foreach (var d in existing.Displays) _rows.Add(Clone(d));
        }
        else
        {
            NameBox.Text = "Neues Profil";
            LoadCurrent_Click(this, new RoutedEventArgs());
        }
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (DisplayProfileEntry item in e.NewItems)
                item.PropertyChanged += Entry_PropertyChanged;
        if (e.OldItems != null)
            foreach (DisplayProfileEntry item in e.OldItems)
                item.PropertyChanged -= Entry_PropertyChanged;
    }

    // Only one display may be primary (Windows itself enforces this - it's the origin (0,0) of the desktop),
    // so checking "Primär" on one row clears it on every other row, radio-button style.
    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingPrimary || e.PropertyName != nameof(DisplayProfileEntry.Primary)) return;
        if (sender is not DisplayProfileEntry changed || !changed.Primary) return;

        _syncingPrimary = true;
        try
        {
            foreach (var row in _rows)
                if (!ReferenceEquals(row, changed)) row.Primary = false;
        }
        finally { _syncingPrimary = false; }
    }

    private void LoadCurrent_Click(object sender, RoutedEventArgs e)
    {
        var current = _displayManager.GetCurrentTopology();
        var existingById = _rows.ToDictionary(r => r.HardwareId, StringComparer.OrdinalIgnoreCase);
        _rows.Clear();

        var groupByKey = new Dictionary<string, int>();
        int nextGroup = 1;
        foreach (var d in current)
        {
            if (existingById.TryGetValue(d.HardwareId, out var previous))
            {
                _rows.Add(previous);
                continue;
            }

            int group = 0;
            if (d.IsActive)
            {
                if (!groupByKey.TryGetValue(d.CloneGroupKey, out group))
                {
                    var siblingCount = current.Count(x => x.IsActive && x.CloneGroupKey == d.CloneGroupKey);
                    group = siblingCount > 1 ? nextGroup++ : 0;
                    groupByKey[d.CloneGroupKey] = group;
                }
            }

            _rows.Add(new DisplayProfileEntry
            {
                HardwareId = d.HardwareId,
                FriendlyNameHint = d.FriendlyName,
                Active = d.IsActive,
                Primary = d.IsPrimary,
                PositionX = d.PositionX,
                PositionY = d.PositionY,
                Width = d.Width,
                Height = d.Height,
                RefreshRateHz = d.RefreshRateHz,
                Group = group,
                DpiScalePercent = d.IsActive && d.CurrentDpiScalePercent > 0 ? d.CurrentDpiScalePercent : null,
            });
        }
    }

    private void EditMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DisplayProfileEntry entry) return;

        var dlg = new DisplayModeDialog(_displayManager, entry) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (dlg.ResultWidth.HasValue) entry.Width = dlg.ResultWidth.Value;
        if (dlg.ResultHeight.HasValue) entry.Height = dlg.ResultHeight.Value;
        if (dlg.ResultRefreshHz.HasValue) entry.RefreshRateHz = dlg.ResultRefreshHz.Value;
        entry.DpiScalePercent = dlg.ResultDpiScalePercent;
        Grid.Items.Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Bitte einen Namen angeben.", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new DisplayProfile
        {
            Name = NameBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            Displays = _rows.ToList(),
        };
        DialogResult = true;
    }

    private static DisplayProfileEntry Clone(DisplayProfileEntry d) => new()
    {
        HardwareId = d.HardwareId,
        FriendlyNameHint = d.FriendlyNameHint,
        Active = d.Active,
        Primary = d.Primary,
        PositionX = d.PositionX,
        PositionY = d.PositionY,
        Width = d.Width,
        Height = d.Height,
        RefreshRateHz = d.RefreshRateHz,
        Group = d.Group,
        DpiScalePercent = d.DpiScalePercent,
    };
}
