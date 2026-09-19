using Microsoft.Extensions.Logging;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Applies and releases every blocker as one unit, and records each blocked distraction so the
/// statistics can show what tempted you and how often.
/// </summary>
public sealed class BlockingCoordinator
{
    private readonly IReadOnlyList<IBlocker> _blockers;
    private readonly BlockHitRepository _hits;
    private readonly TimeProvider _clock;
    private readonly ILogger<BlockingCoordinator> _logger;

    private long? _sessionId;

    public BlockingCoordinator(
        IEnumerable<IBlocker> blockers,
        BlockHitRepository hits,
        ILogger<BlockingCoordinator> logger,
        TimeProvider? timeProvider = null)
    {
        _blockers = blockers.ToList();
        _hits = hits;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;

        foreach (var blocker in _blockers)
        {
            blocker.Blocked += OnBlocked;
        }
    }

    /// <summary>Raised for the UI: something was blocked right now.</summary>
    public event EventHandler<BlockedEventArgs>? Blocked;

    public bool IsActive { get; private set; }

    public async Task ApplyAsync(IReadOnlyList<BlockRule> rules, long? sessionId, CancellationToken cancellationToken = default)
    {
        _sessionId = sessionId;

        foreach (var blocker in _blockers)
        {
            try
            {
                await blocker.ApplyAsync(rules, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // One blocker failing must not cost the session the other one.
                _logger.LogError(exception, "{Blocker} could not be applied.", blocker.GetType().Name);
            }
        }

        IsActive = true;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        foreach (var blocker in _blockers)
        {
            try
            {
                await blocker.ReleaseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "{Blocker} could not be released.", blocker.GetType().Name);
            }
        }

        _sessionId = null;
        IsActive = false;
    }

    private void OnBlocked(object? sender, BlockedEventArgs e)
    {
        Blocked?.Invoke(this, e);

        var now = _clock.GetLocalNow();
        var hit = new BlockHit
        {
            SessionId = _sessionId,
            RuleId = e.RuleId,
            RuleValue = e.RuleValue,
            DisplayName = e.DisplayName,
            OccurredUtc = now.ToUniversalTime(),
            LocalDate = DateOnly.FromDateTime(now.DateTime)
        };

        // Recording a hit must never slow down or break the sweep that raised it.
        _ = Task.Run(async () =>
        {
            try
            {
                await _hits.AddAsync(hit).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not record a blocked distraction.");
            }
        });
    }
}
