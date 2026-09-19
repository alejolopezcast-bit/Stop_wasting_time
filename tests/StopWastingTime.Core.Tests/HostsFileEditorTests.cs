using StopWastingTime.Core.Blocking;

namespace StopWastingTime.Core.Tests;

/// <summary>
/// The hosts file belongs to Windows, and a bad edit breaks name resolution for everything. These tests
/// pin down that the app only ever adds and removes its own marked block.
/// </summary>
public class HostsFileEditorTests
{
    private const string ExistingHosts =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "127.0.0.1 localhost\r\n" +
        "192.168.0.10 nas.local\r\n";

    [Fact]
    public void Apply_adds_a_marked_block_for_each_domain()
    {
        var result = HostsFileEditor.Apply(ExistingHosts, ["instagram.com"]);

        Assert.Contains(HostsFileEditor.BeginMarker, result, StringComparison.Ordinal);
        Assert.Contains(HostsFileEditor.EndMarker, result, StringComparison.Ordinal);
        Assert.Contains("0.0.0.0 instagram.com", result, StringComparison.Ordinal);
        Assert.Contains("0.0.0.0 www.instagram.com", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_keeps_the_entries_that_were_already_there()
    {
        var result = HostsFileEditor.Apply(ExistingHosts, ["tiktok.com"]);

        Assert.Contains("127.0.0.1 localhost", result, StringComparison.Ordinal);
        Assert.Contains("192.168.0.10 nas.local", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_on_an_empty_file_produces_only_the_block()
    {
        var result = HostsFileEditor.Apply(string.Empty, ["reddit.com"]);

        Assert.StartsWith(HostsFileEditor.BeginMarker, result, StringComparison.Ordinal);
        Assert.EndsWith(HostsFileEditor.EndMarker + "\r\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_is_idempotent()
    {
        var once = HostsFileEditor.Apply(ExistingHosts, ["instagram.com", "tiktok.com"]);
        var twice = HostsFileEditor.Apply(once, ["instagram.com", "tiktok.com"]);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Apply_replaces_an_earlier_block_instead_of_stacking_them()
    {
        var first = HostsFileEditor.Apply(ExistingHosts, ["instagram.com"]);
        var second = HostsFileEditor.Apply(first, ["tiktok.com"]);

        Assert.DoesNotContain("instagram.com", second, StringComparison.Ordinal);
        Assert.Contains("0.0.0.0 tiktok.com", second, StringComparison.Ordinal);
        Assert.Single(Occurrences(second, HostsFileEditor.BeginMarker));
    }

    [Fact]
    public void Apply_with_no_domains_leaves_nothing_behind()
    {
        var blocked = HostsFileEditor.Apply(ExistingHosts, ["instagram.com"]);

        var result = HostsFileEditor.Apply(blocked, []);

        Assert.False(HostsFileEditor.HasManagedBlock(result));
        Assert.Equal(ExistingHosts, result);
    }

    [Fact]
    public void Remove_restores_the_file_it_started_from()
    {
        var blocked = HostsFileEditor.Apply(ExistingHosts, ["instagram.com", "x.com"]);

        var result = HostsFileEditor.Remove(blocked);

        Assert.Equal(ExistingHosts, result);
    }

    [Fact]
    public void Remove_on_a_file_the_app_never_touched_changes_nothing()
    {
        var result = HostsFileEditor.Remove(ExistingHosts);

        Assert.Equal(ExistingHosts, result);
    }

    [Fact]
    public void Remove_normalizes_unix_line_endings()
    {
        var unixStyle = "127.0.0.1 localhost\n10.0.0.1 router\n";

        var result = HostsFileEditor.Remove(unixStyle);

        Assert.Equal("127.0.0.1 localhost\r\n10.0.0.1 router\r\n", result);
    }

    [Fact]
    public void Remove_leaves_an_unterminated_block_alone_rather_than_eating_the_rest_of_the_file()
    {
        // What a crash halfway through a write would leave behind. Dropping everything after the
        // marker would take the user entries with it, so the file is returned untouched.
        var damaged = ExistingHosts + HostsFileEditor.BeginMarker + "\r\n0.0.0.0 instagram.com\r\n";

        var result = HostsFileEditor.Remove(damaged);

        Assert.Equal(damaged, result);
    }

    [Fact]
    public void Apply_does_not_add_a_second_www_prefix()
    {
        var result = HostsFileEditor.Apply(string.Empty, ["www.instagram.com"]);

        Assert.Contains("0.0.0.0 www.instagram.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("www.www.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void HasManagedBlock_only_reports_the_apps_own_block()
    {
        Assert.False(HostsFileEditor.HasManagedBlock(ExistingHosts));
        Assert.True(HostsFileEditor.HasManagedBlock(HostsFileEditor.Apply(ExistingHosts, ["x.com"])));
    }

    private static IEnumerable<int> Occurrences(string content, string value)
    {
        var index = content.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            yield return index;
            index = content.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }
    }
}
