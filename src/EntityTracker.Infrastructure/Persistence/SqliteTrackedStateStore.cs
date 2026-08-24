using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Applies one validated tracked-schema change set using a single SQLite transaction.
/// Conditional upserts leave audit timestamps unchanged for relationships that did not change.
/// </summary>
public sealed class SqliteTrackedStateStore : ITrackedStateStore
{
    private readonly SqliteDatabase _database;

    public SqliteTrackedStateStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task ApplyAsync(
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (TrackedEntity entity in changeSet.EntitiesToAdd)
            {
                await InsertEntityAsync(
                    connection,
                    transaction,
                    entity,
                    timestamp,
                    cancellationToken);
                await InsertStatusHistoryAsync(
                    connection,
                    transaction,
                    entity.Id,
                    null,
                    entity.Status,
                    StatusHistoryEntryKind.Created,
                    timestamp,
                    cancellationToken);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesToUpdate)
            {
                await UpdateEntityAsync(
                    connection,
                    transaction,
                    entity,
                    timestamp,
                    cancellationToken);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithProgressToUpdate)
            {
                await InsertTransitionIfChangedAsync(
                    connection,
                    transaction,
                    entity,
                    timestamp,
                    cancellationToken);
                await UpdateProgressAsync(
                    connection,
                    transaction,
                    entity,
                    timestamp,
                    cancellationToken);
            }

            foreach (EntityId entityId in changeSet.EntityIdsToArchive)
            {
                await ArchiveEntityAsync(
                    connection,
                    transaction,
                    entityId,
                    timestamp,
                    cancellationToken);
            }

            foreach (EntityId entityId in changeSet.EntityIdsToRestore)
            {
                await RestoreEntityAsync(
                    connection,
                    transaction,
                    entityId,
                    timestamp,
                    cancellationToken);
            }

            foreach (EntityId ownerId in changeSet.ReconciledOwnerIds)
            {
                PersistedDependency[] desiredResolved = changeSet.ResolvedDependencies
                    .Where(dependency => dependency.Edge.DependentEntityId == ownerId)
                    .ToArray();
                PersistedUnresolvedDependency[] desiredUnresolved =
                    changeSet.UnresolvedDependencies
                        .Where(dependency =>
                            dependency.Dependency.DependentEntityId == ownerId)
                        .ToArray();

                await DeleteRemovedResolvedAsync(
                    connection,
                    transaction,
                    ownerId,
                    desiredResolved,
                    cancellationToken);
                await DeleteRemovedUnresolvedAsync(
                    connection,
                    transaction,
                    ownerId,
                    desiredUnresolved,
                    cancellationToken);

                foreach (PersistedDependency dependency in desiredResolved)
                {
                    await UpsertResolvedAsync(
                        connection,
                        transaction,
                        dependency,
                        timestamp,
                        cancellationToken);
                }

                foreach (PersistedUnresolvedDependency dependency in desiredUnresolved)
                {
                    await UpsertUnresolvedAsync(
                        connection,
                        transaction,
                        dependency,
                        timestamp,
                        cancellationToken);
                }
            }

            foreach (EntityId ownerId in changeSet.ReconciledOverrideOwnerIds)
            {
                ManualDependencyOverride[] desiredOverrides =
                    changeSet.ManualDependencyOverrides
                        .Where(item => item.DependentEntityId == ownerId)
                        .ToArray();

                await DeleteRemovedOverridesAsync(
                    connection,
                    transaction,
                    ownerId,
                    desiredOverrides,
                    cancellationToken);
                foreach (ManualDependencyOverride dependencyOverride in desiredOverrides)
                {
                    await UpsertOverrideAsync(
                        connection,
                        transaction,
                        dependencyOverride,
                        timestamp,
                        cancellationToken);
                }
            }

            if (changeSet.ProgressSnapshotAfterChanges is not null)
            {
                await InsertSnapshotIfChangedAsync(
                    connection,
                    transaction,
                    changeSet.ProgressSnapshotAfterChanges,
                    timestamp,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The tracked schema could not be changed because its candidate state is invalid.",
                exception);
        }
    }

    public async Task EnsureHistoryBaselineAsync(
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(snapshot);

        TrackedEntity[] entityArray = entities.ToArray();
        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        long historyCount = await CountAsync(
            connection,
            transaction,
            "entity_status_history",
            cancellationToken);
        long snapshotCount = await CountAsync(
            connection,
            transaction,
            "progress_snapshots",
            cancellationToken);
        if (historyCount > 0 || snapshotCount > 0)
        {
            if (snapshotCount == 0 || (entityArray.Length > 0 && historyCount == 0))
            {
                throw new InvalidDataException(
                    "The persisted progress history is incomplete and cannot be initialized safely.");
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        foreach (TrackedEntity entity in entityArray)
        {
            await InsertStatusHistoryAsync(
                connection,
                transaction,
                entity.Id,
                null,
                entity.Status,
                StatusHistoryEntryKind.Baseline,
                timestamp,
                cancellationToken);
        }

        await InsertSnapshotAsync(
            connection,
            transaction,
            snapshot,
            timestamp,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"SELECT COUNT(*) FROM {tableName};");
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertTransitionIfChangedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO entity_status_history
            (
                entity_id, previous_status, new_status, entry_kind, occurred_at_utc
            )
            SELECT id, development_status, $newStatus, 'Transition', $timestamp
            FROM tracked_entities
            WHERE id = $id AND development_status <> $newStatus;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$newStatus", entity.Status.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStatusHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId entityId,
        DevelopmentStatus? previousStatus,
        DevelopmentStatus newStatus,
        StatusHistoryEntryKind kind,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO entity_status_history
            (
                entity_id, previous_status, new_status, entry_kind, occurred_at_utc
            )
            VALUES ($entityId, $previousStatus, $newStatus, $kind, $timestamp);
            """);
        command.Parameters.AddWithValue("$entityId", SqlitePersistenceValues.Format(entityId));
        command.Parameters.AddWithValue(
            "$previousStatus",
            previousStatus is null ? DBNull.Value : previousStatus.Value.ToString());
        command.Parameters.AddWithValue("$newStatus", newStatus.ToString());
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSnapshotIfChangedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProgressSnapshotState snapshot,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT ready_count, blocked_count, in_progress_count, rework_needed_count,
                   development_completed_count, reconciled_count
            FROM progress_snapshots
            ORDER BY id DESC
            LIMIT 1;
            """);
        bool unchanged;
        await using (SqliteDataReader reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            unchanged = await reader.ReadAsync(cancellationToken) &&
                reader.GetInt32(0) == snapshot.ReadyCount &&
                reader.GetInt32(1) == snapshot.BlockedCount &&
                reader.GetInt32(2) == snapshot.InProgressCount &&
                reader.GetInt32(3) == snapshot.ReworkNeededCount &&
                reader.GetInt32(4) == snapshot.DevelopmentCompletedCount &&
                reader.GetInt32(5) == snapshot.ReconciledCount;
        }

        if (!unchanged)
        {
            await InsertSnapshotAsync(
                connection,
                transaction,
                snapshot,
                timestamp,
                cancellationToken);
        }
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProgressSnapshotState snapshot,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO progress_snapshots
            (
                recorded_at_utc, ready_count, blocked_count, in_progress_count,
                rework_needed_count, development_completed_count, reconciled_count
            )
            VALUES
            (
                $timestamp, $ready, $blocked, $inProgress, $rework,
                $developmentCompleted, $reconciled
            );
            """);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$ready", snapshot.ReadyCount);
        command.Parameters.AddWithValue("$blocked", snapshot.BlockedCount);
        command.Parameters.AddWithValue("$inProgress", snapshot.InProgressCount);
        command.Parameters.AddWithValue("$rework", snapshot.ReworkNeededCount);
        command.Parameters.AddWithValue(
            "$developmentCompleted",
            snapshot.DevelopmentCompletedCount);
        command.Parameters.AddWithValue("$reconciled", snapshot.ReconciledCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO tracked_entities
            (
                id, source_key, source_name, development_status, notes,
                lifecycle_state, provenance, created_at_utc, schema_updated_at_utc,
                progress_updated_at_utc
            )
            VALUES
            (
                $id, $sourceKey, $sourceName, $developmentStatus, $notes,
                $lifecycleState, $provenance, $timestamp, $timestamp, $timestamp
            );
            """);
        AddEntityParameters(command, entity);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET source_key = $sourceKey,
                source_name = $sourceName,
                lifecycle_state = $lifecycleState,
                provenance = $provenance,
                schema_updated_at_utc = $timestamp
            WHERE id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceKey", EntitySourceKey.From(entity.SourceName).Value);
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$lifecycleState", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue("$provenance", entity.Provenance.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ArchiveEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId entityId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET lifecycle_state = 'Archived',
                schema_updated_at_utc = $timestamp
            WHERE id = $id AND lifecycle_state = 'Active';
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entityId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RestoreEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId entityId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET lifecycle_state = 'Active',
                schema_updated_at_utc = $timestamp
            WHERE id = $id AND lifecycle_state = 'Archived';
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entityId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET development_status = $developmentStatus,
                notes = $notes,
                progress_updated_at_utc = $timestamp
            WHERE id = $id
              AND (development_status <> $developmentStatus OR notes <> $notes);
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$developmentStatus", entity.Status.ToString());
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteRemovedResolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId ownerId,
        IReadOnlyList<PersistedDependency> desired,
        CancellationToken cancellationToken)
    {
        string keepClause = AddKeepParameters(
            "dependency_entity_id",
            desired.Select(static dependency =>
                SqlitePersistenceValues.Format(dependency.Edge.DependencyEntityId)),
            out string[] values);
        using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"DELETE FROM schema_dependencies WHERE dependent_entity_id = $ownerId{keepClause};");
        command.Parameters.AddWithValue("$ownerId", SqlitePersistenceValues.Format(ownerId));
        AddKeepValues(command, values);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteRemovedUnresolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId ownerId,
        IReadOnlyList<PersistedUnresolvedDependency> desired,
        CancellationToken cancellationToken)
    {
        string keepClause = AddKeepParameters(
            "dependency_source_key",
            desired.Select(static dependency => EntitySourceKey.From(
                dependency.Dependency.DependencySourceName).Value),
            out string[] values);
        using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"DELETE FROM unresolved_schema_dependencies WHERE dependent_entity_id = $ownerId{keepClause};");
        command.Parameters.AddWithValue("$ownerId", SqlitePersistenceValues.Format(ownerId));
        AddKeepValues(command, values);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertResolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersistedDependency dependency,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO schema_dependencies
            (
                dependent_entity_id, dependency_entity_id, dependency_kind,
                created_at_utc, updated_at_utc
            )
            VALUES ($ownerId, $targetId, $kind, $timestamp, $timestamp)
            ON CONFLICT (dependent_entity_id, dependency_entity_id)
            DO UPDATE SET dependency_kind = excluded.dependency_kind,
                          updated_at_utc = excluded.updated_at_utc
            WHERE dependency_kind <> excluded.dependency_kind;
            """);
        command.Parameters.AddWithValue(
            "$ownerId",
            SqlitePersistenceValues.Format(dependency.Edge.DependentEntityId));
        command.Parameters.AddWithValue(
            "$targetId",
            SqlitePersistenceValues.Format(dependency.Edge.DependencyEntityId));
        command.Parameters.AddWithValue("$kind", dependency.Kind.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertUnresolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersistedUnresolvedDependency dependency,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO unresolved_schema_dependencies
            (
                dependent_entity_id, dependency_source_key, dependency_source_name,
                dependency_kind, created_at_utc, updated_at_utc
            )
            VALUES ($ownerId, $targetKey, $targetName, $kind, $timestamp, $timestamp)
            ON CONFLICT (dependent_entity_id, dependency_source_key)
            DO UPDATE SET dependency_source_name = excluded.dependency_source_name,
                          dependency_kind = excluded.dependency_kind,
                          updated_at_utc = excluded.updated_at_utc
            WHERE dependency_source_name <> excluded.dependency_source_name
               OR dependency_kind <> excluded.dependency_kind;
            """);
        command.Parameters.AddWithValue(
            "$ownerId",
            SqlitePersistenceValues.Format(dependency.Dependency.DependentEntityId));
        command.Parameters.AddWithValue(
            "$targetKey",
            EntitySourceKey.From(dependency.Dependency.DependencySourceName).Value);
        command.Parameters.AddWithValue(
            "$targetName",
            dependency.Dependency.DependencySourceName);
        command.Parameters.AddWithValue("$kind", dependency.Kind.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteRemovedOverridesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId ownerId,
        IReadOnlyList<ManualDependencyOverride> desired,
        CancellationToken cancellationToken)
    {
        string keepClause = AddKeepParameters(
            "dependency_source_key",
            desired.Select(static item =>
                EntitySourceKey.From(item.DependencySourceName).Value),
            out string[] values);
        using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"DELETE FROM manual_dependency_overrides WHERE dependent_entity_id = $ownerId{keepClause};");
        command.Parameters.AddWithValue("$ownerId", SqlitePersistenceValues.Format(ownerId));
        AddKeepValues(command, values);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertOverrideAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ManualDependencyOverride dependencyOverride,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO manual_dependency_overrides
            (
                dependent_entity_id, dependency_source_key, dependency_source_name,
                override_action, created_at_utc, updated_at_utc
            )
            VALUES ($ownerId, $targetKey, $targetName, $action, $timestamp, $timestamp)
            ON CONFLICT (dependent_entity_id, dependency_source_key)
            DO UPDATE SET dependency_source_name = excluded.dependency_source_name,
                          override_action = excluded.override_action,
                          updated_at_utc = excluded.updated_at_utc
            WHERE dependency_source_name <> excluded.dependency_source_name
               OR override_action <> excluded.override_action;
            """);
        command.Parameters.AddWithValue(
            "$ownerId",
            SqlitePersistenceValues.Format(dependencyOverride.DependentEntityId));
        command.Parameters.AddWithValue(
            "$targetKey",
            EntitySourceKey.From(dependencyOverride.DependencySourceName).Value);
        command.Parameters.AddWithValue("$targetName", dependencyOverride.DependencySourceName);
        command.Parameters.AddWithValue("$action", dependencyOverride.Action.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEntityParameters(SqliteCommand command, TrackedEntity entity)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceKey", EntitySourceKey.From(entity.SourceName).Value);
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$developmentStatus", entity.Status.ToString());
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$lifecycleState", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue("$provenance", entity.Provenance.ToString());
    }

    private static string AddKeepParameters(
        string columnName,
        IEnumerable<string> desiredValues,
        out string[] values)
    {
        values = desiredValues.Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 0
            ? string.Empty
            : $" AND {columnName} " +
              $"NOT IN ({string.Join(", ", values.Select((_, index) => $"$keep{index}"))})";
    }

    private static void AddKeepValues(SqliteCommand command, IReadOnlyList<string> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            command.Parameters.AddWithValue($"$keep{index}", values[index]);
        }
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }
}
