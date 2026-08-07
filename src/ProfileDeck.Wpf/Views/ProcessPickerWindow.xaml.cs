using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ProfileDeck.Wpf.Views;

public sealed class RunningProcessRow
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public BitmapSource? Icon { get; init; }
}

public partial class ProcessPickerWindow : Window
{
    private List<RunningProcessRow> _all = new();

    /// <summary>Selected executable path, set when the dialog closes with DialogResult == true.</summary>
    public string? SelectedPath { get; private set; }

    public ProcessPickerWindow()
    {
        InitializeComponent();
        Infrastructure.DarkTitleBar.Apply(this);
        Loaded += (_, _) => Refresh_Click(this, new RoutedEventArgs());
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var rows = new List<RunningProcessRow>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path)) continue;
                rows.Add(new RunningProcessRow { Name = p.ProcessName, Path = path, Icon = TryGetIcon(path) });
            }
            catch { /* system processes we can't access - skip */ }
        }
        _all = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ApplyFilter();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        ProcessListView.ItemsSource = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programme (*.exe)|*.exe|Alle Dateien (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true)
        {
            SelectedPath = dlg.FileName;
            DialogResult = true;
        }
    }

    private void ProcessListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Take_Click(sender, e);

    private void Take_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessListView.SelectedItem is RunningProcessRow row)
        {
            SelectedPath = row.Path;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Bitte einen Prozess auswählen oder eine Datei wählen.", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static BitmapSource? TryGetIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { return null; }
    }
}
