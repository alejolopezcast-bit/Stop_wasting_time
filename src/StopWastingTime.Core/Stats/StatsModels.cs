namespace StopWastingTime.Core.Stats;

/// <summary>The window the statistics screen is looking at.</summary>
public enum StatsPeriod
{
    Day,
    Week,
    Month,
    Year
}

/// <summary>
/// One calendar day of activity. <paramref name="FocusedTime"/> is the time actually spent in sessions,
/// finished or abandoned, because an abandoned 20 minute session was still 20 minutes of focus.
/// </summary>
public sealed record DailySummary(
    DateOnly Date,
    int CompletedSessions,
    int AbortedSessions,
    TimeSpan FocusedTime,
    int BlockHits)
{
    public static DailySummary Empty(DateOnly date) => new(date, 0, 0, TimeSpan.Zero, 0);

    public int TotalSessions => CompletedSessions + AbortedSessions;
}

/// <summary>Headline numbers for a period, shown as the KPI cards.</summary>
public sealed record PeriodTotals(
    StatsPeriod Period,
    DateOnly From,
    DateOnly To,
    int CompletedSessions,
    int AbortedSessions,
    TimeSpan FocusedTime,
    TimeSpan AverageSession,
    int BlockHits,
    int CurrentStreak);
