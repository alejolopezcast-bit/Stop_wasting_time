using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Tests;

/// <summary>
/// Migrations are where a schema change quietly eats someone's history, so the upgrade path is tested
/// against a database that really is on the old version.
/// </summary>
public class DatabaseInitializerTests
{
    [Fact]
    public async Task A_new_database_lands_on_the_current_version_with_a_starter_blocklist()
    {
        using var database = await TestDatabase.CreateAsync();

        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, await ReadSchemaVersionAsync(database.Factory));
        Assert.NotEmpty(await database.Rules.GetAllAsync());
    }

    [Fact]
    public async Task Upgrading_from_version_one_keeps_the_rules_that_were_already_there()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swt-migration-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(path);

        try
        {
            await CreateVersionOneDatabaseAsync(factory);

            await new DatabaseInitializer(factory).InitializeAsync();

            var rules = await new BlockRuleRepository(factory).GetAllAsync();
            var kept = rules.Single(rule => rule.Value == "oldgame");

            Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, await ReadSchemaVersionAsync(factory));
            Assert.Equal("Old Game", kept.DisplayName);

            // The column added by the migration exists and starts empty.
            Assert.Null(kept.IconPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task An_apps_location_is_remembered_so_its_icon_survives_the_app_not_running()
    {
        using var database = await TestDatabase.CreateAsync();

        var rule = await database.Rules.AddAsync(new BlockRule
        {
            Kind = BlockKind.Process,
            Value = "somegame",
            DisplayName = "Some Game",
            IsEnabled = true
        });

        await database.Rules.SetIconPathAsync(rule.Id, @"C:\Games\somegame.exe");

        var stored = (await database.Rules.GetAllAsync()).Single(candidate => candidate.Value == "somegame");
        Assert.Equal(@"C:\Games\somegame.exe", stored.IconPath);
    }

    /// <summary>Builds the schema exactly as version 1 left it, with a rule already in place.</summary>
    private static async Task CreateVersionOneDatabaseAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE BlockRules (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Kind        TEXT    NOT NULL,
                Value       TEXT    NOT NULL,
                DisplayName TEXT    NOT NULL,
                IsEnabled   INTEGER NOT NULL DEFAULT 1
            );

            CREATE UNIQUE INDEX UX_BlockRules_Kind_Value ON BlockRules (Kind, Value);

            INSERT INTO BlockRules (Kind, Value, DisplayName, IsEnabled)
            VALUES ('Process', 'oldgame', 'Old Game', 1);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
