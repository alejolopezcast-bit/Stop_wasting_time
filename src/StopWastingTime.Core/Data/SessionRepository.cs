using System.Globalization;
using Microsoft.Data.Sqlite;
using StopWastingTime.Core.Models;

namespace StopWastingTime.Core.Data;

/// <summary>Reads and writes focus sessions.</summary>
public sealed class SessionRepository(SqliteConnectionFactory factory)
{
    public async Task<FocusSession> InsertAsync(FocusSession session, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sessions (StartedUtc, StartedLocalDate, EndedUtc, PlannedMinutes, ElapsedSeconds, Status, IsStrict, Label)
            VALUES ($startedUtc, $startedLocalDate, $endedUtc, $plannedMinutes, $elapsedSeconds, $status, $isStrict, $label);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$startedUtc", Sql.ToText(session.StartedUtc));
        command.Parameters.AddWithValue("$startedLocalDate", Sql.ToText(session.StartedLocalDate));
        command.Parameters.AddWithValue("$endedUtc", Sql.ToTextOrNull(session.EndedUtc));
        command.Parameters.AddWithValue("$plannedMinutes", session.PlannedMinutes);
        command.Parameters.AddWithValue("$elapsedSeconds", session.ElapsedSeconds);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$isStrict", session.IsStrict ? 1 : 0);
        command.Parameters.AddWithValue("$label", (object?)session.Label ?? DBNull.Value);

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        return new FocusSession
        {
            Id = id,
            StartedUtc = session.StartedUtc,
            StartedLocalDate = session.StartedLocalDate,
            EndedUtc = session.EndedUtc,
            PlannedMinutes = session.PlannedMinutes,
            ElapsedSeconds = session.ElapsedSeconds,
            Status = session.Status,
            IsStrict = session.IsStrict,
            Label = session.Label
        };
    }

    public async Task UpdateAsync(FocusSession session, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Sessions
               SET EndedUtc = $endedUtc,
                   ElapsedSeconds = $elapsedSeconds,
                   Status = $status
             WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$endedUtc", Sql.ToTextOrNull(session.EndedUtc));
        command.Parameters.AddWithValue("$elapsedSeconds", session.ElapsedSeconds);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$id", session.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sessions left in <see cref="SessionStatus.Running"/> by a crash or a forced shutdown.</summary>
    public async Task<IReadOnlyList<FocusSession>> GetOrphanedRunningAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Sessions WHERE Status = 'Running' ORDER BY Id;";
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FocusSession>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Sessions ORDER BY StartedUtc DESC LIMIT $count;";
        command.Parameters.AddWithValue("$count", count);
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FocusSession>> GetByLocalDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM Sessions
             WHERE StartedLocalDate BETWEEN $from AND $to
             ORDER BY StartedUtc;
            """;
        command.Parameters.AddWithValue("$from", Sql.ToText(from));
        command.Parameters.AddWithValue("$to", Sql.ToText(to));
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<FocusSession>> ReadAllAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var sessions = new List<FocusSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(Map(reader));
        }

        return sessions;
    }

    private static FocusSession Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        StartedUtc = Sql.ToDateTimeOffset(reader.GetString(reader.GetOrdinal("StartedUtc"))),
        StartedLocalDate = Sql.ToDateOnly(reader.GetString(reader.GetOrdinal("StartedLocalDate"))),
        EndedUtc = Sql.ToDateTimeOffsetOrNull(reader, "EndedUtc"),
        PlannedMinutes = reader.GetInt32(reader.GetOrdinal("PlannedMinutes")),
        ElapsedSeconds = reader.GetInt32(reader.GetOrdinal("ElapsedSeconds")),
        Status = Enum.Parse<SessionStatus>(reader.GetString(reader.GetOrdinal("Status"))),
        IsStrict = reader.GetInt32(reader.GetOrdinal("IsStrict")) != 0,
        Label = reader.IsDBNull(reader.GetOrdinal("Label")) ? null : reader.GetString(reader.GetOrdinal("Label"))
    };
}
