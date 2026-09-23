namespace StopWastingTime.Core.Settings;

/// <summary>Preferences that belong to the person rather than to their focus history.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// The interface language as a two letter code, or null when it was never chosen, in which case the
    /// language of Windows decides.
    /// </summary>
    public string? Language { get; init; }
}
