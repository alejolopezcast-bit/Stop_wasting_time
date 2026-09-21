namespace StopWastingTime.Core.Blocking;

/// <summary>
/// Turns whatever the user pastes into the bare domain the hosts file needs: "https://www.Instagram.com/explore"
/// and "instagram.com" both become "instagram.com".
/// </summary>
public static class DomainNormalizer
{
    public static bool TryNormalize(string? input, out string domain)
    {
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim().ToLowerInvariant();

        // Scheme, then credentials, then path, query and fragment.
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        var credentials = value.IndexOf('@');
        if (credentials >= 0)
        {
            value = value[(credentials + 1)..];
        }

        var pathStart = value.IndexOfAny(['/', '?', '#']);
        if (pathStart >= 0)
        {
            value = value[..pathStart];
        }

        // Port.
        var port = value.IndexOf(':');
        if (port >= 0)
        {
            value = value[..port];
        }

        // The hosts entry covers the www alias on its own, so it is stripped here.
        if (value.StartsWith("www.", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        value = value.Trim('.').Trim();

        if (!IsPlausibleDomain(value))
        {
            return false;
        }

        domain = value;
        return true;
    }

    /// <summary>A name with at least one dot and nothing but the characters a host name may contain.</summary>
    private static bool IsPlausibleDomain(string value)
    {
        if (value.Length is < 3 or > 253 || !value.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (value.StartsWith('-') || value.EndsWith('-') || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
    }
}
