using Microsoft.Extensions.Logging;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Sessions;

/// <summary>
/// Runs one focus session at a time: starts the blocking, keeps the countdown, and closes the session
/// whether it finishes, is abandoned, or is interrupted by a crash.
/// </summary>
public sealed class FocusSessionService
{
    /// <summary>Shortest session the UI will accept.</summary>
    public const int MinimumMinutes = 1;

    /// <summary>Longest session the UI will accept.</summary>
    public const int MaximumMinutes = 8 * 60;

    private readonly SessionRepository _sessions;
    private readonly BlockRuleRepository _rules;
    private readonly BlockingCoordinator _blocking;
    private readonly TimeProvider _clock;
    private readonly ILogger<FocusSessionService> _logger;

    /// <summary>
    /// The context the service was built on, which in the app is the UI thread. A session can end on a
    /// background thread, once an await inside the service resumes there, and subscribers update
    /// windows and observable collections that only tolerate being touched from their own thread.
    /// Events are handed back to this context so every subscriber can stay simple. Outside a UI, for
    /// instance in tests, there is no context and events are raised directly.
    /// </summary>
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public FocusSessionService(
        SessionRepository sessions,
        BlockRuleRepository rules,
        BlockingCoordinator blocking,
        ILogger<FocusSessionService> logger,
        TimeProvider? timeProvider = null)
    {
        _sessions = sessions;
        _rules = rules;
        _blocking = blocking;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;

        _blocking.Blocked += (_, _) =>
        {
            if (Current is not null)
            {
                BlockedDistractions++;
            }
        };
    }

    public FocusSession? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary>Distractions blocked since the running session started.</summary>
    public int BlockedDistractions { get; private set; }

    public event EventHandler<FocusSession>? SessionStarted;

    public event EventHandler<SessionProgress>? Progressed;

    public event EventHandler<FocusSession>? SessionEnded;

    public async Task<FocusSession> StartAsync(
        int plannedMinutes,
        bool isStrict,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A focus session is already running.");
        }

        if (plannedMinutes is < MinimumMinutes or > MaximumMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedMinutes),
                plannedMinutes,
                $"A session lasts between {MinimumMinutes} and {MaximumMinutes} minutes.");
        }

        var now = _clock.GetLocalNow();

        // Stored as Running before anything else: if the machine dies mid session the attempt is not
        // lost, and the next launch knows it has cleaning up to do.
        var session = await _sessions.InsertAsync(
            new FocusSession
            {
                StartedUtc = now.ToUniversalTime(),
                StartedLocalDate = DateOnly.FromDateTime(now.DateTime),
                PlannedMinutes = plannedMinutes,
                Status = SessionStatus.Running,
                IsStrict = isStrict,
                Label = label
            },
            cancellationToken).ConfigureAwait(false);

        Current = session;
        BlockedDistractions = 0;

        var rules = await _rules.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        await _blocking.ApplyAsync(rules, session.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Session {Id} started: {Minutes} minutes, strict: {Strict}.",
            session.Id,
            plannedMinutes,
            isStrict);

        Raise(() => SessionStarted?.Invoke(this, session));
        Raise(() => Progressed?.Invoke(this, BuildProgress(session)));

        return session;
    }

    /// <summary>
    /// Advances the countdown and finishes the session once the planned time is up. Elapsed time comes
    /// from the clock rather than a tick count, so a missed tick or a sleeping machine cannot buy extra
    /// minutes or lose them.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (Current is not { } session)
        {
            return;
        }

        var progress = BuildProgress(session);
        Raise(() => Progressed?.Invoke(this, progress));

        if (progress.Remaining <= TimeSpan.Zero)
        {
            await CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The session reached its planned time.</summary>
    public Task<FocusSession?> CompleteAsync(CancellationToken cancellationToken = default) =>
        EndAsync(SessionStatus.Completed, cancellationToken);

    /// <summary>The user gave up on the session. Refused while the session is strict.</summary>
    public Task<FocusSession?> AbortAsync(CancellationToken cancellationToken = default)
    {
        if (Current is { IsStrict: true })
        {
            throw new InvalidOperationException("A strict session cannot be cancelled.");
        }

        return EndAsync(SessionStatus.Aborted, cancellationToken);
    }

    /// <summary>
    /// Cleans up after a crash or a forced shutdown: sessions still marked Running are closed as
    /// abandoned, and the blocking is released no matter what, so a killed app never leaves sites
    /// blocked forever.
    /// </summary>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var orphans = await _sessions.GetOrphanedRunningAsync(cancellationToken).ConfigureAwait(false);

        foreach (var orphan in orphans)
        {
            var elapsed = _clock.GetUtcNow() - orphan.StartedUtc;
            var capped = elapsed < TimeSpan.Zero
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Min(elapsed.TotalSeconds, orphan.Planned.TotalSeconds));

            orphan.Status = SessionStatus.Aborted;
            orphan.EndedUtc = orphan.StartedUtc + capped;
            orphan.ElapsedSeconds = (int)capped.TotalSeconds;

            await _sessions.UpdateAsync(orphan, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Session {Id} was left running by a crash and was closed as abandoned.", orphan.Id);
        }

        await _blocking.ReleaseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<FocusSession?> EndAsync(SessionStatus status, CancellationToken cancellationToken)
    {
        if (Current is not { } session)
        {
            return null;
        }

        Current = null;

        var elapsed = _clock.GetUtcNow() - session.StartedUtc;
        var capped = TimeSpan.FromSeconds(Math.Clamp(elapsed.TotalSeconds, 0, session.Planned.TotalSeconds));

        session.Status = status;
        session.EndedUtc = _clock.GetUtcNow();
        session.ElapsedSeconds = (int)capped.TotalSeconds;

        try
        {
            await _sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Whatever happened to the database, the machine has to be usable again.
            await _blocking.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Session {Id} ended as {Status} after {Seconds}s with {Blocked} blocked distraction(s).",
            session.Id,
            status,
            session.ElapsedSeconds,
            BlockedDistractions);

        Raise(() => SessionEnded?.Invoke(this, session));

        return session;
    }

    /// <summary>Runs a notification on the thread the service belongs to.</summary>
    private void Raise(Action notification)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            notification();
            return;
        }

        _context.Post(_ => notification(), null);
    }

    private SessionProgress BuildProgress(FocusSession session)
    {
        var elapsed = _clock.GetUtcNow() - session.StartedUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var remaining = session.Planned - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return new SessionProgress(elapsed, remaining, session.Planned, BlockedDistractions);
    }
}
