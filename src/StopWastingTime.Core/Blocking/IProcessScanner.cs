using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StopWastingTime.Core.Blocking;

/// <summary>A process seen running on the machine.</summary>
public readonly record struct RunningProcess(int Id, string Name);

/// <summary>
/// The window onto the operating system's process list. Behind an interface so the blocking logic can
/// be tested without starting or killing real programs.
/// </summary>
public interface IProcessScanner
{
    IReadOnlyList<RunningProcess> Snapshot();

    /// <summary>Closes the process and its children. Returns false when it could not be closed.</summary>
    bool TryKill(RunningProcess process);

    /// <summary>
    /// Where the program lives on disk, or null when it cannot be read, which is normal for processes
    /// owned by another user. Kept out of <see cref="Snapshot"/> because reading it is slow and throws
    /// often, and the once a second sweep does not need it: only the blocklist screen does, to show the
    /// real icon of each app.
    /// </summary>
    string? TryGetExecutablePath(RunningProcess process);
}

/// <summary>The real thing, on top of <see cref="Process"/>.</summary>
public sealed class SystemProcessScanner(ILogger<SystemProcessScanner> logger) : IProcessScanner
{
    public IReadOnlyList<RunningProcess> Snapshot()
    {
        var running = new List<RunningProcess>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                running.Add(new RunningProcess(process.Id, process.ProcessName));
            }
            catch (InvalidOperationException)
            {
                // The process ended between listing and reading it. Nothing to block.
            }
            finally
            {
                process.Dispose();
            }
        }

        return running;
    }

    public string? TryGetExecutablePath(RunningProcess process)
    {
        try
        {
            using var handle = Process.GetProcessById(process.Id);
            return handle.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or NotSupportedException)
        {
            // Gone, or owned by someone else. Neither is worth reporting.
            return null;
        }
    }

    public bool TryKill(RunningProcess process)
    {
        try
        {
            using var handle = Process.GetProcessById(process.Id);
            handle.Kill(entireProcessTree: true);
            return true;
        }
        catch (ArgumentException)
        {
            // Already gone.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Usually a process owned by another user or running at a higher integrity level. Without
            // administrator rights there is nothing the app can do, so it says so and carries on.
            logger.LogWarning(exception, "Could not close {Process} (pid {Pid}).", process.Name, process.Id);
            return false;
        }
    }
}
