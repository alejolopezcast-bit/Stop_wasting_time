using Microsoft.Extensions.Logging.Abstractions;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Tests;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData("instagram.com", "instagram.com")]
    [InlineData("www.instagram.com", "instagram.com")]
    [InlineData("https://instagram.com", "instagram.com")]
    [InlineData("https://www.Instagram.com/explore/", "instagram.com")]
    [InlineData("http://www.reddit.com:8080/r/all?sort=new", "reddit.com")]
    [InlineData("  X.COM  ", "x.com")]
    [InlineData("news.ycombinator.com", "news.ycombinator.com")]
    public void Recognises_a_domain_however_it_was_pasted(string input, string expected)
    {
        Assert.True(DomainNormalizer.TryNormalize(input, out var domain));
        Assert.Equal(expected, domain);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("not a domain")]
    [InlineData("-bad.com")]
    [InlineData("double..dot.com")]
    public void Rejects_anything_that_is_not_a_domain(string? input)
    {
        Assert.False(DomainNormalizer.TryNormalize(input, out var domain));
        Assert.Equal(string.Empty, domain);
    }
}

public class ProcessNamesTests
{
    [Theory]
    [InlineData("steam", "steam")]
    [InlineData("Steam.exe", "steam")]
    [InlineData("  STEAM.EXE ", "steam")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "steam")]
    [InlineData("\"C:/Games/steam.exe\"", "steam")]
    public void Normalize_reduces_anything_to_the_bare_executable_name(string input, string expected) =>
        Assert.Equal(expected, ProcessNames.Normalize(input));

    [Theory]
    [InlineData("Steam.exe", "steam")]
    [InlineData("steam", "Steam")]
    public void Matches_ignores_case_and_the_extension(string ruleValue, string processName) =>
        Assert.True(ProcessNames.Matches(ruleValue, processName));

    [Theory]
    [InlineData("steam", "steamwebhelper")]
    [InlineData("code", "vscode")]
    [InlineData("", "steam")]
    public void Matches_requires_the_whole_name_not_a_fragment(string ruleValue, string processName) =>
        Assert.False(ProcessNames.Matches(ruleValue, processName));

    [Theory]
    [InlineData("explorer")]
    [InlineData("csrss.exe")]
    [InlineData("LSASS")]
    [InlineData("stopwastingtime")]
    public void System_processes_are_protected(string name) => Assert.True(ProcessNames.IsProtected(name));

    [Fact]
    public void Ordinary_programs_are_not_protected() => Assert.False(ProcessNames.IsProtected("steam"));
}

public class ProcessBlockerTests
{
    private static BlockRule Rule(string value, bool enabled = true) => new()
    {
        Id = 1,
        Kind = BlockKind.Process,
        Value = value,
        DisplayName = value,
        IsEnabled = enabled
    };

    private static ProcessBlocker Create(IProcessScanner scanner, FakeClock clock) =>
        new(scanner, NullLogger<ProcessBlocker>.Instance, clock);

    [Fact]
    public async Task Closes_a_process_that_is_on_the_list()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("Steam", 100);
        scanner.Add("devenv", 200);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));
        await blocker.ApplyAsync([Rule("steam")]);

        blocker.ScanOnce();

        Assert.Single(scanner.Killed);
        Assert.Equal(100, scanner.Killed[0].Id);
    }

    [Fact]
    public async Task Leaves_processes_that_are_not_on_the_list_alone()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("devenv", 200);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));
        await blocker.ApplyAsync([Rule("steam")]);

        blocker.ScanOnce();

        Assert.Empty(scanner.Killed);
    }

    [Fact]
    public async Task Refuses_to_close_a_system_process_even_when_it_is_on_the_list()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("explorer", 4);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));
        await blocker.ApplyAsync([Rule("explorer")]);

        blocker.ScanOnce();

        Assert.Empty(scanner.Killed);
    }

    [Fact]
    public async Task Ignores_rules_that_are_switched_off()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("Steam", 100);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));
        await blocker.ApplyAsync([Rule("steam", enabled: false)]);

        blocker.ScanOnce();

        Assert.Empty(scanner.Killed);
    }

    [Fact]
    public async Task Counts_one_distraction_when_a_program_starts_several_processes()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("Steam", 100);
        scanner.Add("Steam", 101);

        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var blocker = Create(scanner, clock);

        var hits = 0;
        blocker.Blocked += (_, _) => hits++;

        await blocker.ApplyAsync([Rule("steam")]);
        blocker.ScanOnce();

        Assert.Equal(2, scanner.Killed.Count);
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task Counts_the_distraction_again_once_the_cooldown_has_passed()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("Steam", 100);

        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var blocker = Create(scanner, clock);

        var hits = 0;
        blocker.Blocked += (_, _) => hits++;

        await blocker.ApplyAsync([Rule("steam")]);
        blocker.ScanOnce();

        clock.Advance(ProcessBlocker.HitCooldown + TimeSpan.FromSeconds(1));
        scanner.Add("Steam", 102);
        blocker.ScanOnce();

        Assert.Equal(2, hits);
    }

    [Fact]
    public async Task Does_not_count_a_process_it_could_not_close()
    {
        var scanner = new FakeProcessScanner { CanKill = false };
        scanner.Add("Steam", 100);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));

        var hits = 0;
        blocker.Blocked += (_, _) => hits++;

        await blocker.ApplyAsync([Rule("steam")]);
        blocker.ScanOnce();

        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task Stops_closing_anything_once_released()
    {
        var scanner = new FakeProcessScanner();
        scanner.Add("Steam", 100);

        var blocker = Create(scanner, new FakeClock(DateTimeOffset.UtcNow));
        await blocker.ApplyAsync([Rule("steam")]);
        await blocker.ReleaseAsync();

        blocker.ScanOnce();

        Assert.Empty(scanner.Killed);
    }
}

public class HostsFileBlockerTests
{
    private static BlockRule Site(string domain, bool enabled = true) => new()
    {
        Kind = BlockKind.Website,
        Value = domain,
        DisplayName = domain,
        IsEnabled = enabled
    };

    private static HostsFileBlocker Create(IHostsFileAccess hosts) =>
        new(hosts, NullLogger<HostsFileBlocker>.Instance);

    [Fact]
    public async Task Blocks_the_sites_that_are_switched_on()
    {
        var hosts = new InMemoryHostsFileAccess("127.0.0.1 localhost\r\n");
        var blocker = Create(hosts);

        await blocker.ApplyAsync([Site("instagram.com"), Site("youtube.com", enabled: false)]);

        Assert.Contains("0.0.0.0 instagram.com", hosts.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("youtube.com", hosts.Content, StringComparison.Ordinal);
        Assert.True(hosts.BackedUp);
        Assert.Equal(1, hosts.FlushCount);
    }

    [Fact]
    public async Task Releasing_gives_the_file_back_exactly_as_it_was()
    {
        const string original = "127.0.0.1 localhost\r\n";
        var hosts = new InMemoryHostsFileAccess(original);
        var blocker = Create(hosts);

        await blocker.ApplyAsync([Site("instagram.com")]);
        await blocker.ReleaseAsync();

        Assert.Equal(original, hosts.Content);
    }

    [Fact]
    public async Task Releasing_twice_is_harmless()
    {
        var hosts = new InMemoryHostsFileAccess("127.0.0.1 localhost\r\n");
        var blocker = Create(hosts);

        await blocker.ApplyAsync([Site("instagram.com")]);
        await blocker.ReleaseAsync();
        await blocker.ReleaseAsync();

        Assert.Equal("127.0.0.1 localhost\r\n", hosts.Content);
    }

    [Fact]
    public async Task Does_not_touch_the_file_when_no_site_is_blocked()
    {
        var hosts = new InMemoryHostsFileAccess("127.0.0.1 localhost\r\n");
        var blocker = Create(hosts);

        await blocker.ApplyAsync([Site("instagram.com", enabled: false)]);

        Assert.False(hosts.BackedUp);
        Assert.Equal("127.0.0.1 localhost\r\n", hosts.Content);
    }

    [Fact]
    public async Task Reports_missing_permissions_instead_of_throwing()
    {
        var blocker = Create(new ThrowingHostsFileAccess());

        await blocker.ApplyAsync([Site("instagram.com")]);

        Assert.Equal(HostsFileProblem.AccessDenied, blocker.LastProblem);
    }

    /// <summary>What the real file does when the app is not elevated.</summary>
    private sealed class ThrowingHostsFileAccess : IHostsFileAccess
    {
        public string Read() => string.Empty;

        public void Write(string content) => throw new UnauthorizedAccessException();

        public void Backup()
        {
        }

        public void FlushDns()
        {
        }
    }
}
