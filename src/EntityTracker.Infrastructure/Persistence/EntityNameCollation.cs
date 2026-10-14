using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// The ENTITY_NAME collation: names are equal when they are equal ignoring surrounding spaces and
/// case, exactly as the app compares them (<c>EntitySourceKey</c>: trimmed, culture-invariant upper
/// case, ordinal). Columns holding names declare it, so a UNIQUE constraint on the name enforces the
/// app's rule without a derived key column. It is registered on every connection.
/// </summary>
public static class EntityNameCollation
{
    public const string Name = "ENTITY_NAME";

    public static void Register(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.CreateCollation(Name, Compare);
    }

    public static int Compare(string? x, string? y) =>
        string.CompareOrdinal(Normalize(x), Normalize(y));

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
