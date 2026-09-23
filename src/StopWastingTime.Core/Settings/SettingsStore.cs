using System.Text.Json;

namespace StopWastingTime.Core.Settings;

/// <summary>
/// Keeps <see cref="AppSettings"/> in a small JSON file next to the database. It is a file and not a
/// table because the language is needed before the database is open: the very first thing the app can
/// say is that it is already running.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    /// <summary>
    /// The saved settings, or the defaults when there are none. A missing, unreadable or mangled file is
    /// never a reason for the app not to start.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes to a temporary file first and moves it into place, so a crash halfway through leaves the
    /// old settings instead of half of the new ones.
    /// </summary>
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
