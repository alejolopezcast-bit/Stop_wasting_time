namespace StopWastingTime.Core;

/// <summary>Where the app keeps its data on disk.</summary>
public static class AppPaths
{
    /// <summary>
    /// Environment variable that moves the whole data directory somewhere else. Useful for trying the
    /// app against a throwaway profile, and for keeping it on a portable drive.
    /// </summary>
    public const string DataDirectoryVariable = "SWT_DATA_DIR";

    /// <summary>%LOCALAPPDATA%\StopWastingTime, unless SWT_DATA_DIR says otherwise.</summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string DatabaseFile => Path.Combine(DataDirectory, "stopwastingtime.db");

    /// <summary>Copy of the hosts file as it was before the app ever touched it.</summary>
    public static string HostsBackupFile => Path.Combine(DataDirectory, "hosts.backup");

    public static string LogFile => Path.Combine(DataDirectory, "app.log");

    /// <summary>The Windows hosts file. Editing it requires administrator rights.</summary>
    public static string HostsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);

        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StopWastingTime")
            : Path.GetFullPath(overridden.Trim());
    }
}
