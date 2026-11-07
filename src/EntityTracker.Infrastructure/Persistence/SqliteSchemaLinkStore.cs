using System.Security.Cryptography;
using System.Text;

using EntityTracker.Application.Persistence;
using EntityTracker.Application.Schemas;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteSchemaLinkStore(SqliteDatabase database) : ISchemaLinkStore
{
    public async Task<TrackerSchemaLink?> GetLinkAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        TrackerSchemaLink? link = (await ReadAsync(connection, transaction,
            "tracker_id = $id", SqlitePersistenceValues.Format(trackerId), cancellationToken)).SingleOrDefault();
        await transaction.CommitAsync(cancellationToken);
        return link;
    }

    public async Task<IReadOnlyList<TrackerSchemaLink>> GetLinksAsync(
        SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        IReadOnlyList<TrackerSchemaLink> links = await ReadAsync(connection, transaction,
            "schema_id = $id", SqlitePersistenceValues.Format(schemaId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return links;
    }

    public async Task<string> FingerprintSchemaAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        string fingerprint = await FingerprintSchemaAsync(connection, transaction, schemaId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fingerprint;
    }

    public async Task<string> FingerprintTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        string fingerprint = await SqliteTrackedStateStore.FingerprintAsync(connection, transaction, trackerId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fingerprint;
    }

    public async Task DetachAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        // The baseline goes with the link (ON DELETE CASCADE).
        command.CommandText = "DELETE FROM tracker_schemas WHERE tracker_id = $id;";
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(trackerId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyPushAsync(SchemaPushCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        DateTimeOffset appliedAtUtc = database.TimeProvider.GetUtcNow();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (await FingerprintSchemaAsync(connection, transaction, commit.SchemaId, cancellationToken) !=
            commit.ExpectedSchemaFingerprint)
            throw new InvalidOperationException("The schema changed after this review opened. Open a new review.");
        foreach (TrackerPushCommit tracker in commit.Trackers)
            if (await SqliteTrackedStateStore.FingerprintAsync(connection, transaction, tracker.TrackerId,
                    cancellationToken) != tracker.ExpectedFingerprint)
                throw new InvalidOperationException("A tracker changed after this review opened. Open a new review.");
        try
        {
            foreach (TrackerPushCommit tracker in commit.Trackers)
            {
                if (tracker.Link.SchemaId != commit.SchemaId || tracker.Link.TrackerId != tracker.TrackerId)
                    throw new InvalidOperationException("A pushed link belongs to another schema or tracker.");
                await SqliteTrackedStateStore.ApplyChangeSetAsync(connection, transaction, tracker.TrackerId,
                    tracker.ChangeSet, appliedAtUtc, cancellationToken);
                await WriteAsync(connection, transaction, tracker.Link, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The push could not be applied because a tracker's result would be invalid.", exception);
        }
    }

    public async Task ApplyConversionAsync(SchemaConversionCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        string timestamp = SqlitePersistenceValues.FormatTimestamp(database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // The source must still be an active Tracker; recycling it is the last check that nothing moved.
            using (SqliteCommand recycle = Command(connection, transaction, """
                       UPDATE trackers SET recycled_at_utc = $timestamp, updated_at_utc = $timestamp
                       WHERE id = $id AND recycled_at_utc IS NULL
                         AND NOT EXISTS (SELECT 1 FROM tracker_schemas WHERE tracker_id = $id);
                       """))
            {
                recycle.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(commit.SourceTrackerId));
                recycle.Parameters.AddWithValue("$timestamp", timestamp);
                if (await recycle.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("The tracker changed or was recycled. Open Convert to schema again.");
            }

            foreach (SchemaContent content in commit.Schemas)
            {
                Schema schema = content.Schema;
                using (SqliteCommand insert = Command(connection, transaction, """
                           INSERT INTO schemas (id, project_id, name, created_at_utc, updated_at_utc, recycled_at_utc)
                           SELECT $id, $projectId, $name, $created, $updated, NULL
                           FROM projects WHERE id = $projectId AND recycled_at_utc IS NULL;
                           """))
                {
                    insert.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schema.Id));
                    insert.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(schema.ProjectId));
                    insert.Parameters.AddWithValue("$name", schema.Name);
                    insert.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(schema.CreatedAtUtc));
                    insert.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(schema.UpdatedAtUtc));
                    if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException("A project the schema belongs in is no longer active.");
                }
                foreach (SchemaEntity entity in content.Entities)
                    await ExecuteAsync(connection, transaction,
                        "INSERT INTO schema_entities (id, schema_id, name, group_name) VALUES ($id, $schema, $name, $group);",
                        cancellationToken, ("$id", SqlitePersistenceValues.Format(entity.Id)),
                        ("$schema", SqlitePersistenceValues.Format(schema.Id)), ("$name", entity.Name),
                        ("$group", entity.GroupName));
                foreach (SchemaDependency dependency in content.Dependencies)
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO schema_entity_dependencies (dependent_entity_id, dependency_entity_id)
                        VALUES ($dependent, $dependency);
                        """, cancellationToken,
                        ("$dependent", SqlitePersistenceValues.Format(dependency.DependentEntityId)),
                        ("$dependency", SqlitePersistenceValues.Format(dependency.DependencyEntityId)));
            }

            foreach (TrackerSchemaLink link in commit.Links)
            {
                string trackerId = SqlitePersistenceValues.Format(link.TrackerId);
                // The copy now follows the schema: its link to the source and its Sync from source baseline go.
                await ExecuteAsync(connection, transaction,
                    "UPDATE trackers SET copied_from_tracker_id = NULL WHERE id = $tracker;",
                    cancellationToken, ("$tracker", trackerId));
                await SqliteSyncBaselineRows.DeleteAsync(connection, transaction, trackerId, cancellationToken);
                await WriteAsync(connection, transaction, link, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The tracker could not be converted, because a schema name is taken or a copy changed. Try again.",
                exception);
        }
    }

    /// <summary>Replaces the Tracker's link and baseline with <paramref name="link"/>.</summary>
    internal static async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction,
        TrackerSchemaLink link, CancellationToken cancellationToken)
    {
        string trackerId = SqlitePersistenceValues.Format(link.TrackerId);
        await ExecuteAsync(connection, transaction, "DELETE FROM tracker_schemas WHERE tracker_id = $tracker;",
            cancellationToken, ("$tracker", trackerId));
        await ExecuteAsync(connection, transaction,
            "INSERT INTO tracker_schemas (tracker_id, schema_id) VALUES ($tracker, $schema);", cancellationToken,
            ("$tracker", trackerId), ("$schema", SqlitePersistenceValues.Format(link.SchemaId)));
        foreach (SchemaBaselineEntity entity in link.Entities)
        {
            // A linked entity must be one of the linked Tracker's own entities.
            using SqliteCommand command = Command(connection, transaction, """
                INSERT INTO tracker_schema_entities (tracker_id, schema_entity_id, entity_id, name, group_name)
                SELECT $tracker, $schemaEntity, $entity, $name, $group
                WHERE $entity IS NULL OR EXISTS (SELECT 1 FROM tracked_entities
                                                 WHERE id = $entity AND tracker_id = $tracker);
                """);
            command.Parameters.AddWithValue("$tracker", trackerId);
            command.Parameters.AddWithValue("$schemaEntity", SqlitePersistenceValues.Format(entity.SchemaEntityId));
            command.Parameters.AddWithValue("$entity",
                entity.EntityId is { } id ? SqlitePersistenceValues.Format(id) : DBNull.Value);
            command.Parameters.AddWithValue("$name", entity.Name);
            command.Parameters.AddWithValue("$group", entity.GroupName);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("A schema link refers to an entity of another tracker.");
        }
        foreach (SchemaBaselineEntity entity in link.Entities)
        foreach (SchemaEntityId dependency in entity.Dependencies)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO tracker_schema_dependencies
                (tracker_id, dependent_schema_entity_id, dependency_schema_entity_id)
                VALUES ($tracker, $dependent, $dependency);
                """, cancellationToken,
                ("$tracker", trackerId), ("$dependent", SqlitePersistenceValues.Format(entity.SchemaEntityId)),
                ("$dependency", SqlitePersistenceValues.Format(dependency)));
    }

    /// <summary>The links whose <c>tracker_schemas</c> row matches <paramref name="where"/>, given <c>$id</c>.</summary>
    internal static async Task<IReadOnlyList<TrackerSchemaLink>> ReadAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string where, string id, CancellationToken cancellationToken)
    {
        List<(string Tracker, string Schema)> links = [];
        using (SqliteCommand command = Command(connection, transaction,
                   $"SELECT tracker_id, schema_id FROM tracker_schemas WHERE {where} ORDER BY tracker_id;"))
        {
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                links.Add((reader.GetString(0), reader.GetString(1)));
        }

        List<TrackerSchemaLink> result = [];
        foreach ((string tracker, string schema) in links)
        {
            Dictionary<string, List<SchemaEntityId>> dependencies = new(StringComparer.Ordinal);
            using (SqliteCommand command = Command(connection, transaction, """
                       SELECT dependent_schema_entity_id, dependency_schema_entity_id
                       FROM tracker_schema_dependencies WHERE tracker_id = $tracker
                       ORDER BY dependent_schema_entity_id, dependency_schema_entity_id;
                       """))
            {
                command.Parameters.AddWithValue("$tracker", tracker);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!dependencies.TryGetValue(reader.GetString(0), out List<SchemaEntityId>? list))
                        dependencies[reader.GetString(0)] = list = [];
                    list.Add(SqlitePersistenceValues.ParseSchemaEntityId(reader.GetString(1)));
                }
            }

            List<SchemaBaselineEntity> entities = [];
            using (SqliteCommand command = Command(connection, transaction, """
                       SELECT schema_entity_id, entity_id, name, group_name
                       FROM tracker_schema_entities WHERE tracker_id = $tracker ORDER BY schema_entity_id;
                       """))
            {
                command.Parameters.AddWithValue("$tracker", tracker);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    entities.Add(new SchemaBaselineEntity(
                        SqlitePersistenceValues.ParseSchemaEntityId(reader.GetString(0)),
                        reader.IsDBNull(1) ? null : SqlitePersistenceValues.ParseEntityId(reader.GetString(1)),
                        reader.GetString(2), reader.GetString(3),
                        dependencies.GetValueOrDefault(reader.GetString(0)) ?? []));
            }
            result.Add(new TrackerSchemaLink(SqlitePersistenceValues.ParseTrackerId(tracker),
                SqlitePersistenceValues.ParseSchemaId(schema), entities));
        }
        return result;
    }

    private static async Task<string> FingerprintSchemaAsync(SqliteConnection connection,
        SqliteTransaction transaction, SchemaId schemaId, CancellationToken cancellationToken)
    {
        StringBuilder state = new();
        string[] queries =
        [
            "SELECT id, name, recycled_at_utc FROM schemas WHERE id = $id;",
            "SELECT id, name, group_name FROM schema_entities WHERE schema_id = $id ORDER BY id;",
            "SELECT d.dependent_entity_id, d.dependency_entity_id FROM schema_entity_dependencies d JOIN schema_entities e ON e.id = d.dependent_entity_id WHERE e.schema_id = $id ORDER BY 1, 2;",
            "SELECT tracker_id FROM tracker_schemas WHERE schema_id = $id ORDER BY tracker_id;"
        ];
        foreach (string query in queries)
        {
            state.Append('|');
            using SqliteCommand command = Command(connection, transaction, query);
            command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schemaId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                state.Append('[');
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string value = reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString()!;
                    state.Append(value.Length).Append(':').Append(value);
                }
                state.Append(']');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.ToString())));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql,
        CancellationToken cancellationToken, params (string Name, string? Value)[] parameters)
    {
        using SqliteCommand command = Command(connection, transaction, sql);
        foreach ((string name, string? value) in parameters)
            command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
