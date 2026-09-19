using System.Globalization;
using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Data;

/// <summary>Stores every blocked distraction attempt.</summary>
public sealed class BlockHitRepository(SqliteConnectionFactory factory)
{
    public async Task AddAsync(BlockHit hit, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BlockHits (SessionId, RuleId, RuleValue, DisplayName, OccurredUtc, LocalDate)
            VALUES ($sessionId, $ruleId, $ruleValue, $displayName, $occurredUtc, $localDate);
            """;
        command.Parameters.AddWithValue("$sessionId", (object?)hit.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$ruleId", (object?)hit.RuleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$ruleValue", hit.RuleValue);
        command.Parameters.AddWithValue("$displayName", hit.DisplayName);
        command.Parameters.AddWithValue("$occurredUtc", Sql.ToText(hit.OccurredUtc));
        command.Parameters.AddWithValue("$localDate", Sql.ToText(hit.LocalDate));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountForSessionAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM BlockHits WHERE SessionId = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Blocked attempts per local day, for the statistics charts.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, int>> CountByLocalDateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LocalDate, COUNT(*) AS Hits
              FROM BlockHits
             WHERE LocalDate BETWEEN $from AND $to
             GROUP BY LocalDate;
            """;
        command.Parameters.AddWithValue("$from", Sql.ToText(from));
        command.Parameters.AddWithValue("$to", Sql.ToText(to));

        var counts = new Dictionary<DateOnly, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[Sql.ToDateOnly(reader.GetString(0))] = reader.GetInt32(1);
        }

        return counts;
    }

    /// <summary>The distractions you were tempted by most often, most frequent first.</summary>
    public async Task<IReadOnlyList<TopDistraction>> GetTopAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DisplayName, COUNT(*) AS Hits
              FROM BlockHits
             WHERE LocalDate BETWEEN $from AND $to
             GROUP BY DisplayName
             ORDER BY Hits DESC
             LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", Sql.ToText(from));
        command.Parameters.AddWithValue("$to", Sql.ToText(to));
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<TopDistraction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TopDistraction(reader.GetString(0), reader.GetInt32(1)));
        }

        return results;
    }
}

/// <summary>How many times a single app or site was blocked over a period.</summary>
public sealed record TopDistraction(string DisplayName, int Hits);
