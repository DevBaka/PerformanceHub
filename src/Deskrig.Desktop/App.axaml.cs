using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Deskrig.Desktop.Infrastructure;
using Deskrig.Desktop.Views;
using Deskrig.Core.Logging;
using Deskrig.Core.Persistence;

namespace Deskrig.Desktop;

public partial class App : Application
{
    public static ILogSink Log { get; } = new FileLogSink();

    // A named kernel Mutex is a Windows Terminal-Services concept ("Global/" prefix and all) - a plain
    // exclusively-locked file in our own data directory is the simpler cross-platform equivalent and works
    // identically on Windows and Linux.
    private static FileStream? _singleInstanceLock;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppPaths.EnsureCreated();

        if (!TryAcquireSingleInstanceLock())
        {
            // No window exists yet at this point in Avalonia's startup - unlike WPF there's no message loop
            // to show a blocking dialog on, so this is a console/log notice instead of a MessageBox. The
            // already-running instance's tray icon is still there either way.
            //
            // IClassicDesktopStyleApplicationLifetime.Shutdown() assumes the dispatcher's main loop is
            // already pumping (it isn't yet - we're still inside OnFrameworkInitializationCompleted, called
            // before ClassicDesktopStyleApplicationLifetime.Start() enters that loop), so calling it here
            // throws instead of exiting cleanly. Exiting the process directly sidesteps that entirely.
            Console.Error.WriteLine("Deskrig läuft bereits (siehe Tray-Icon).");
            Environment.Exit(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unbehandelte Ausnahme (Hintergrund-Thread)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unbehandelte Ausnahme (Task)", args.Exception);
            args.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // closing the main window minimizes to tray
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
        Log.Info("Deskrig gestartet.");
    }

    private static bool TryAcquireSingleInstanceLock()
    {
        try
        {
            var path = Path.Combine(AppPaths.RootDir, ".instance-lock");
            _singleInstanceLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
