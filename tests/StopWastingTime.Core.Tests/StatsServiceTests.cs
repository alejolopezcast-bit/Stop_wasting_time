using StopWastingTime.Core.Models;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.Core.Tests;

/// <summary>
/// The statistics are the reason the app is worth using, so they run against a real SQLite database
/// rather than a stub: the grouping is SQL, and SQL is what has to be right.
/// </summary>
public class StatsServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static async Task<FocusSession> AddSessionAsync(
        TestDatabase database,
        DateOnly date,
        SessionStatus status,
        int minutes = 25,
        int? elapsedSeconds = null)
    {
        var started = date.ToDateTime(new TimeOnly(10, 0));

        return await database.Sessions.InsertAsync(new FocusSession
        {
            StartedUtc = new DateTimeOffset(started, TimeSpan.Zero),
            StartedLocalDate = date,
            EndedUtc = new DateTimeOffset(started.AddMinutes(minutes), TimeSpan.Zero),
            PlannedMinutes = minutes,
            ElapsedSeconds = elapsedSeconds ?? minutes * 60,
            Status = status
        });
    }

    private static StatsService CreateService(TestDatabase database) => new(database.Sessions, database.Hits);

    [Fact]
    public async Task Counts_completed_and_abandoned_sessions_separately()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Completed);
        await AddSessionAsync(database, Today, SessionStatus.Completed);
        await AddSessionAsync(database, Today, SessionStatus.Aborted, elapsedSeconds: 300);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(2, totals.CompletedSessions);
        Assert.Equal(1, totals.AbortedSessions);
    }

    [Fact]
    public async Task Focused_time_includes_the_minutes_of_an_abandoned_session()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Completed, minutes: 25);
        await AddSessionAsync(database, Today, SessionStatus.Aborted, minutes: 25, elapsedSeconds: 600);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(TimeSpan.FromMinutes(35), totals.FocusedTime);
    }

    [Fact]
    public async Task A_running_session_is_not_counted_yet()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Running, elapsedSeconds: 0);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(0, totals.CompletedSessions);
        Assert.Equal(0, totals.AbortedSessions);
    }

    [Fact]
    public async Task The_week_covers_monday_to_sunday()
    {
        // 2026-09-19 is a Saturday, so its week runs from Monday the 14th to Sunday the 20th.
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, new DateOnly(2026, 9, 14), SessionStatus.Completed);
        await AddSessionAsync(database, new DateOnly(2026, 9, 20), SessionStatus.Completed);
        await AddSessionAsync(database, new DateOnly(2026, 9, 13), SessionStatus.Completed);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Week, Today);

        Assert.Equal(new DateOnly(2026, 9, 14), totals.From);
        Assert.Equal(new DateOnly(2026, 9, 20), totals.To);
        Assert.Equal(2, totals.CompletedSessions);
    }

    [Fact]
    public async Task The_month_covers_the_whole_calendar_month()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, new DateOnly(2026, 9, 1), SessionStatus.Completed);
        await AddSessionAsync(database, new DateOnly(2026, 9, 30), SessionStatus.Completed);
        await AddSessionAsync(database, new DateOnly(2026, 8, 31), SessionStatus.Completed);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Month, Today);

        Assert.Equal(new DateOnly(2026, 9, 1), totals.From);
        Assert.Equal(new DateOnly(2026, 9, 30), totals.To);
        Assert.Equal(2, totals.CompletedSessions);
    }

    [Fact]
    public async Task Days_with_no_sessions_still_appear_so_charts_stay_continuous()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Completed);

        var summaries = await CreateService(database)
            .GetDailySummariesAsync(Today.AddDays(-6), Today);

        Assert.Equal(7, summaries.Count);
        Assert.All(summaries.Take(6), day => Assert.Equal(0, day.TotalSessions));
        Assert.Equal(1, summaries[^1].CompletedSessions);
    }

    [Fact]
    public async Task Average_session_is_the_focused_time_over_the_sessions_that_ended()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Completed, minutes: 30);
        await AddSessionAsync(database, Today, SessionStatus.Completed, minutes: 10);

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(TimeSpan.FromMinutes(20), totals.AverageSession);
    }

    [Fact]
    public async Task Average_session_is_zero_when_nothing_was_done()
    {
        using var database = await TestDatabase.CreateAsync();

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(TimeSpan.Zero, totals.AverageSession);
    }

    [Fact]
    public async Task Blocked_distractions_are_counted_per_day()
    {
        using var database = await TestDatabase.CreateAsync();
        var session = await AddSessionAsync(database, Today, SessionStatus.Completed);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await database.Hits.AddAsync(new BlockHit
            {
                SessionId = session.Id,
                RuleValue = "steam",
                DisplayName = "Steam",
                OccurredUtc = DateTimeOffset.UtcNow,
                LocalDate = Today
            });
        }

        var totals = await CreateService(database).GetTotalsAsync(StatsPeriod.Day, Today);

        Assert.Equal(3, totals.BlockHits);
    }

    [Fact]
    public async Task Streak_counts_consecutive_days_that_have_a_completed_session()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Completed);
        await AddSessionAsync(database, Today.AddDays(-1), SessionStatus.Completed);
        await AddSessionAsync(database, Today.AddDays(-2), SessionStatus.Completed);
        await AddSessionAsync(database, Today.AddDays(-4), SessionStatus.Completed);

        var streak = await CreateService(database).GetCurrentStreakAsync(Today);

        Assert.Equal(3, streak);
    }

    [Fact]
    public async Task Todays_missing_session_does_not_break_the_streak_yet()
    {
        // The streak only dies after a full day without focus, so a morning with nothing done yet
        // still shows yesterday's run.
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today.AddDays(-1), SessionStatus.Completed);
        await AddSessionAsync(database, Today.AddDays(-2), SessionStatus.Completed);

        var streak = await CreateService(database).GetCurrentStreakAsync(Today);

        Assert.Equal(2, streak);
    }

    [Fact]
    public async Task An_abandoned_session_does_not_keep_the_streak_alive()
    {
        using var database = await TestDatabase.CreateAsync();
        await AddSessionAsync(database, Today, SessionStatus.Aborted);
        await AddSessionAsync(database, Today.AddDays(-1), SessionStatus.Aborted);

        var streak = await CreateService(database).GetCurrentStreakAsync(Today);

        Assert.Equal(0, streak);
    }

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), StatsService.StartOfWeek(new DateOnly(2026, 9, 14)));
        Assert.Equal(new DateOnly(2026, 9, 14), StatsService.StartOfWeek(new DateOnly(2026, 9, 20)));
    }
}
