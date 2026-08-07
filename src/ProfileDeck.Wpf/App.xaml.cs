using System.Windows;
using ProfileDeck.Core.Logging;
using ProfileDeck.Core.Persistence;

namespace ProfileDeck.Wpf;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    public static ILogSink Log { get; } = new FileLogSink();

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "Global/ProfileDeck_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("ProfileDeck läuft bereits (siehe Tray-Icon).", "ProfileDeck", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unbehandelte Ausnahme (UI-Thread)", args.Exception);
            MessageBox.Show(args.Exception.Message, "ProfileDeck - Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Exceptions off the UI thread (background tasks, thread pool callbacks) can't be marked "handled" -
        // the process terminates either way, but logging first at least leaves a trace of what happened.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unbehandelte Ausnahme (Hintergrund-Thread)", args.ExceptionObject as Exception);
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* already released */ }
        base.OnExit(e);
    }
}
