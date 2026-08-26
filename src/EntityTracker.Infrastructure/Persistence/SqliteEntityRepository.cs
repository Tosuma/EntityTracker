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

    public async Task<TrackedEntity?> GetAsync(
        EntityId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_name, development_status, notes, lifecycle_state, provenance,
                   requested_priority, responsible_developer, group_name
            FROM tracked_entities
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(id));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEntity(reader)
            : null;
    }

    public async Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_name, development_status, notes, lifecycle_state, provenance,
                   requested_priority, responsible_developer, group_name
            FROM tracked_entities
            ORDER BY source_name COLLATE NOCASE, id;
            """;

        List<TrackedEntity> entities = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entities.Add(ReadEntity(reader));
        }

        return entities.AsReadOnly();
    }

    internal async Task<bool> TryAddAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracked_entities
            (
                id,
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
        command.Parameters.AddWithValue("$createdAtUtc", timestamp);
        command.Parameters.AddWithValue("$schemaUpdatedAtUtc", timestamp);
        command.Parameters.AddWithValue("$progressUpdatedAtUtc", timestamp);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    internal async Task<bool> UpdateSchemaMetadataAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tracked_entities
            SET source_key = $sourceKey,
                source_name = $sourceName,
                provenance = $provenance,
                schema_updated_at_utc = $schemaUpdatedAtUtc
            WHERE id = $id;
            """;
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
        string sourceName = reader.GetString(1);
        DevelopmentStatus status = SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(
            reader.GetString(2),
            "development status");
        string notes = reader.GetString(3);
        EntityLifecycleState lifecycleState =
            SqlitePersistenceValues.ParseEnum<EntityLifecycleState>(
                reader.GetString(4),
                "entity lifecycle state");
        EntityProvenance provenance = SqlitePersistenceValues.ParseEnum<EntityProvenance>(
            reader.GetString(5),
            "entity provenance");
        int? requestedPriority = reader.IsDBNull(6) ? null : reader.GetInt32(6);
        string responsibleDeveloper = reader.GetString(7);
        string groupName = reader.GetString(8);

        return new TrackedEntity(
            id,
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
