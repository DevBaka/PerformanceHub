using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Deskrig.Core.Models;

namespace Deskrig.Desktop.Infrastructure;

/// <summary>Tray icon + context menu via Avalonia's built-in TrayIcon/NativeMenu, which works on both
/// platforms (native tray on Windows, StatusNotifierItem/AppIndicator over D-Bus on Linux - needs a
/// tray-capable desktop environment there, same as any other Linux tray app).</summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly Func<IReadOnlyList<SystemProfile>> _getProfiles;
    private readonly Action<SystemProfile> _applyProfile;
    private readonly Action _restorePrevious;
    private readonly Action _showWindow;
    private readonly Action _exit;

    public TrayIconManager(
        Func<IReadOnlyList<SystemProfile>> getProfiles,
        Action<SystemProfile> applyProfile,
        Action restorePrevious,
        Action showWindow,
        Action exit)
    {
        _getProfiles = getProfiles;
        _applyProfile = applyProfile;
        _restorePrevious = restorePrevious;
        _showWindow = showWindow;
        _exit = exit;

        _icon = new TrayIcon
        {
            Icon = CreatePlaceholderIcon(),
            ToolTipText = "Deskrig",
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => _showWindow();

        var icons = new TrayIcons { _icon };
        if (Application.Current != null) TrayIcon.SetIcons(Application.Current, icons);

        RebuildMenu();
    }

    public void RebuildMenu()
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Deskrig öffnen");
        openItem.Click += (_, _) => _showWindow();
        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());

        foreach (var profile in _getProfiles())
        {
            var p = profile;
            var item = new NativeMenuItem(p.Name);
            item.Click += (_, _) => _applyProfile(p);
            menu.Add(item);
        }

        menu.Add(new NativeMenuItemSeparator());
        var restoreItem = new NativeMenuItem("Restore Previous");
        restoreItem.Click += (_, _) => _restorePrevious();
        menu.Add(restoreItem);

        menu.Add(new NativeMenuItemSeparator());
        var exitItem = new NativeMenuItem("Beenden");
        exitItem.Click += (_, _) => _exit();
        menu.Add(exitItem);

        _icon.Menu = menu;
    }

    /// <summary>No custom app icon ships with the project (the WPF build used the generic
    /// System.Drawing.SystemIcons.Application placeholder for the same reason) - a small solid square
    /// stands in here since there's no cross-platform equivalent of "the OS's default app icon".</summary>
    private static WindowIcon CreatePlaceholderIcon()
    {
        var visual = new Border
        {
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#5B8DEF")),
            CornerRadius = new CornerRadius(6),
        };
        visual.Measure(new Size(32, 32));
        visual.Arrange(new Rect(0, 0, 32, 32));

        var rtb = new RenderTargetBitmap(new PixelSize(32, 32));
        rtb.Render(visual);
        using var stream = new MemoryStream();
        rtb.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
