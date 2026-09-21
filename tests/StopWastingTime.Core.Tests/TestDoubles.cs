using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;

namespace StopWastingTime.Core.Tests;

/// <summary>A clock the tests move by hand, so nothing has to wait for real time to pass.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan amount) => _now += amount;
}

/// <summary>A process list the test controls, standing in for the machine.</summary>
public sealed class FakeProcessScanner : IProcessScanner
{
    private readonly List<RunningProcess> _running = [];

    public List<RunningProcess> Killed { get; } = [];

    /// <summary>Set to false to act like a process the app has no permission to close.</summary>
    public bool CanKill { get; set; } = true;

    public void Add(string name, int id) => _running.Add(new RunningProcess(id, name));

    public IReadOnlyList<RunningProcess> Snapshot() => _running.ToList();

    /// <summary>Tests never look at icons, so there is nothing to hand back.</summary>
    public string? TryGetExecutablePath(RunningProcess process) => null;

    public bool TryKill(RunningProcess process)
    {
        if (!CanKill)
        {
            return false;
        }

        Killed.Add(process);
        _running.RemoveAll(candidate => candidate.Id == process.Id);
        return true;
    }
}

/// <summary>A hosts file that lives in memory, so tests never touch the real one.</summary>
public sealed class InMemoryHostsFileAccess(string initialContent = "") : IHostsFileAccess
{
    public string Content { get; private set; } = initialContent;

    public bool BackedUp { get; private set; }

    public int FlushCount { get; private set; }

    public string Read() => Content;

    public void Write(string content) => Content = content;

    public void Backup() => BackedUp = true;

    public void FlushDns() => FlushCount++;
}

/// <summary>A real SQLite database in a temporary file, thrown away when the test ends.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _path;

    private TestDatabase(string path, SqliteConnectionFactory factory)
    {
        _path = path;
        Factory = factory;
        Sessions = new SessionRepository(factory);
        Rules = new BlockRuleRepository(factory);
        Hits = new BlockHitRepository(factory);
    }

    public SqliteConnectionFactory Factory { get; }

    public SessionRepository Sessions { get; }

    public BlockRuleRepository Rules { get; }

    public BlockHitRepository Hits { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swt-tests-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(path);
        await new DatabaseInitializer(factory).InitializeAsync();

        return new TestDatabase(path, factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // A temp file left behind is not worth failing a test over.
        }
    }
}
