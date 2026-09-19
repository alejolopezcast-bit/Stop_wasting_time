namespace StopWastingTime.Core;

/// <summary>Where the app keeps its data on disk.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\StopWastingTime</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StopWastingTime");

    public static string DatabaseFile => Path.Combine(DataDirectory, "stopwastingtime.db");

    /// <summary>Copy of the hosts file as it was before the app ever touched it.</summary>
    public static string HostsBackupFile => Path.Combine(DataDirectory, "hosts.backup");

    public static string LogFile => Path.Combine(DataDirectory, "app.log");

    /// <summary>The Windows hosts file. Editing it requires administrator rights.</summary>
    public static string HostsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
