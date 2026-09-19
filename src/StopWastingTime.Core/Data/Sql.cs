using System.Globalization;
using Microsoft.Data.Sqlite;

namespace StopWastingTime.Core.Data;

/// <summary>
/// Conversions between CLR values and the text SQLite stores. Dates go in as invariant ISO-8601 so they
/// sort correctly as strings and never depend on the machine's culture.
/// </summary>
internal static class Sql
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string ToText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static object ToTextOrNull(DateTimeOffset? value) =>
        value is null ? DBNull.Value : ToText(value.Value);

    public static string ToText(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset ToDateTimeOffset(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? ToDateTimeOffsetOrNull(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : ToDateTimeOffset(reader.GetString(ordinal));
    }

    public static DateOnly ToDateOnly(string value) =>
        DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
}
