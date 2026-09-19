namespace StopWastingTime.Core.Models;

/// <summary>What a <see cref="BlockRule"/> targets.</summary>
public enum BlockKind
{
    /// <summary>A desktop program, matched by its executable name (for example "steam").</summary>
    Process,

    /// <summary>A website, matched by its domain (for example "instagram.com").</summary>
    Website
}

/// <summary>Lifecycle of a focus session.</summary>
public enum SessionStatus
{
    /// <summary>Started and not finished yet.</summary>
    Running,

    /// <summary>Reached the planned duration.</summary>
    Completed,

    /// <summary>Cancelled by the user, or orphaned by a crash.</summary>
    Aborted
}
