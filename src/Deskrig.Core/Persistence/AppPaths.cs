namespace Deskrig.Core.Persistence;

public static class AppPaths
{
    public static string RootDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deskrig");

    public static string ProfilesDir => Path.Combine(RootDir, "profiles");
    public static string DisplayProfilesDir => Path.Combine(ProfilesDir, "display");
    public static string SystemProfilesDir => Path.Combine(ProfilesDir, "system");
    public static string LogsDir => Path.Combine(RootDir, "logs");
    public static string SnapshotFile => Path.Combine(RootDir, "last-snapshot.json");
    public static string SettingsFile => Path.Combine(RootDir, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DisplayProfilesDir);
        Directory.CreateDirectory(SystemProfilesDir);
        Directory.CreateDirectory(LogsDir);
    }
}
