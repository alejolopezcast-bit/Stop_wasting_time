using System.Globalization;

namespace StopWastingTime.Core.Settings;

/// <summary>The languages the interface is translated into, and how the one to show is picked.</summary>
public static class AppLanguages
{
    public const string Spanish = "es";

    public const string English = "en";

    /// <summary>What someone gets when neither their choice nor Windows names a language we have.</summary>
    public const string Fallback = English;

    public static IReadOnlyList<string> Supported { get; } = [Spanish, English];

    /// <summary>
    /// A language chosen in the app wins. Without one, the language Windows is displayed in, when there
    /// is a translation for it. Otherwise English, as the language most people can get by in.
    /// </summary>
    public static string Resolve(string? saved, CultureInfo system)
    {
        if (TryNormalize(saved, out var chosen))
        {
            return chosen;
        }

        return TryNormalize(system.TwoLetterISOLanguageName, out var fromWindows) ? fromWindows : Fallback;
    }

    /// <summary>Accepts "EN", "en" or "en-GB" alike, so a hand edited settings file still works.</summary>
    public static bool TryNormalize(string? code, out string language)
    {
        language = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var primary = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        var match = Supported.FirstOrDefault(supported => supported == primary);

        if (match is null)
        {
            return false;
        }

        language = match;
        return true;
    }
}
