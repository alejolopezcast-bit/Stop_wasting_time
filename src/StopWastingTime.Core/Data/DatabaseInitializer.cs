using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Data;

/// <summary>
/// Creates the schema on first run and seeds a starter blocklist. Schema changes go in
/// <see cref="MigrateAsync"/>, guarded by SQLite's own <c>user_version</c> counter.
/// </summary>
public sealed class DatabaseInitializer(SqliteConnectionFactory factory)
{
    /// <summary>Bump this and add a migration step whenever the schema changes.</summary>
    public const int CurrentSchemaVersion = 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var version = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version >= CurrentSchemaVersion)
        {
            return;
        }

        await MigrateAsync(connection, version, cancellationToken).ConfigureAwait(false);
        await SetSchemaVersionAsync(connection, CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateAsync(SqliteConnection connection, int fromVersion, CancellationToken cancellationToken)
    {
        if (fromVersion < 1)
        {
            await ExecuteAsync(connection, Schema.V1, cancellationToken).ConfigureAwait(false);
            await SeedDefaultRulesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedDefaultRulesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (kind, value, displayName, enabled) in DefaultRules)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO BlockRules (Kind, Value, DisplayName, IsEnabled)
                VALUES ($kind, $value, $displayName, $enabled);
                """;
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$displayName", displayName);
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starter blocklist. Things that are purely recreational start enabled; things that double as work
    /// tools (YouTube, Discord) are pre-loaded but disabled so the user opts in.
    /// </summary>
    private static readonly (BlockKind Kind, string Value, string DisplayName, bool Enabled)[] DefaultRules =
    [
        (BlockKind.Process, "steam", "Steam", true),
        (BlockKind.Process, "epicgameslauncher", "Epic Games Launcher", true),
        (BlockKind.Process, "riotclientux", "Riot Client", true),
        (BlockKind.Process, "discord", "Discord", false),
        (BlockKind.Website, "instagram.com", "Instagram", true),
        (BlockKind.Website, "tiktok.com", "TikTok", true),
        (BlockKind.Website, "facebook.com", "Facebook", true),
        (BlockKind.Website, "x.com", "X (Twitter)", true),
        (BlockKind.Website, "reddit.com", "Reddit", true),
        (BlockKind.Website, "youtube.com", "YouTube", false)
    ];

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection connection, int version, CancellationToken cancellationToken)
    {
        // PRAGMA does not accept parameters, and the value is an int constant we control.
        await ExecuteAsync(connection, $"PRAGMA user_version = {version};", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static class Schema
    {
        public const string V1 = """
            CREATE TABLE IF NOT EXISTS Sessions (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedUtc       TEXT    NOT NULL,
                StartedLocalDate TEXT    NOT NULL,
                EndedUtc         TEXT    NULL,
                PlannedMinutes   INTEGER NOT NULL,
                ElapsedSeconds   INTEGER NOT NULL DEFAULT 0,
                Status           TEXT    NOT NULL,
                IsStrict         INTEGER NOT NULL DEFAULT 0,
                Label            TEXT    NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Sessions_LocalDate ON Sessions (StartedLocalDate);

            CREATE TABLE IF NOT EXISTS BlockRules (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Kind        TEXT    NOT NULL,
                Value       TEXT    NOT NULL,
                DisplayName TEXT    NOT NULL,
                IsEnabled   INTEGER NOT NULL DEFAULT 1
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_BlockRules_Kind_Value ON BlockRules (Kind, Value);

            CREATE TABLE IF NOT EXISTS BlockHits (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId   INTEGER NULL REFERENCES Sessions (Id) ON DELETE SET NULL,
                RuleId      INTEGER NULL,
                RuleValue   TEXT    NOT NULL,
                DisplayName TEXT    NOT NULL,
                OccurredUtc TEXT    NOT NULL,
                LocalDate   TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_BlockHits_LocalDate ON BlockHits (LocalDate);
            """;
    }
}
