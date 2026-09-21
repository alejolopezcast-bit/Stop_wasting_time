using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Reading and writing the Windows hosts file, behind an interface so the blocker can be tested without
/// touching a real system file.
/// </summary>
public interface IHostsFileAccess
{
    string Read();

    void Write(string content);

    /// <summary>Keeps a copy of the file as it was before the app ever changed it.</summary>
    void Backup();

    /// <summary>Drops the resolver cache so the change takes effect right away.</summary>
    void FlushDns();
}

/// <summary>The real hosts file at %WINDIR%\System32\drivers\etc\hosts. Needs administrator rights.</summary>
public sealed class SystemHostsFileAccess(ILogger<SystemHostsFileAccess> logger) : IHostsFileAccess
{
    public string Read() => File.Exists(AppPaths.HostsFile) ? File.ReadAllText(AppPaths.HostsFile) : string.Empty;

    public void Write(string content) => File.WriteAllText(AppPaths.HostsFile, content);

    public void Backup()
    {
        if (File.Exists(AppPaths.HostsBackupFile) || !File.Exists(AppPaths.HostsFile))
        {
            return;
        }

        AppPaths.EnsureDataDirectory();
        File.Copy(AppPaths.HostsFile, AppPaths.HostsBackupFile);
        logger.LogInformation("Backed up the hosts file to {Path}.", AppPaths.HostsBackupFile);
    }

    public void FlushDns()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            // The hosts file is already written; a stale cache only delays the block a little.
            logger.LogWarning(exception, "Could not flush the DNS cache.");
        }
    }
}
