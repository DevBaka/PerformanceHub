using System.Windows;
using ProfileDeck.Core.Services;

namespace ProfileDeck.Wpf.Views;

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
        Infrastructure.DarkTitleBar.Apply(this);
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

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text?.Trim() ?? "";
        ServiceListView.ItemsSource = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ServiceListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Take_Click(sender, e);

    private void Take_Click(object sender, RoutedEventArgs e)
    {
        if (ServiceListView.SelectedItem is ServiceRow row)
        {
            SelectedServiceName = row.Name;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Bitte einen Dienst auswählen.", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
