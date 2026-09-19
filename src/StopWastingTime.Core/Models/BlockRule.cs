namespace StopWastingTime.Core.Models;

/// <summary>One entry of the blocklist: an app to close or a site to make unreachable.</summary>
public sealed class BlockRule
{
    public long Id { get; init; }

    public required BlockKind Kind { get; init; }

    /// <summary>Normalized value: an executable name without ".exe", or a bare domain.</summary>
    public required string Value { get; init; }

    /// <summary>Friendly name shown in the UI.</summary>
    public required string DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;
}
