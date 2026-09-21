using System.Globalization;
using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Data;

/// <summary>Reads and writes the blocklist.</summary>
public sealed class BlockRuleRepository(SqliteConnectionFactory factory)
{
    public async Task<IReadOnlyList<BlockRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM BlockRules ORDER BY Kind, DisplayName COLLATE NOCASE;";
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BlockRule>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM BlockRules WHERE IsEnabled = 1 ORDER BY Kind, DisplayName COLLATE NOCASE;";
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a rule, or re-enables the existing one when that kind/value is already listed.</summary>
    public async Task<BlockRule> AddAsync(BlockRule rule, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BlockRules (Kind, Value, DisplayName, IsEnabled, IconPath)
            VALUES ($kind, $value, $displayName, $isEnabled, $iconPath)
            ON CONFLICT (Kind, Value) DO UPDATE SET IsEnabled = excluded.IsEnabled;
            SELECT Id FROM BlockRules WHERE Kind = $kind AND Value = $value;
            """;
        command.Parameters.AddWithValue("$kind", rule.Kind.ToString());
        command.Parameters.AddWithValue("$value", rule.Value);
        command.Parameters.AddWithValue("$displayName", rule.DisplayName);
        command.Parameters.AddWithValue("$isEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$iconPath", (object?)rule.IconPath ?? DBNull.Value);

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        return new BlockRule
        {
            Id = id,
            Kind = rule.Kind,
            Value = rule.Value,
            DisplayName = rule.DisplayName,
            IsEnabled = rule.IsEnabled,
            IconPath = rule.IconPath
        };
    }

    public async Task SetEnabledAsync(long id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE BlockRules SET IsEnabled = $isEnabled WHERE Id = $id;";
        command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Remembers where an app was found, so its icon survives the program not running. Called when the
    /// blocklist screen happens to see it.
    /// </summary>
    public async Task SetIconPathAsync(long id, string iconPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE BlockRules SET IconPath = $iconPath WHERE Id = $id;";
        command.Parameters.AddWithValue("$iconPath", iconPath);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM BlockRules WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<BlockRule>> ReadAllAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var rules = new List<BlockRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rules.Add(new BlockRule
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                Kind = Enum.Parse<BlockKind>(reader.GetString(reader.GetOrdinal("Kind"))),
                Value = reader.GetString(reader.GetOrdinal("Value")),
                DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
                IsEnabled = reader.GetInt32(reader.GetOrdinal("IsEnabled")) != 0,
                IconPath = reader.IsDBNull(reader.GetOrdinal("IconPath"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("IconPath"))
            });
        }

        return rules;
    }
}
