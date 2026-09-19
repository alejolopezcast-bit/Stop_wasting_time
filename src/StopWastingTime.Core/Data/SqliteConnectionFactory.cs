using Microsoft.Data.Sqlite;

namespace StopWastingTime.Core.Data;

/// <summary>Opens connections to the app database. A single file, no server, no ORM.</summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    /// <param name="databaseFile">
    /// Path to the .db file. Defaults to <see cref="AppPaths.DatabaseFile"/>; tests pass a temp file.
    /// </param>
    public SqliteConnectionFactory(string? databaseFile = null)
    {
        DatabaseFile = databaseFile ?? AppPaths.DatabaseFile;

        var directory = Path.GetDirectoryName(DatabaseFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabaseFile { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
