using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteProjectRepository(SqliteDatabase database) : IProjectRepository
{
    public async Task<Project?> GetAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, lifecycle_state, created_at_utc, updated_at_utc, recycled_at_utc
            FROM projects
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(projectId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Project>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, lifecycle_state, created_at_utc, updated_at_utc, recycled_at_utc
            FROM projects
            ORDER BY name_key, id;
            """;
        List<Project> projects = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(Read(reader));
        }

        return projects;
    }

    public async Task<bool> IsNameReservedAsync(
        string name,
        ProjectId? excludingProjectId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeName(name);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM projects
                WHERE name_key = $nameKey
                  AND ($excludedId IS NULL OR id <> $excludedId));
            """;
        command.Parameters.AddWithValue("$nameKey", normalized.ToUpperInvariant());
        command.Parameters.AddWithValue(
            "$excludedId",
            excludingProjectId is null
                ? DBNull.Value
                : SqlitePersistenceValues.Format(excludingProjectId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Trim();
        return normalized.Length == 0
            ? throw new ArgumentException("A project name is required.", nameof(name))
            : normalized;
    }

    private static Project Read(SqliteDataReader reader) => new(
        SqlitePersistenceValues.ParseProjectId(reader.GetString(0)),
        reader.GetString(1),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(3), "project creation timestamp"),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(4), "project update timestamp"),
        SqlitePersistenceValues.ParseEnum<CatalogLifecycleState>(
            reader.GetString(2),
            "project lifecycle state"),
        reader.IsDBNull(5)
            ? null
            : SqlitePersistenceValues.ParseTimestamp(reader.GetString(5), "project recycle timestamp"));
}
