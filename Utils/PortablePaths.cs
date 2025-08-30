using System;
using System.IO;

namespace DJWinOptimizer.Utils
{
    public static class PortablePaths
    {
        public static string AppRoot { get; private set; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        public static string ProfilesDir => Path.Combine(AppRoot, "Profiles");
        public static string LogsDir => Path.Combine(AppRoot, "Logs");
        public static string SettingsFile => Path.Combine(AppRoot, "appsettings.json");

        public static void Initialize()
        {
            Directory.CreateDirectory(ProfilesDir);
            Directory.CreateDirectory(LogsDir);
        }
    }
}
