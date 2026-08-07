using Avalonia.Controls;
using Avalonia.Layout;

namespace Deskrig.Desktop.Infrastructure;

/// <summary>
/// Small modal-dialog helpers replacing WPF's System.Windows.MessageBox, which Avalonia has no built-in
/// equivalent for. Built the same way the WPF code already built its one ad-hoc prompt dialog (plain
/// controls, no XAML) rather than pulling in an extra NuGet dependency for something this small.
/// </summary>
public static class Dialogs
{
    public static Task<bool> ConfirmAsync(Window owner, string message, string title = "Deskrig")
        => ShowButtonsAsync(owner, title, message, ("Ja", true), ("Nein", false));

    public static Task<bool> ConfirmOkCancelAsync(Window owner, string message, string title = "Deskrig")
        => ShowButtonsAsync(owner, title, message, ("OK", true), ("Abbrechen", false));

    public static Task InfoAsync(Window owner, string message, string title = "Deskrig")
        => ShowButtonsAsync(owner, title, message, ("OK", true));

    public static Task WarnAsync(Window owner, string message, string title = "Deskrig")
        => ShowButtonsAsync(owner, title, message, ("OK", true));

    private static async Task<bool> ShowButtonsAsync(Window owner, string title, string message, params (string Label, bool Result)[] buttons)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 16) });

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6 };
        foreach (var (label, result) in buttons)
        {
            var btn = new Button { Content = label, Width = 90 };
            btn.Click += (_, _) => dlg.Close(result);
            buttonRow.Children.Add(btn);
        }
        panel.Children.Add(buttonRow);
        dlg.Content = panel;

        return await dlg.ShowDialog<bool>(owner);
    }

    public static async Task<string?> PromptTextAsync(Window owner, string label, string title = "Deskrig")
    {
        var dlg = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Avalonia.Thickness(0, 0, 0, 6) });
        var box = new TextBox();
        panel.Children.Add(box);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 12, 0, 0), Spacing = 6 };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", Width = 70, IsCancel = true };
        ok.Click += (_, _) => dlg.Close(box.Text);
        cancel.Click += (_, _) => dlg.Close(null);
        buttonRow.Children.Add(ok);
        buttonRow.Children.Add(cancel);
        panel.Children.Add(buttonRow);
        dlg.Content = panel;

        return await dlg.ShowDialog<string?>(owner);
    }
}
