using Microsoft.Extensions.Logging;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Makes blocked sites unreachable by pointing their domains at 0.0.0.0 in the Windows hosts file.
/// The browser itself is left alone: it is a work tool, only the listed sites go away.
/// </summary>
public sealed class HostsFileBlocker(IHostsFileAccess hosts, ILogger<HostsFileBlocker> logger) : IBlocker
{
    /// <summary>
    /// Never raised. A site that does not resolve produces nothing to observe, so there is no moment to
    /// report; the empty accessors satisfy <see cref="IBlocker"/> without pretending otherwise.
    /// </summary>
    public event EventHandler<BlockedEventArgs>? Blocked
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Why the last apply or release failed, for the UI to put into words. Null when all is well. It is
    /// a reason rather than a sentence so the core stays out of the business of choosing a language.
    /// </summary>
    public HostsFileProblem? LastProblem { get; private set; }

    public Task ApplyAsync(IReadOnlyList<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        var domains = rules
            .Where(rule => rule.Kind == BlockKind.Website && rule.IsEnabled)
            .Select(rule => rule.Value)
            .ToList();

        if (domains.Count == 0)
        {
            return Task.CompletedTask;
        }

        Guard(() =>
        {
            hosts.Backup();
            hosts.Write(HostsFileEditor.Apply(hosts.Read(), domains));
            hosts.FlushDns();
            logger.LogInformation("Blocked {Count} site(s) through the hosts file.", domains.Count);
        });

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        Guard(() =>
        {
            var content = hosts.Read();
            if (!HostsFileEditor.HasManagedBlock(content))
            {
                return;
            }

            hosts.Write(HostsFileEditor.Remove(content));
            hosts.FlushDns();
            logger.LogInformation("Removed the hosts file block.");
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// The hosts file is the one part that can fail for reasons outside the app: missing administrator
    /// rights, or antivirus software holding the file. It is recorded and shown, never thrown, so a
    /// focus session still runs with the apps blocked.
    /// </summary>
    private void Guard(Action action)
    {
        try
        {
            action();
            LastProblem = null;
        }
        catch (UnauthorizedAccessException exception)
        {
            LastProblem = HostsFileProblem.AccessDenied;
            logger.LogWarning(exception, "No permission to edit the hosts file.");
        }
        catch (IOException exception)
        {
            LastProblem = HostsFileProblem.WriteFailed;
            logger.LogWarning(exception, "Could not write the hosts file.");
        }
    }
}

/// <summary>The ways editing the hosts file goes wrong that are worth telling someone about.</summary>
public enum HostsFileProblem
{
    /// <summary>The app is not running as administrator.</summary>
    AccessDenied,

    /// <summary>The file could not be written, usually because antivirus software is holding it.</summary>
    WriteFailed
}
