using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteTrackerRepository(SqliteDatabase database) : ITrackerRepository
{
    public async Task<Tracker?> GetAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE tracker.id = $id;";
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(trackerId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task<IReadOnlyList<Tracker>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        QueryAsync(null, cancellationToken);

    public Task<IReadOnlyList<Tracker>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        return QueryAsync(projectId, cancellationToken);
    }

    private async Task<IReadOnlyList<Tracker>> QueryAsync(
        ProjectId? projectId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql +
            (projectId is null ? string.Empty : " WHERE tracker.project_id = $projectId") +
            " ORDER BY tracker.name_key, tracker.id;";
        if (projectId is not null)
        {
            command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        }

        List<Tracker> trackers = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trackers.Add(Read(reader));
        }

        return trackers;
    }

    private static Tracker Read(SqliteDataReader reader) => new(
        SqlitePersistenceValues.ParseTrackerId(reader.GetString(0)),
        SqlitePersistenceValues.ParseProjectId(reader.GetString(1)),
        reader.GetString(2),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(4), "tracker creation timestamp"),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(5), "tracker update timestamp"),
        SqlitePersistenceValues.ParseEnum<CatalogLifecycleState>(
            reader.GetString(3),
            "tracker lifecycle state"),
        reader.IsDBNull(6)
            ? null
            : SqlitePersistenceValues.ParseTimestamp(reader.GetString(6), "tracker recycle timestamp"),
        reader.IsDBNull(7) ? null : SqlitePersistenceValues.ParseTrackerId(reader.GetString(7)));

    private const string SelectSql = """
        SELECT tracker.id, tracker.project_id, tracker.name, tracker.lifecycle_state,
               tracker.created_at_utc, tracker.updated_at_utc, tracker.recycled_at_utc,
               tracker.copied_from_tracker_id
        FROM trackers tracker
        """;
}
