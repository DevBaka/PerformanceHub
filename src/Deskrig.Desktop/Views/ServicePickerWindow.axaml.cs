using Avalonia.Controls;
using Avalonia.Interactivity;
using Deskrig.Core.Services;
using Deskrig.Desktop.Infrastructure;

namespace Deskrig.Desktop.Views;

public sealed class ServiceRow
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public string? StartupType { get; init; }
}

public partial class ServicePickerWindow : Window
{
    private List<ServiceRow> _all = new();
    public string? SelectedServiceName { get; private set; }

    public ServicePickerWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            var svc = new ServiceControlService();
            _all = svc.ListAll()
                .Select(s => new ServiceRow { Name = s.Name, DisplayName = s.DisplayName, Status = s.Status.ToString(), StartupType = s.StartupType })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();
        };
    }

    private void FilterBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        ServiceGrid.ItemsSource = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ServiceGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => Take_Click(sender, e);

    private async void Take_Click(object? sender, RoutedEventArgs e)
    {
        if (ServiceGrid.SelectedItem is ServiceRow row)
        {
            SelectedServiceName = row.Name;
            Close(true);
        }
        else
        {
            await Dialogs.InfoAsync(this, "Bitte einen Dienst auswählen.");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
