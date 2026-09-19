using Microsoft.Extensions.Logging.Abstractions;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Sessions;

namespace StopWastingTime.Core.Tests;

public class FocusSessionServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);

    private static (FocusSessionService Service, RecordingBlocker Blocker) Create(TestDatabase database, FakeClock clock)
    {
        var blocker = new RecordingBlocker();
        var coordinator = new BlockingCoordinator(
            [blocker],
            database.Hits,
            NullLogger<BlockingCoordinator>.Instance,
            clock);

        var service = new FocusSessionService(
            database.Sessions,
            database.Rules,
            coordinator,
            NullLogger<FocusSessionService>.Instance,
            clock);

        return (service, blocker);
    }

    [Fact]
    public async Task Starting_a_session_stores_it_as_running_and_turns_the_blocking_on()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);
        var (service, blocker) = Create(database, clock);

        var session = await service.StartAsync(25, isStrict: false);

        Assert.True(service.IsRunning);
        Assert.Equal(SessionStatus.Running, session.Status);
        Assert.Equal(1, blocker.ApplyCount);

        var stored = await database.Sessions.GetOrphanedRunningAsync();
        Assert.Single(stored);
    }

    [Fact]
    public async Task A_second_session_cannot_start_while_one_is_running()
    {
        using var database = await TestDatabase.CreateAsync();
        var (service, _) = Create(database, new FakeClock(Start));

        await service.StartAsync(25, isStrict: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(25, isStrict: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(24 * 60)]
    public async Task Refuses_a_duration_outside_the_allowed_range(int minutes)
    {
        using var database = await TestDatabase.CreateAsync();
        var (service, _) = Create(database, new FakeClock(Start));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(minutes, isStrict: false));
    }

    [Fact]
    public async Task The_session_completes_by_itself_when_the_time_is_up()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);
        var (service, blocker) = Create(database, clock);

        await service.StartAsync(25, isStrict: false);

        clock.Advance(TimeSpan.FromMinutes(24));
        await service.TickAsync();
        Assert.True(service.IsRunning);

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.TickAsync();

        Assert.False(service.IsRunning);
        Assert.Equal(1, blocker.ReleaseCount);

        var recent = await database.Sessions.GetRecentAsync(1);
        Assert.Equal(SessionStatus.Completed, recent[0].Status);
        Assert.Equal(25 * 60, recent[0].ElapsedSeconds);
    }

    [Fact]
    public async Task Abandoning_a_session_records_the_time_that_was_actually_spent()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);
        var (service, blocker) = Create(database, clock);

        await service.StartAsync(25, isStrict: false);
        clock.Advance(TimeSpan.FromMinutes(7));
        await service.AbortAsync();

        var recent = await database.Sessions.GetRecentAsync(1);
        Assert.Equal(SessionStatus.Aborted, recent[0].Status);
        Assert.Equal(7 * 60, recent[0].ElapsedSeconds);
        Assert.Equal(1, blocker.ReleaseCount);
    }

    [Fact]
    public async Task A_strict_session_refuses_to_be_abandoned()
    {
        using var database = await TestDatabase.CreateAsync();
        var (service, _) = Create(database, new FakeClock(Start));

        await service.StartAsync(25, isStrict: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AbortAsync());
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Elapsed_time_follows_the_clock_not_the_number_of_ticks()
    {
        // A sleeping laptop or a missed tick must not hand out extra minutes.
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);
        var (service, _) = Create(database, clock);

        await service.StartAsync(30, isStrict: false);

        clock.Advance(TimeSpan.FromMinutes(12));
        SessionProgress? progress = null;
        service.Progressed += (_, value) => progress = value;
        await service.TickAsync();

        Assert.NotNull(progress);
        Assert.Equal(TimeSpan.FromMinutes(12), progress!.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(18), progress.Remaining);
        Assert.Equal(0.4, progress.Fraction, 3);
    }

    [Fact]
    public async Task Blocked_distractions_are_counted_while_the_session_runs()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);
        var (service, blocker) = Create(database, clock);

        await service.StartAsync(25, isStrict: false);
        blocker.RaiseBlocked("steam", "Steam");
        blocker.RaiseBlocked("steam", "Steam");

        Assert.Equal(2, service.BlockedDistractions);
    }

    [Fact]
    public async Task Recovery_closes_a_session_the_crash_left_running_and_unblocks_everything()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(Start);

        // A session that was interrupted: stored as running, with nothing to end it.
        await database.Sessions.InsertAsync(new FocusSession
        {
            StartedUtc = Start,
            StartedLocalDate = DateOnly.FromDateTime(Start.UtcDateTime),
            PlannedMinutes = 25,
            Status = SessionStatus.Running
        });

        clock.Advance(TimeSpan.FromHours(3));
        var (service, blocker) = Create(database, clock);

        await service.RecoverAsync();

        var recent = await database.Sessions.GetRecentAsync(1);
        Assert.Equal(SessionStatus.Aborted, recent[0].Status);

        // Capped at the planned duration: a machine left off for three hours was not focusing.
        Assert.Equal(25 * 60, recent[0].ElapsedSeconds);
        Assert.Equal(1, blocker.ReleaseCount);
    }

    [Fact]
    public async Task Recovery_releases_the_blocking_even_when_there_is_nothing_to_clean_up()
    {
        using var database = await TestDatabase.CreateAsync();
        var (service, blocker) = Create(database, new FakeClock(Start));

        await service.RecoverAsync();

        Assert.Equal(1, blocker.ReleaseCount);
    }

    [Fact]
    public async Task An_ordinary_session_only_blocks_the_rules_that_are_switched_on()
    {
        using var database = await TestDatabase.CreateAsync();
        await database.Rules.AddAsync(NewRule("chosen", enabled: true));
        await database.Rules.AddAsync(NewRule("ignored", enabled: false));

        var (service, blocker) = Create(database, new FakeClock(Start));
        await service.StartAsync(25, isStrict: false);

        Assert.Contains(blocker.LastRules, rule => rule.Value == "chosen");
        Assert.DoesNotContain(blocker.LastRules, rule => rule.Value == "ignored");
    }

    [Fact]
    public async Task An_ultra_session_blocks_the_whole_list_including_what_is_switched_off()
    {
        // The point of ultra focus: nothing on the list is left as an escape hatch.
        using var database = await TestDatabase.CreateAsync();
        await database.Rules.AddAsync(NewRule("chosen", enabled: true));
        await database.Rules.AddAsync(NewRule("switched-off", enabled: false));

        var (service, blocker) = Create(database, new FakeClock(Start));
        await service.StartAsync(60, isStrict: true, "Ultra", blockEverything: true);

        var switchedOff = blocker.LastRules.Single(rule => rule.Value == "switched-off");
        Assert.Contains(blocker.LastRules, rule => rule.Value == "chosen");
        Assert.True(switchedOff.IsEnabled, "a rule that was off has to arrive enabled, or the blocker skips it");
    }

    [Fact]
    public async Task An_ultra_session_cannot_be_abandoned_and_keeps_blocking()
    {
        using var database = await TestDatabase.CreateAsync();
        var (service, blocker) = Create(database, new FakeClock(Start));

        await service.StartAsync(60, isStrict: true, "Ultra", blockEverything: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AbortAsync());
        Assert.True(service.IsRunning);
        Assert.Equal(0, blocker.ReleaseCount);
    }

    private static BlockRule NewRule(string value, bool enabled) => new()
    {
        Kind = BlockKind.Process,
        Value = value,
        DisplayName = value,
        IsEnabled = enabled
    };

    [Fact]
    public async Task Session_events_come_back_on_the_thread_the_service_was_built_on()
    {
        // The UI subscribes to these events and touches windows and observable collections, which only
        // tolerate their own thread. A session that ends after an await would otherwise deliver the news
        // from a thread pool thread and take the window down with it.
        using var database = await TestDatabase.CreateAsync();

        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var clock = new FakeClock(Start);
            var (service, _) = Create(database, clock);
            await service.StartAsync(25, isStrict: false);

            var postsBefore = context.Posts;

            // Ending from somewhere else, the way a timer continuation does.
            await Task.Run(async () =>
            {
                clock.Advance(TimeSpan.FromMinutes(25));
                await service.TickAsync();
            });

            Assert.True(context.Posts > postsBefore, "the session events were not handed back to the original context");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Counts how many notifications were handed back instead of being raised in place.</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int Posts { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posts++;
            callback(state);
        }
    }

    /// <summary>A blocker that records what it was asked to do and can report a blocked distraction.</summary>
    private sealed class RecordingBlocker : IBlocker
    {
        public int ApplyCount { get; private set; }

        public int ReleaseCount { get; private set; }

        /// <summary>The rules handed over the last time the blocking was applied.</summary>
        public IReadOnlyList<BlockRule> LastRules { get; private set; } = [];

        public event EventHandler<BlockedEventArgs>? Blocked;

        public Task ApplyAsync(IReadOnlyList<BlockRule> rules, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastRules = rules;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }

        public void RaiseBlocked(string value, string displayName) =>
            Blocked?.Invoke(this, new BlockedEventArgs(null, value, displayName));
    }
}
