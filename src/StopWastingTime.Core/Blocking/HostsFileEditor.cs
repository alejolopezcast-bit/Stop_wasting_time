using System.Text;

namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Builds the contents of the Windows hosts file. Pure string work, no file access: this is the piece
/// that can corrupt a system file, so it is kept separate and covered by tests.
/// </summary>
public static class HostsFileEditor
{
    public const string BeginMarker = "# >>> Stop Wasting Time >>>";
    public const string EndMarker = "# <<< Stop Wasting Time <<<";

    private const string BlackholeAddress = "0.0.0.0";
    private const string NewLine = "\r\n";

    /// <summary>
    /// Returns <paramref name="content"/> with a managed block that points every domain at 0.0.0.0.
    /// Any previous block is replaced, so applying twice changes nothing and lines the user added
    /// themselves are left alone.
    /// </summary>
    public static string Apply(string content, IEnumerable<string> domains)
    {
        var cleaned = Remove(content);

        var entries = domains
            .Select(domain => domain?.Trim().ToLowerInvariant())
            .Where(domain => !string.IsNullOrEmpty(domain))
            .Select(domain => domain!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            return cleaned;
        }

        var builder = new StringBuilder(cleaned);

        if (builder.Length > 0 && !cleaned.EndsWith(NewLine, StringComparison.Ordinal))
        {
            builder.Append(NewLine);
        }

        builder.Append(BeginMarker).Append(NewLine);
        builder.Append("# Added while a focus session is running. Removed automatically when it ends.").Append(NewLine);

        foreach (var domain in entries)
        {
            builder.Append(BlackholeAddress).Append(' ').Append(domain).Append(NewLine);

            // Without the www alias the site is still reachable at www.<domain>.
            if (!domain.StartsWith("www.", StringComparison.Ordinal))
            {
                builder.Append(BlackholeAddress).Append(" www.").Append(domain).Append(NewLine);
            }
        }

        builder.Append(EndMarker).Append(NewLine);

        return builder.ToString();
    }

    /// <summary>Returns <paramref name="content"/> without the managed block, leaving the rest as it was.</summary>
    public static string Remove(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        var insideBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Equals(BeginMarker, StringComparison.Ordinal))
            {
                insideBlock = true;
                continue;
            }

            if (trimmed.Equals(EndMarker, StringComparison.Ordinal))
            {
                insideBlock = false;
                continue;
            }

            if (!insideBlock)
            {
                kept.Add(line);
            }
        }

        // An unterminated block (a crash mid-write) would otherwise swallow the rest of the file.
        if (insideBlock)
        {
            return content;
        }

        var result = string.Join(NewLine, kept).TrimEnd('\r', '\n');

        return result.Length == 0 ? string.Empty : result + NewLine;
    }

    public static bool HasManagedBlock(string content) =>
        content.Contains(BeginMarker, StringComparison.Ordinal);
}
