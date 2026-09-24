using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteEntityRepository : IEntityRepository
{
    private readonly SqliteDatabase _database;

    public SqliteEntityRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    internal SqliteDatabase Database => _database;

    public async Task<TrackedEntity?> GetAsync(
        TrackerId trackerId,
        EntityId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(id);

        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, tracker_id, source_name, development_status, notes, lifecycle_state, provenance,
                   requested_priority, responsible_developer, group_name
            FROM tracked_entities
            WHERE tracker_id = $trackerId AND id = $id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(id));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEntity(reader)
            : null;
    }

    public async Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, tracker_id, source_name, development_status, notes, lifecycle_state, provenance,
                   requested_priority, responsible_developer, group_name
            FROM tracked_entities
            WHERE tracker_id = $trackerId
            ORDER BY source_name COLLATE NOCASE, id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<TrackedEntity> entities = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entities.Add(ReadEntity(reader));
        }

        return entities.AsReadOnly();
    }

    internal async Task<bool> TryAddAsync(
        TrackerId trackerId,
        TrackedEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The entity belongs to another tracker.");
        }

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracked_entities
            (
                id,
                tracker_id,
                source_key,
                source_name,
                development_status,
                notes,
                lifecycle_state,
                provenance,
                requested_priority,
                responsible_developer,
                group_name,
                created_at_utc,
                schema_updated_at_utc,
                progress_updated_at_utc
            )
            VALUES
            (
                $id,
                $trackerId,
                $sourceKey,
                $sourceName,
                $developmentStatus,
                $notes,
                $lifecycleState,
                $provenance,
                $requestedPriority,
                $responsibleDeveloper,
                $groupName,
                $createdAtUtc,
                $schemaUpdatedAtUtc,
                $progressUpdatedAtUtc
            )
            ON CONFLICT DO NOTHING;
            """;
        AddEntityParameters(command, entity);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$createdAtUtc", timestamp);
        command.Parameters.AddWithValue("$schemaUpdatedAtUtc", timestamp);
        command.Parameters.AddWithValue("$progressUpdatedAtUtc", timestamp);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    internal async Task<bool> UpdateSchemaMetadataAsync(
        TrackerId trackerId,
        TrackedEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The entity belongs to another tracker.");
        }

        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tracked_entities
            SET source_key = $sourceKey,
                source_name = $sourceName,
                provenance = $provenance,
                schema_updated_at_utc = $schemaUpdatedAtUtc
            WHERE tracker_id = $trackerId AND id = $id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceKey", EntitySourceKey.From(entity.SourceName).Value);
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$provenance", entity.Provenance.ToString());
        command.Parameters.AddWithValue(
            "$schemaUpdatedAtUtc",
            SqlitePersistenceValues.FormatTimestamp(_database.TimeProvider.GetUtcNow()));

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The schema metadata conflicts with an existing tracked entity.",
                exception);
        }
    }

    private static void AddEntityParameters(
        SqliteCommand command,
        TrackedEntity entity)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceKey", EntitySourceKey.From(entity.SourceName).Value);
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$developmentStatus", entity.Status.ToString());
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$lifecycleState", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue("$provenance", entity.Provenance.ToString());
        command.Parameters.AddWithValue(
            "$requestedPriority",
            entity.RequestedPriority is null ? DBNull.Value : entity.RequestedPriority.Value);
        command.Parameters.AddWithValue(
            "$responsibleDeveloper",
            entity.ResponsibleDeveloper);
        command.Parameters.AddWithValue("$groupName", entity.GroupName);
    }

    private static TrackedEntity ReadEntity(SqliteDataReader reader)
    {
        EntityId id = SqlitePersistenceValues.ParseEntityId(reader.GetString(0));
        TrackerId trackerId = SqlitePersistenceValues.ParseTrackerId(reader.GetString(1));
        string sourceName = reader.GetString(2);
        DevelopmentStatus status = SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(
            reader.GetString(3),
            "development status");
        string notes = reader.GetString(4);
        EntityLifecycleState lifecycleState =
            SqlitePersistenceValues.ParseEnum<EntityLifecycleState>(
                reader.GetString(5),
                "entity lifecycle state");
        EntityProvenance provenance = SqlitePersistenceValues.ParseEnum<EntityProvenance>(
            reader.GetString(6),
            "entity provenance");
        int? requestedPriority = reader.IsDBNull(7) ? null : reader.GetInt32(7);
        string responsibleDeveloper = reader.GetString(8);
        string groupName = reader.GetString(9);

        return new TrackedEntity(
            id,
            trackerId,
            sourceName,
            status,
            notes,
            lifecycleState,
            provenance,
            requestedPriority,
            responsibleDeveloper,
            groupName);
    }
}
