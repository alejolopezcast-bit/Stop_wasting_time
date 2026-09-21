namespace StopWastingTime.Core.Sessions;

/// <summary>A snapshot of the running session, pushed to the UI once a second.</summary>
public sealed record SessionProgress(
    TimeSpan Elapsed,
    TimeSpan Remaining,
    TimeSpan Planned,
    int BlockedDistractions)
{
    /// <summary>How much of the session is done, from 0 to 1, for the progress ring.</summary>
    public double Fraction => Planned <= TimeSpan.Zero
        ? 0
        : Math.Clamp(Elapsed.TotalSeconds / Planned.TotalSeconds, 0, 1);

    /// <summary>The countdown, as mm:ss or h:mm:ss for long sessions.</summary>
    public string RemainingText => Remaining.TotalHours >= 1
        ? $"{(int)Remaining.TotalHours}:{Remaining.Minutes:00}:{Remaining.Seconds:00}"
        : $"{Remaining.Minutes + (Remaining.Hours * 60):00}:{Remaining.Seconds:00}";
}
