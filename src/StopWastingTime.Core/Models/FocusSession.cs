namespace StopWastingTime.Core.Models;

/// <summary>A single attempt at concentrating for a planned amount of time.</summary>
public sealed class FocusSession
{
    public long Id { get; init; }

    public DateTimeOffset StartedUtc { get; init; }

    /// <summary>
    /// Local calendar day the session started on, stored alongside the UTC instant so statistics can
    /// group by day/week/month without doing time zone math in SQL.
    /// </summary>
    public DateOnly StartedLocalDate { get; init; }

    public DateTimeOffset? EndedUtc { get; set; }

    public int PlannedMinutes { get; init; }

    public int ElapsedSeconds { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Running;

    /// <summary>A strict session cannot be cancelled and keeps the app from being closed.</summary>
    public bool IsStrict { get; init; }

    public string? Label { get; init; }

    public TimeSpan Planned => TimeSpan.FromMinutes(PlannedMinutes);

    public TimeSpan Elapsed => TimeSpan.FromSeconds(ElapsedSeconds);
}
