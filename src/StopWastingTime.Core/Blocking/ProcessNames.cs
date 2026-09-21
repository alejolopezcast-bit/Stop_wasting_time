namespace StopWastingTime.Core.Blocking;

/// <summary>Matching rules for executable names, and the list of processes that are never touched.</summary>
public static class ProcessNames
{
    /// <summary>
    /// Processes the app refuses to close even if they end up on the blocklist. Killing any of these
    /// leaves Windows unusable, and a blocker that can brick the desktop is worse than a distraction.
    /// </summary>
    public static IReadOnlySet<string> Protected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "idle",
        "registry",
        "memory compression",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "lsaiso",
        "svchost",
        "dwm",
        "explorer",
        "fontdrvhost",
        "sihost",
        "ctfmon",
        "taskhostw",
        "runtimebroker",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "searchhost",
        "textinputhost",
        "audiodg",
        "conhost",
        "winlogonui",
        // The app itself, however it happens to be launched.
        "stopwastingtime",
        "dotnet"
    };

    /// <summary>
    /// Reduces anything the user might type ("C:\Program Files\Steam\Steam.exe", "Steam.exe", " steam ")
    /// to the bare lowercase executable name used for matching.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var name = value.Trim().Trim('"');

        // Take the file name if a full path was given. Handles both separators.
        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0 && separator < name.Length - 1)
        {
            name = name[(separator + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Trim().ToLowerInvariant();
    }

    public static bool IsProtected(string value) => Protected.Contains(Normalize(value));

    /// <summary>True when a running process is the one a rule refers to. Names must match exactly.</summary>
    public static bool Matches(string ruleValue, string processName) =>
        !string.IsNullOrEmpty(ruleValue) &&
        string.Equals(Normalize(ruleValue), Normalize(processName), StringComparison.Ordinal);
}
