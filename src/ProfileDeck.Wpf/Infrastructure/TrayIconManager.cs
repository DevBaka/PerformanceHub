using System.Windows.Forms;
using ProfileDeck.Core.Models;
using Application = System.Windows.Application;

namespace ProfileDeck.Wpf.Infrastructure;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;
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

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "ProfileDeck",
        };
        _icon.DoubleClick += (_, _) => _showWindow();
        RebuildMenu();
    }

    public void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("ProfileDeck öffnen", null, (_, _) => _showWindow());
        menu.Items.Add(new ToolStripSeparator());

        foreach (var profile in _getProfiles())
        {
            var name = profile.Name;
            var item = menu.Items.Add(name);
            item.Click += (_, _) => _applyProfile(profile);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restore Previous", null, (_, _) => _restorePrevious());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => _exit());

        _icon.ContextMenuStrip = menu;
    }

    public void ShowBalloon(string title, string text)
        => _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
