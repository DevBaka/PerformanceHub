using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Deskrig.Desktop.Infrastructure;

namespace Deskrig.Desktop.Views;

public sealed class RunningProcessRow
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

public partial class ProcessPickerWindow : Window
{
    private List<RunningProcessRow> _all = new();

    /// <summary>Selected executable path, set when the dialog closes with a true result.</summary>
    public string? SelectedPath { get; private set; }

    public ProcessPickerWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) => RefreshList();
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        var rows = new List<RunningProcessRow>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path)) continue;
                rows.Add(new RunningProcessRow { Name = p.ProcessName, Path = path });
            }
            catch { /* processes we can't access (other users, kernel threads, ...) - skip */ }
        }
        _all = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ApplyFilter();
    }

    private void FilterBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        ProcessGrid.ItemsSource = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Programm wählen",
            AllowMultiple = false,
        });
        var file = files.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } path)
        {
            SelectedPath = path;
            Close(true);
        }
    }

    private void ProcessGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => Take_Click(sender, e);

    private async void Take_Click(object? sender, RoutedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is RunningProcessRow row)
        {
            SelectedPath = row.Path;
            Close(true);
        }
        else
        {
            await Dialogs.InfoAsync(this, "Bitte einen Prozess auswählen oder eine Datei wählen.");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
