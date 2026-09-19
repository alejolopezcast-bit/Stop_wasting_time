using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Blocking;

/// <summary>One way of keeping a distraction out of reach while a focus session runs.</summary>
public interface IBlocker
{
    /// <summary>Raised when this blocker actually stopped something, so it can be counted and shown.</summary>
    event EventHandler<BlockedEventArgs>? Blocked;

    /// <summary>Starts enforcing the rules that apply to this blocker. Ignores the rest.</summary>
    Task ApplyAsync(IReadOnlyList<BlockRule> rules, CancellationToken cancellationToken = default);

    /// <summary>Undoes everything <see cref="ApplyAsync"/> did. Must be safe to call twice.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>A distraction that was just blocked.</summary>
public sealed class BlockedEventArgs(long? ruleId, string ruleValue, string displayName) : EventArgs
{
    public long? RuleId { get; } = ruleId;

    public string RuleValue { get; } = ruleValue;

    public string DisplayName { get; } = displayName;
}
