using System;
using System.Threading;
using System.Windows.Forms;

namespace DJWinOptimizer
{
    internal static class Program
    {
        private static Mutex? _singleInstanceMutex;
        [STAThread]
        static void Main()
        {
            var args = Environment.GetCommandLineArgs();
            bool elevatedRelaunch = Array.Exists(args, a => string.Equals(a, "--elevated", StringComparison.OrdinalIgnoreCase));

            bool createdNew = false;
            _singleInstanceMutex = new Mutex(true, "Global/DJWinOptimizer_SingleInstance", out createdNew);
            
            // For elevated relaunch, wait a bit longer for the old instance to close
            if (!createdNew && elevatedRelaunch)
            {
                // Old instance may still be shutting down; wait briefly and retry to avoid false 'already running'
                for (int i = 0; i < 30 && !createdNew; i++) // Increased retries for elevated relaunch
                {
                    try { _singleInstanceMutex?.Dispose(); } catch { }
                    Thread.Sleep(500);
                    _singleInstanceMutex = new Mutex(true, "Global/DJWinOptimizer_SingleInstance", out createdNew);
                }
                // If still not createdNew after retries, proceed without showing message to allow elevation handoff
                if (!createdNew)
                {
                    try { _singleInstanceMutex?.Dispose(); } catch { }
                    _singleInstanceMutex = null; // don't hold the mutex; allow elevated instance to continue
                    createdNew = true; // treat as allowed
                }
            }
            else if (!createdNew)
            {
                MessageBox.Show("DJ Win Optimizer is already running.", "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ApplicationExit += (_, __) => Core.App.Instance?.Shutdown();
            Core.App.Init();

            // Global exception handlers for failsafe logging
            Application.ThreadException += (s, e) =>
            {
                try { Core.App.Instance?.Logger.Error("UI thread exception", e.Exception); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { Core.App.Instance?.Logger.Error("Unhandled exception", e.ExceptionObject as Exception); } catch { }
            };
            Application.Run(new UI.MainForm());

            // Release mutex when app exits
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        }
    }
}
