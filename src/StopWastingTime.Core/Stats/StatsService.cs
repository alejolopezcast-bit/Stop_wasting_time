using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Stats;

/// <summary>
/// Turns stored sessions into the day / week / month / year numbers the statistics screen shows.
/// Everything is grouped by the local calendar date recorded when the session started.
/// </summary>
public sealed class StatsService(SessionRepository sessions, BlockHitRepository hits)
{
    /// <summary>How far back to look when counting the streak.</summary>
    private const int StreakLookbackDays = 730;

    /// <summary>
    /// One entry per day in the range, including days with no activity, so charts stay continuous.
    /// </summary>
    public async Task<IReadOnlyList<DailySummary>> GetDailySummariesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var sessionsInRange = await sessions.GetByLocalDateRangeAsync(from, to, cancellationToken).ConfigureAwait(false);
        var hitsByDate = await hits.CountByLocalDateAsync(from, to, cancellationToken).ConfigureAwait(false);

        var byDate = sessionsInRange
            .Where(session => session.Status != SessionStatus.Running)
            .GroupBy(session => session.StartedLocalDate)
            .ToDictionary(group => group.Key, group => group.ToList());

        var summaries = new List<DailySummary>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            byDate.TryGetValue(date, out var daySessions);
            hitsByDate.TryGetValue(date, out var dayHits);

            if (daySessions is null)
            {
                summaries.Add(DailySummary.Empty(date) with { BlockHits = dayHits });
                continue;
            }

            summaries.Add(new DailySummary(
                date,
                daySessions.Count(session => session.Status == SessionStatus.Completed),
                daySessions.Count(session => session.Status == SessionStatus.Aborted),
                TimeSpan.FromSeconds(daySessions.Sum(session => (long)session.ElapsedSeconds)),
                dayHits));
        }

        return summaries;
    }

    public async Task<PeriodTotals> GetTotalsAsync(
        StatsPeriod period,
        DateOnly reference,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = GetRange(period, reference);
        var summaries = await GetDailySummariesAsync(from, to, cancellationToken).ConfigureAwait(false);

        var completed = summaries.Sum(day => day.CompletedSessions);
        var aborted = summaries.Sum(day => day.AbortedSessions);
        var focused = TimeSpan.FromSeconds(summaries.Sum(day => day.FocusedTime.TotalSeconds));
        var blockHits = summaries.Sum(day => day.BlockHits);
        var totalSessions = completed + aborted;
        var average = totalSessions == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(focused.TotalSeconds / totalSessions);

        var streak = await GetCurrentStreakAsync(reference, cancellationToken).ConfigureAwait(false);

        return new PeriodTotals(period, from, to, completed, aborted, focused, average, blockHits, streak);
    }

    /// <summary>
    /// Consecutive days with at least one completed session. Today not having one yet does not break the
    /// streak: the count then ends yesterday, so the streak only dies after a full day without focus.
    /// </summary>
    public async Task<int> GetCurrentStreakAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var from = today.AddDays(-StreakLookbackDays);
        var sessionsInRange = await sessions.GetByLocalDateRangeAsync(from, today, cancellationToken).ConfigureAwait(false);

        var completedDays = sessionsInRange
            .Where(session => session.Status == SessionStatus.Completed)
            .Select(session => session.StartedLocalDate)
            .ToHashSet();

        if (completedDays.Count == 0)
        {
            return 0;
        }

        var cursor = completedDays.Contains(today) ? today : today.AddDays(-1);

        var streak = 0;
        while (completedDays.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    /// <summary>The calendar range a period covers around <paramref name="reference"/>. Weeks start on Monday.</summary>
    public static (DateOnly From, DateOnly To) GetRange(StatsPeriod period, DateOnly reference) => period switch
    {
        StatsPeriod.Day => (reference, reference),
        StatsPeriod.Week => (StartOfWeek(reference), StartOfWeek(reference).AddDays(6)),
        StatsPeriod.Month => (
            new DateOnly(reference.Year, reference.Month, 1),
            new DateOnly(reference.Year, reference.Month, DateTime.DaysInMonth(reference.Year, reference.Month))),
        StatsPeriod.Year => (new DateOnly(reference.Year, 1, 1), new DateOnly(reference.Year, 12, 31)),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown period.")
    };

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }
}
