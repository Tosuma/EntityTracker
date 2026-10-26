using EntityTracker.Application.History;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Applies one validated tracked-schema change set using a single SQLite transaction.
/// Conditional upserts leave audit timestamps unchanged for relationships that did not change.
/// </summary>
public sealed class SqliteTrackedStateStore : ITrackedStateStore, ITrackerSyncStore
{
    private readonly SqliteDatabase _database;

    public SqliteTrackedStateStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    internal SqliteDatabase Database => _database;

    public async Task ApplyAsync(
        TrackerId trackerId,
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default)
    {
        await ApplyInternalAsync(trackerId, changeSet, cancellationToken, null);
    }

    public async Task<TrackerSyncBaseline?> ReadBaselineAsync(
        TrackerId destinationTrackerId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await SqliteSyncBaselineRows.ReadAsync(connection, null,
            SqlitePersistenceValues.Format(destinationTrackerId), cancellationToken);
    }

    public async Task<string> FingerprintAsync(
        TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        string fingerprint = await FingerprintAsync(connection, transaction, trackerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fingerprint;
    }

    public async Task ApplyAsync(TrackerSyncCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        await ApplyInternalAsync(commit.DestinationTrackerId, commit.ChangeSet,
            cancellationToken, commit);
    }

    private async Task ApplyInternalAsync(
        TrackerId trackerId,
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken,
        TrackerSyncCommit? syncCommit)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(changeSet);

        DateTimeOffset appliedAtUtc = _database.TimeProvider.GetUtcNow();
        string timestamp = SqlitePersistenceValues.FormatTimestamp(appliedAtUtc);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (syncCommit is not null)
            {
                string sourceFingerprint = await FingerprintAsync(connection, transaction,
                    syncCommit.SourceTrackerId, cancellationToken);
                string destinationFingerprint = await FingerprintAsync(connection, transaction,
                    trackerId, cancellationToken);
                if (sourceFingerprint != syncCommit.ExpectedSourceFingerprint ||
                    destinationFingerprint != syncCommit.ExpectedDestinationFingerprint)
                    throw new InvalidOperationException(
                        "A tracker changed after this review opened. Open a new sync review.");
            }
            await ValidateScopeAsync(
                connection,
                transaction,
                trackerId,
                changeSet,
                cancellationToken);
            foreach (TrackedEntity entity in changeSet.EntitiesToAdd)
            {
                await InsertEntityAsync(
                    connection,
                    transaction,
                    trackerId,
                    entity,
                    timestamp,
                    cancellationToken);
                if (entity.IsPlaceholder) continue;
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

            // Dependencies change before entities are updated: a rename onto a placeholder's name
            // needs the placeholder's dependents moved, and the placeholder gone, first.
            foreach (EntityId ownerId in changeSet.DependencyOwnerIds)
            {
                EntityDependency[] desired = changeSet.Dependencies
                    .Where(item => item.DependentEntityId == ownerId)
                    .ToArray();

                await DeleteRemovedDependenciesAsync(
                    connection,
                    transaction,
                    ownerId,
                    desired,
                    cancellationToken);
                foreach (EntityDependency dependency in desired)
                {
                    await UpsertDependencyAsync(
                        connection,
                        transaction,
                        dependency,
                        timestamp,
                        cancellationToken);
                }
            }

            await DeleteUnreferencedPlaceholdersAsync(connection, transaction, trackerId, cancellationToken);

            foreach (ResponsibilitySelection selection in changeSet.ResponsibilitySelections)
                await SqliteResponsibilityWriter.ApplySelectionAsync(connection, transaction,
                    trackerId, selection, appliedAtUtc.ToUniversalTime(), cancellationToken);

            foreach (ResponsibilityAddition addition in changeSet.ResponsibilityAdditions)
                await SqliteResponsibilityWriter.AddCurrentAsync(connection, transaction,
                    trackerId, addition, appliedAtUtc.ToUniversalTime(), cancellationToken);

            foreach (ResponsibilityRemoval removal in changeSet.ResponsibilityRemovals)
                await SqliteResponsibilityWriter.RemoveCurrentAsync(connection, transaction,
                    trackerId, removal, appliedAtUtc.ToUniversalTime(), cancellationToken);

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
                bool statusChanged = await SqliteStatusEvents.AppendAsync(
                    connection,
                    transaction,
                    entity.Id,
                    entity.Status,
                    StatusHistoryEntryKind.Transition,
                    timestamp,
                    cancellationToken);
                await UpdateProgressAsync(
                    connection,
                    transaction,
                    entity,
                    statusChanged,
                    timestamp,
                    cancellationToken);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithRequestedPriorityToUpdate)
            {
                await UpdateRequestedPriorityAsync(
                    connection,
                    transaction,
                    entity,
                    cancellationToken);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithGroupNameToUpdate)
            {
                await UpdateGroupNameAsync(
                    connection,
                    transaction,
                    entity,
                    cancellationToken);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithDueDateToUpdate)
            {
                await UpdateDueDateAsync(
                    connection,
                    transaction,
                    entity,
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

            foreach (EntityId entityId in changeSet.EntityIdsToPurge)
            {
                await PurgeArchivedEntityAsync(
                    connection,
                    transaction,
                    trackerId,
                    entityId,
                    cancellationToken);
            }

            await DeleteUnreferencedPlaceholdersAsync(connection, transaction, trackerId, cancellationToken);

            if (changeSet.ProgressSnapshotAfterChanges is not null)
            {
                await InsertSnapshotIfChangedAsync(
                    connection,
                    transaction,
                    trackerId,
                    changeSet.ProgressSnapshotAfterChanges,
                    timestamp,
                    cancellationToken);
            }

            if (syncCommit is not null)
                await SqliteSyncBaselineRows.WriteAsync(connection, transaction,
                    SqlitePersistenceValues.Format(trackerId), syncCommit.Baseline, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The tracked schema could not be changed because its candidate state is invalid.",
                exception);
        }
    }

    private static async Task<string> FingerprintAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        TrackerId trackerId, CancellationToken cancellationToken)
    {
        string id = SqlitePersistenceValues.Format(trackerId);
        StringBuilder state = new();
        string[] queries =
        [
            "SELECT id, project_id, recycled_at_utc, copied_from_tracker_id FROM trackers WHERE id = $id ORDER BY id;",
            "SELECT e.id, e.source_name, s.development_status, e.notes, e.filter_active, e.shared_notes, e.lifecycle_state, e.requested_priority, e.due_date, e.group_name, e.schema_updated_at_utc, e.progress_updated_at_utc FROM tracked_entities e JOIN entity_current_status s ON s.entity_id = e.id WHERE e.tracker_id = $id ORDER BY e.id;",
            "SELECT d.dependent_entity_id, d.dependency_entity_id, d.updated_at_utc FROM entity_dependencies d JOIN tracked_entities e ON e.id = d.dependent_entity_id WHERE e.tracker_id = $id ORDER BY d.dependent_entity_id, d.dependency_entity_id;",
            "SELECT b.side, b.name, b.is_active, b.requested_priority, b.due_date, b.group_name, b.filter_active, d.dependency_name FROM tracker_sync_baseline_entities b LEFT JOIN tracker_sync_baseline_dependencies d ON d.baseline_entity_id = b.id WHERE b.tracker_id = $id ORDER BY b.side, b.name, d.dependency_name;",
            "SELECT p.id, p.entity_id, p.developer_id, p.started_at_utc, p.ended_at_utc FROM responsibility_periods p JOIN tracked_entities e ON e.id = p.entity_id WHERE e.tracker_id = $id ORDER BY p.id;"
        ];
        foreach (string query in queries)
        {
            state.Append('|');
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = query;
            command.Parameters.AddWithValue("$id", id);
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

    public async Task EnsureHistoryBaselineAsync(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(snapshot);

        TrackedEntity[] entityArray = entities.ToArray();
        if (entityArray.Any(entity => entity.TrackerId != trackerId))
        {
            throw new InvalidOperationException("History baseline entities belong to another tracker.");
        }
        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await ValidateHistoryScopeAsync(
            connection,
            transaction,
            trackerId,
            entityArray,
            cancellationToken);

        // Unresolved references have no progress of their own and must remain removable.
        entityArray = entityArray.Where(static entity => !entity.IsPlaceholder).ToArray();

        long snapshotCount = await CountAsync(
            connection,
            transaction,
            "progress_snapshots",
            trackerId,
            cancellationToken);
        if (snapshotCount > 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // The status history is each entity's status; an entity without events (Not started) gets
        // its baseline event, and the Tracker its first progress snapshot.
        foreach (TrackedEntity entity in entityArray)
        {
            using (SqliteCommand events = CreateCommand(connection, transaction,
                       "SELECT COUNT(*) FROM entity_status_history WHERE entity_id = $id;"))
            {
                events.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
                if (Convert.ToInt64(await events.ExecuteScalarAsync(cancellationToken),
                        System.Globalization.CultureInfo.InvariantCulture) > 0)
                    continue;
            }

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
            trackerId,
            snapshot,
            timestamp,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ValidateHistoryScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        IReadOnlyCollection<TrackedEntity> entities,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand trackerCommand = CreateCommand(connection, transaction, """
            SELECT COUNT(*)
            FROM trackers tracker
            INNER JOIN projects project ON project.id = tracker.project_id
            WHERE tracker.id = $trackerId
              AND tracker.recycled_at_utc IS NULL
              AND project.recycled_at_utc IS NULL;
            """))
        {
            trackerCommand.Parameters.AddWithValue(
                "$trackerId",
                SqlitePersistenceValues.Format(trackerId));
            if (Convert.ToInt64(
                    await trackerCommand.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidOperationException("The selected tracker is not active.");
            }
        }

        if (entities.Count == 0)
        {
            return;
        }

        using SqliteCommand entityCommand = CreateCommand(connection, transaction, $"""
            SELECT COUNT(*)
            FROM tracked_entities
            WHERE tracker_id = $trackerId
              AND id IN ({string.Join(", ", entities.Select((_, index) => $"$entityId{index}"))});
            """);
        entityCommand.Parameters.AddWithValue(
            "$trackerId",
            SqlitePersistenceValues.Format(trackerId));
        int index = 0;
        foreach (TrackedEntity entity in entities)
        {
            entityCommand.Parameters.AddWithValue(
                $"$entityId{index++}",
                SqlitePersistenceValues.Format(entity.Id));
        }

        long ownedCount = Convert.ToInt64(
            await entityCommand.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (ownedCount != entities.Select(static entity => entity.Id).Distinct().Count())
        {
            throw new InvalidOperationException(
                "History baseline entities are not owned by the selected tracker.");
        }
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        TrackerId trackerId,
        CancellationToken cancellationToken)
    {
        string sql = tableName == "entity_status_history"
            ? """
                SELECT COUNT(*)
                FROM entity_status_history history
                INNER JOIN tracked_entities entity ON entity.id = history.entity_id
                WHERE entity.tracker_id = $trackerId;
                """
            : $"SELECT COUNT(*) FROM {tableName} WHERE tracker_id = $trackerId;";
        using SqliteCommand command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task InsertStatusHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId entityId,
        DevelopmentStatus? previousStatus,
        DevelopmentStatus newStatus,
        StatusHistoryEntryKind kind,
        string timestamp,
        CancellationToken cancellationToken)
    {
        _ = previousStatus;
        return SqliteStatusEvents.AppendAsync(
            connection, transaction, entityId, newStatus, kind, timestamp, cancellationToken);
    }

    private static async Task InsertSnapshotIfChangedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        ProgressSnapshotState snapshot,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT ready_count, blocked_count, in_progress_count, rework_needed_count,
                   development_completed_count, reconciled_count,
                   manually_blocked_count, reworking_count
            FROM progress_snapshots
            WHERE tracker_id = $trackerId
            ORDER BY recorded_at_utc DESC, sequence DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
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
                reader.GetInt32(5) == snapshot.ReconciledCount &&
                reader.GetInt32(6) == snapshot.ManuallyBlockedCount &&
                reader.GetInt32(7) == snapshot.ReworkingCount;
        }

        if (!unchanged)
        {
            await InsertSnapshotAsync(
                connection,
                transaction,
                trackerId,
                snapshot,
                timestamp,
                cancellationToken);
        }
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        ProgressSnapshotState snapshot,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO progress_snapshots
            (
                tracker_id, sequence, recorded_at_utc, ready_count, blocked_count, in_progress_count,
                rework_needed_count, development_completed_count, reconciled_count, snapshot_id,
                manually_blocked_count, reworking_count
            )
            VALUES
            (
                $trackerId, (SELECT COALESCE(MAX(sequence), 0) + 1 FROM progress_snapshots),
                $timestamp, $ready, $blocked, $inProgress, $rework,
                $developmentCompleted, $reconciled, $snapshotId, $manualBlocked, $reworking
            );
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$ready", snapshot.ReadyCount);
        command.Parameters.AddWithValue("$blocked", snapshot.BlockedCount);
        command.Parameters.AddWithValue("$inProgress", snapshot.InProgressCount);
        command.Parameters.AddWithValue("$rework", snapshot.ReworkNeededCount);
        command.Parameters.AddWithValue(
            "$developmentCompleted",
            snapshot.DevelopmentCompletedCount);
        command.Parameters.AddWithValue("$reconciled", snapshot.ReconciledCount);
        command.Parameters.AddWithValue("$manualBlocked", snapshot.ManuallyBlockedCount);
        command.Parameters.AddWithValue("$reworking", snapshot.ReworkingCount);
        command.Parameters.AddWithValue("$snapshotId", Guid.NewGuid().ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        TrackedEntity entity,
        string timestamp,
        CancellationToken cancellationToken)
    {
        if (entity.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The entity belongs to another tracker.");
        }
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO tracked_entities
            (
                id, tracker_id, source_name, notes, filter_active, shared_notes,
                lifecycle_state, requested_priority, due_date, group_name,
                created_at_utc, schema_updated_at_utc,
                progress_updated_at_utc
            )
            VALUES
            (
                $id, $trackerId, $sourceName, $notes, $filterActive, $sharedNotes,
                $lifecycleState, $requestedPriority, $dueDate, $groupName,
                $timestamp, $timestamp, $timestamp
            )
            -- Adding an entity with a placeholder's ID turns the placeholder into it, so the
            -- dependencies on the placeholder now refer to the new entity.
            ON CONFLICT (id) DO UPDATE SET
                source_name = excluded.source_name,
                notes = excluded.notes,
                filter_active = excluded.filter_active,
                shared_notes = excluded.shared_notes,
                lifecycle_state = excluded.lifecycle_state,
                requested_priority = excluded.requested_priority,
                due_date = excluded.due_date,
                group_name = excluded.group_name,
                created_at_utc = excluded.created_at_utc,
                schema_updated_at_utc = excluded.schema_updated_at_utc,
                progress_updated_at_utc = excluded.progress_updated_at_utc
            WHERE tracked_entities.lifecycle_state = 'Placeholder'
              AND tracked_entities.tracker_id = excluded.tracker_id;
            """);
        AddEntityParameters(command, entity);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("An entity with this ID already exists.");
    }

    /// <summary>A placeholder only exists while a dependency refers to it.</summary>
    private static async Task DeleteUnreferencedPlaceholdersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            DELETE FROM tracked_entities
            WHERE tracker_id = $trackerId
              AND lifecycle_state = 'Placeholder'
              AND NOT EXISTS (SELECT 1 FROM entity_dependencies dependency
                              WHERE dependency.dependency_entity_id = tracked_entities.id);
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
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
            SET source_name = $sourceName,
                lifecycle_state = $lifecycleState,
                schema_updated_at_utc = $timestamp
            WHERE id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$lifecycleState", entity.LifecycleState.ToString());
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

    private static async Task PurgeArchivedEntityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        EntityId entityId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand history = CreateCommand(connection, transaction, """
            DELETE FROM entity_status_history
            WHERE entity_id = $entityId;
            """);
        history.Parameters.AddWithValue("$entityId", SqlitePersistenceValues.Format(entityId));
        await history.ExecuteNonQueryAsync(cancellationToken);

        // An entity other entities depend on becomes a placeholder: it keeps its ID and name, so their
        // dependencies stay as unresolved references, and loses everything else.
        using SqliteCommand entity = CreateCommand(connection, transaction, """
            DELETE FROM responsibility_periods WHERE entity_id = $entityId
              AND EXISTS (SELECT 1 FROM entity_dependencies WHERE dependency_entity_id = $entityId);
            DELETE FROM entity_dependencies WHERE dependent_entity_id = $entityId
              AND EXISTS (SELECT 1 FROM entity_dependencies WHERE dependency_entity_id = $entityId);
            UPDATE tracked_entities
            SET lifecycle_state = 'Placeholder', notes = '',
                filter_active = '', shared_notes = '',
                requested_priority = NULL, due_date = NULL, group_name = ''
            WHERE id = $entityId
              AND tracker_id = $trackerId
              AND lifecycle_state = 'Archived'
              AND EXISTS (SELECT 1 FROM entity_dependencies WHERE dependency_entity_id = $entityId);
            DELETE FROM tracked_entities
            WHERE id = $entityId
              AND tracker_id = $trackerId
              AND lifecycle_state = 'Archived';
            SELECT changes() + (SELECT COUNT(*) FROM tracked_entities
                                WHERE id = $entityId AND lifecycle_state = 'Placeholder');
            """);
        entity.Parameters.AddWithValue("$entityId", SqlitePersistenceValues.Format(entityId));
        entity.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        if (Convert.ToInt64(await entity.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                "Only an archived entity in this tracker can be permanently deleted.");
        }
    }

    /// <summary>
    /// Saves an entity's notes and Filter active, and records when its progress last changed. Its
    /// status is its status history, which <paramref name="statusChanged"/> says was appended to.
    /// </summary>
    private static async Task UpdateProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        bool statusChanged,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET notes = $notes,
                filter_active = $filterActive,
                shared_notes = $sharedNotes,
                progress_updated_at_utc = $timestamp
            WHERE id = $id
              AND ($statusChanged = 1 OR notes <> $notes OR filter_active <> $filterActive OR shared_notes <> $sharedNotes);
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$statusChanged", statusChanged ? 1 : 0);
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$filterActive", entity.FilterActive);
        command.Parameters.AddWithValue("$sharedNotes", entity.SharedNotes);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRequestedPriorityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET requested_priority = $requestedPriority
            WHERE id = $id
              AND requested_priority IS NOT $requestedPriority;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue(
            "$requestedPriority",
            entity.RequestedPriority is null ? DBNull.Value : entity.RequestedPriority.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDueDateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET due_date = $dueDate
            WHERE id = $id
              AND due_date IS NOT $dueDate;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$dueDate", SqlitePersistenceValues.FormatDate(entity.DueDate));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateGroupNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackedEntity entity,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE tracked_entities
            SET group_name = $groupName
            WHERE id = $id
              AND group_name <> $groupName;
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$groupName", entity.GroupName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteRemovedDependenciesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId ownerId,
        IReadOnlyList<EntityDependency> desired,
        CancellationToken cancellationToken)
    {
        string keepClause = AddKeepParameters(
            "dependency_entity_id",
            desired.Select(static item => SqlitePersistenceValues.Format(item.DependencyEntityId)),
            out string[] values);
        using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"DELETE FROM entity_dependencies WHERE dependent_entity_id = $ownerId{keepClause};");
        command.Parameters.AddWithValue("$ownerId", SqlitePersistenceValues.Format(ownerId));
        AddKeepValues(command, values);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertDependencyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityDependency dependency,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO entity_dependencies
            (dependent_entity_id, dependency_entity_id, created_at_utc, updated_at_utc)
            VALUES ($dependentEntityId, $targetId, $timestamp, $timestamp)
            ON CONFLICT (dependent_entity_id, dependency_entity_id) DO NOTHING;
            """);
        SqliteDependencyValues.Add(command, dependency);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEntityParameters(SqliteCommand command, TrackedEntity entity)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$filterActive", entity.FilterActive);
        command.Parameters.AddWithValue("$sharedNotes", entity.SharedNotes);
        command.Parameters.AddWithValue("$lifecycleState", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue(
            "$requestedPriority",
            entity.RequestedPriority is null ? DBNull.Value : entity.RequestedPriority.Value);
        command.Parameters.AddWithValue("$dueDate", SqlitePersistenceValues.FormatDate(entity.DueDate));
        command.Parameters.AddWithValue("$groupName", entity.GroupName);
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

    private static async Task ValidateScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand trackerCommand = CreateCommand(connection, transaction, """
            SELECT COUNT(*)
            FROM trackers tracker
            INNER JOIN projects project ON project.id = tracker.project_id
            WHERE tracker.id = $trackerId
              AND tracker.recycled_at_utc IS NULL
              AND project.recycled_at_utc IS NULL;
            """))
        {
            trackerCommand.Parameters.AddWithValue(
                "$trackerId",
                SqlitePersistenceValues.Format(trackerId));
            if (Convert.ToInt64(await trackerCommand.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new InvalidOperationException("The selected tracker is not active.");
            }
        }

        TrackedEntity[] added = changeSet.EntitiesToAdd.ToArray();
        if (added.Any(entity => entity.TrackerId != trackerId) ||
            added.Select(static entity => entity.Id).Distinct().Count() != added.Length)
        {
            throw new InvalidOperationException("The change set contains invalid tracker ownership.");
        }

        HashSet<EntityId> addedIds = added.Select(static entity => entity.Id).ToHashSet();
        HashSet<EntityId> referencedIds = [];
        referencedIds.UnionWith(changeSet.EntitiesToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntitiesWithProgressToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntitiesWithRequestedPriorityToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntitiesWithResponsibleDeveloperToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntitiesWithGroupNameToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntitiesWithDueDateToUpdate.Select(static entity => entity.Id));
        referencedIds.UnionWith(changeSet.EntityIdsToArchive);
        referencedIds.UnionWith(changeSet.EntityIdsToRestore);
        referencedIds.UnionWith(changeSet.DependencyOwnerIds);
        referencedIds.UnionWith(changeSet.Dependencies.Select(static item => item.DependentEntityId));
        referencedIds.UnionWith(changeSet.Dependencies.Select(static item => item.DependencyEntityId));
        referencedIds.UnionWith(changeSet.ResponsibilityAdditions.Select(static item => item.EntityId));
        referencedIds.UnionWith(changeSet.ResponsibilityRemovals.Select(static item => item.EntityId));
        referencedIds.ExceptWith(addedIds);

        foreach (TrackedEntity entity in changeSet.EntitiesToUpdate
                     .Concat(changeSet.EntitiesWithProgressToUpdate)
                     .Concat(changeSet.EntitiesWithRequestedPriorityToUpdate)
                     .Concat(changeSet.EntitiesWithResponsibleDeveloperToUpdate)
                     .Concat(changeSet.EntitiesWithGroupNameToUpdate)
                     .Concat(changeSet.EntitiesWithDueDateToUpdate))
        {
            if (entity.TrackerId != trackerId)
            {
                throw new InvalidOperationException("The change set contains an entity from another tracker.");
            }
        }

        foreach (EntityId entityId in referencedIds)
        {
            using SqliteCommand command = CreateCommand(connection, transaction, """
                SELECT COUNT(*) FROM tracked_entities
                WHERE tracker_id = $trackerId AND id = $entityId;
                """);
            command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
            command.Parameters.AddWithValue("$entityId", SqlitePersistenceValues.Format(entityId));
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new InvalidOperationException(
                    "The change set references an entity outside the selected tracker.");
            }
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
