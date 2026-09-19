namespace StopWastingTime.Core.Models;

/// <summary>Records one blocked distraction attempt, so the app can show how often you were tempted.</summary>
public sealed class BlockHit
{
    public long Id { get; init; }

    public long? SessionId { get; init; }

    public long? RuleId { get; init; }

    /// <summary>Copy of the rule value, kept so the hit survives the rule being deleted.</summary>
    public required string RuleValue { get; init; }

    public required string DisplayName { get; init; }

    public DateTimeOffset OccurredUtc { get; init; }

    public DateOnly LocalDate { get; init; }
}
