using Microsoft.Extensions.Logging;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Closes blocked programs while a session runs. It polls rather than hooking process creation: a one
/// second sweep is cheap, needs no driver, and catches a program a moment after it opens, which is soon
/// enough to make the point.
/// </summary>
public sealed class ProcessBlocker(
    IProcessScanner scanner,
    ILogger<ProcessBlocker> logger,
    TimeProvider? timeProvider = null) : IBlocker, IAsyncDisposable
{
    /// <summary>How often the process list is swept.</summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// One launch of a program often starts several processes (Steam alone starts a handful). Hits for
    /// the same rule inside this window are counted once, so the distraction count stays honest.
    /// </summary>
    public static readonly TimeSpan HitCooldown = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, DateTimeOffset> _lastHit = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();

    private IReadOnlyList<BlockRule> _rules = [];
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public event EventHandler<BlockedEventArgs>? Blocked;

    public bool IsRunning => _loop is not null;

    public Task ApplyAsync(IReadOnlyList<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        var applicable = rules
            .Where(rule => rule.Kind == BlockKind.Process && rule.IsEnabled)
            .Where(rule => !ProcessNames.IsProtected(rule.Value))
            .ToList();

        lock (_stateLock)
        {
            _rules = applicable;
            _lastHit.Clear();
        }

        logger.LogInformation("Watching {Count} blocked app(s).", applicable.Count);

        if (_loop is not null)
        {
            return Task.CompletedTask;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_cancellation.Token);

        return Task.CompletedTask;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _rules = [];
        }

        if (_cancellation is null)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: that is how the loop stops.
            }
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;

        logger.LogInformation("Stopped watching blocked apps.");
    }

    /// <summary>
    /// One sweep of the process list. Public so tests can drive the blocker a step at a time instead of
    /// waiting on a timer.
    /// </summary>
    public int ScanOnce()
    {
        IReadOnlyList<BlockRule> rules;
        lock (_stateLock)
        {
            rules = _rules;
        }

        if (rules.Count == 0)
        {
            return 0;
        }

        var closed = 0;

        foreach (var process in scanner.Snapshot())
        {
            if (ProcessNames.IsProtected(process.Name))
            {
                continue;
            }

            var rule = rules.FirstOrDefault(candidate => ProcessNames.Matches(candidate.Value, process.Name));
            if (rule is null)
            {
                continue;
            }

            if (!scanner.TryKill(process))
            {
                continue;
            }

            closed++;
            logger.LogInformation("Closed {Process} (pid {Pid}).", process.Name, process.Id);

            if (ShouldCount(rule.Value))
            {
                Blocked?.Invoke(this, new BlockedEventArgs(rule.Id, rule.Value, rule.DisplayName));
            }
        }

        return closed;
    }

    private bool ShouldCount(string ruleValue)
    {
        var now = _clock.GetUtcNow();

        lock (_stateLock)
        {
            if (_lastHit.TryGetValue(ruleValue, out var previous) && now - previous < HitCooldown)
            {
                return false;
            }

            _lastHit[ruleValue] = now;
            return true;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ScanInterval, _clock);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    ScanOnce();
                }
                catch (Exception exception)
                {
                    // A bad sweep must not end the watch: the session is still running.
                    logger.LogError(exception, "A process sweep failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync() => await ReleaseAsync().ConfigureAwait(false);
}
