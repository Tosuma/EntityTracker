using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Collaboration;

public sealed class SqliteProjectStateStore(
    SqliteDatabase database,
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IEntityAuditReader auditReader,
    IDependencyRepository dependencyRepository,
    IManualDependencyOverrideRepository overrideRepository)
{
    public async Task<ProjectRepositoryState> ExportAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        Project project = await projectRepository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The Project no longer exists in SQLite.");
        IReadOnlyList<Tracker> trackers = await trackerRepository.GetByProjectAsync(projectId, cancellationToken);
        List<TrackerRepositoryState> trackerStates = [];
        foreach (Tracker tracker in trackers)
        {
            Task<IReadOnlyList<TrackedEntity>> entitiesTask = entityRepository.GetAllAsync(tracker.Id, cancellationToken);
            Task<IReadOnlyList<EntityAuditTimestamps>> auditsTask = auditReader.GetAllAsync(tracker.Id, cancellationToken);
            Task<IReadOnlyList<PersistedDependency>> resolvedTask = dependencyRepository.GetAllAsync(tracker.Id, cancellationToken);
            Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask = dependencyRepository.GetAllUnresolvedAsync(tracker.Id, cancellationToken);
            Task<IReadOnlyList<ManualDependencyOverride>> overridesTask = overrideRepository.GetAllAsync(tracker.Id, cancellationToken);
            await Task.WhenAll(entitiesTask, auditsTask, resolvedTask, unresolvedTask, overridesTask);
            Dictionary<EntityId, TrackedEntity> entities = (await entitiesTask).ToDictionary(item => item.Id);
            Dictionary<EntityId, EntityAuditTimestamps> audits = (await auditsTask).ToDictionary(item => item.EntityId);
            IReadOnlyList<PersistedDependency> resolved = await resolvedTask;
            IReadOnlyList<PersistedUnresolvedDependency> unresolved = await unresolvedTask;
            IReadOnlyList<ManualDependencyOverride> overrides = await overridesTask;
            EntityRepositoryState[] repositoryEntities = entities.Values.Select(entity =>
                new EntityRepositoryState(
                    entity,
                    audits[entity.Id],
                    resolved.Where(item => item.Edge.DependentEntityId == entity.Id)
                        .Select(item => new ImportedDependencyDeclaration(entities[item.Edge.DependencyEntityId].SourceName, item.Kind))
                        .Concat(unresolved.Where(item => item.Dependency.DependentEntityId == entity.Id)
                            .Select(item => new ImportedDependencyDeclaration(item.Dependency.DependencySourceName, item.Kind)))
                        .ToArray(),
                    overrides.Where(item => item.DependentEntityId == entity.Id).ToArray())).ToArray();
            trackerStates.Add(new TrackerRepositoryState(tracker, repositoryEntities));
        }

        IReadOnlyList<RepositoryOperation> operations = await ReadHistoryOperationsAsync(
            projectId, trackerStates, cancellationToken);
        return new ProjectRepositoryState(project, trackerStates, operations, []);
    }

    public async Task ReplaceAsync(
        ProjectRepositoryState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string projectId = SqlitePersistenceValues.Format(state.Project.Id);
            await ExecuteAsync(connection, transaction, """
                DELETE FROM entity_status_history
                WHERE entity_id IN
                (SELECT entity.id FROM tracked_entities entity
                 INNER JOIN trackers tracker ON tracker.id = entity.tracker_id
                 WHERE tracker.project_id = $projectId);
                DELETE FROM progress_snapshots
                WHERE tracker_id IN (SELECT id FROM trackers WHERE project_id = $projectId);
                DELETE FROM schema_import_summary
                WHERE tracker_id IN (SELECT id FROM trackers WHERE project_id = $projectId);
                DELETE FROM tracked_entities
                WHERE tracker_id IN (SELECT id FROM trackers WHERE project_id = $projectId);
                DELETE FROM trackers WHERE project_id = $projectId;
                DELETE FROM projects WHERE id = $projectId;
                """, [new("$projectId", projectId)], cancellationToken);

            bool projectDeleted = state.Tombstones.Any(item =>
                item.Kind == RepositoryTombstoneKind.Project && item.DeletedId == state.Project.Id.Value);
            if (!projectDeleted)
            {
                await InsertProjectAsync(connection, transaction, state.Project, cancellationToken);
                HashSet<TrackerId> liveTrackerIds = state.Trackers.Select(item => item.Tracker.Id).ToHashSet();
                foreach (TrackerRepositoryState tracker in state.Trackers)
                {
                    await InsertTrackerAsync(connection, transaction, tracker.Tracker,
                        copiedFromIsLive: false, cancellationToken);
                    foreach (EntityRepositoryState entity in tracker.Entities)
                        await InsertEntityAsync(connection, transaction, entity, cancellationToken);
                }
                foreach (TrackerRepositoryState tracker in state.Trackers.Where(item =>
                             item.Tracker.CopiedFromTrackerId is { } copiedFrom &&
                             liveTrackerIds.Contains(copiedFrom)))
                {
                    await ExecuteAsync(connection, transaction, """
                        UPDATE trackers
                        SET copied_from_tracker_id = $copiedFrom
                        WHERE id = $id;
                        """,
                        [new("$copiedFrom", SqlitePersistenceValues.Format(tracker.Tracker.CopiedFromTrackerId!)),
                            new("$id", SqlitePersistenceValues.Format(tracker.Tracker.Id))],
                        cancellationToken);
                }
                foreach (TrackerRepositoryState tracker in state.Trackers)
                    await InsertRelationshipsAsync(connection, transaction, tracker, cancellationToken);
                await InsertHistoryAsync(connection, transaction, state, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<RepositoryOperation>> ReadHistoryOperationsAsync(
        ProjectId projectId,
        IReadOnlyList<TrackerRepositoryState> trackers,
        CancellationToken cancellationToken)
    {
        Dictionary<OperationId, HistoryBuilder> operations = [];
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT history.operation_id, entity.tracker_id, history.entity_id,
                       history.previous_status, history.new_status, history.occurred_at_utc,
                       history.entry_kind
                FROM entity_status_history history
                INNER JOIN tracked_entities entity ON entity.id = history.entity_id
                INNER JOIN trackers tracker ON tracker.id = entity.tracker_id
                WHERE tracker.project_id = $projectId;
                """;
            command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                OperationId operationId = SqlitePersistenceValues.ParseOperationId(reader.GetString(0));
                HistoryBuilder builder = GetBuilder(operations, operationId,
                    SqlitePersistenceValues.ParseTimestamp(reader.GetString(5), "history timestamp"));
                builder.TrackerIds.Add(SqlitePersistenceValues.ParseTrackerId(reader.GetString(1)));
                EntityId entityId = SqlitePersistenceValues.ParseEntityId(reader.GetString(2));
                builder.EntityIds.Add(entityId);
                builder.Transitions.Add(new EntityStatusHistoryEntry(operationId, entityId,
                    reader.IsDBNull(3) ? null : SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(reader.GetString(3), "previous status"),
                    SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(reader.GetString(4), "status"),
                    SqlitePersistenceValues.ParseTimestamp(reader.GetString(5), "history timestamp"),
                    SqlitePersistenceValues.ParseEnum<StatusHistoryEntryKind>(reader.GetString(6), "history kind")));
            }
        }
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT snapshot.operation_id, snapshot.tracker_id, snapshot.recorded_at_utc,
                       snapshot.ready_count, snapshot.blocked_count, snapshot.in_progress_count,
                       snapshot.rework_needed_count, snapshot.development_completed_count,
                       snapshot.reconciled_count
                FROM progress_snapshots snapshot
                INNER JOIN trackers tracker ON tracker.id = snapshot.tracker_id
                WHERE tracker.project_id = $projectId;
                """;
            command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                OperationId operationId = SqlitePersistenceValues.ParseOperationId(reader.GetString(0));
                DateTimeOffset occurred = SqlitePersistenceValues.ParseTimestamp(reader.GetString(2), "snapshot timestamp");
                HistoryBuilder builder = GetBuilder(operations, operationId, occurred);
                TrackerId trackerId = SqlitePersistenceValues.ParseTrackerId(reader.GetString(1));
                builder.TrackerIds.Add(trackerId);
                builder.Snapshots.Add(new RepositoryProgressSnapshot(trackerId, occurred,
                    new ProgressSnapshotState(reader.GetInt32(3), reader.GetInt32(4),
                        reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8))));
            }
        }
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT summary.operation_id, summary.tracker_id, summary.applied_at_utc,
                       summary.source_file_name, summary.import_mode, summary.new_entity_count,
                       summary.changed_entity_count, summary.archived_entity_count,
                       summary.unchanged_entity_count, summary.unresolved_entity_count
                FROM schema_import_summary summary
                INNER JOIN trackers tracker ON tracker.id = summary.tracker_id
                WHERE tracker.project_id = $projectId;
                """;
            command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                OperationId operationId = SqlitePersistenceValues.ParseOperationId(reader.GetString(0));
                HistoryBuilder builder = GetBuilder(operations, operationId,
                    SqlitePersistenceValues.ParseTimestamp(reader.GetString(2), "import timestamp"));
                TrackerId trackerId = SqlitePersistenceValues.ParseTrackerId(reader.GetString(1));
                builder.TrackerIds.Add(trackerId);
                builder.Import = new RepositoryImportSummary(trackerId, reader.GetString(3),
                    SqlitePersistenceValues.ParseEnum<SchemaImportMode>(reader.GetString(4), "import mode"),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9));
            }
        }
        return operations.Select(item => new RepositoryOperation(item.Key,
            RepositoryOperationKind.HistoryImported, item.Value.OccurredAtUtc, [],
            item.Value.TrackerIds.ToArray(), item.Value.EntityIds.ToArray(),
            item.Value.Transitions, item.Value.Import, item.Value.Snapshots)).ToArray();
    }

    private static HistoryBuilder GetBuilder(Dictionary<OperationId, HistoryBuilder> values,
        OperationId id, DateTimeOffset occurred)
    {
        if (!values.TryGetValue(id, out HistoryBuilder? builder))
        {
            builder = new HistoryBuilder(occurred);
            values.Add(id, builder);
        }
        else if (occurred < builder.OccurredAtUtc) builder.OccurredAtUtc = occurred;
        return builder;
    }

    private static async Task InsertProjectAsync(SqliteConnection connection, SqliteTransaction transaction,
        Project project, CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            INSERT INTO projects
            (id, name_key, name, lifecycle_state, created_at_utc, updated_at_utc, recycled_at_utc)
            VALUES ($id, $key, $name, $lifecycle, $created, $updated, $recycled);
            """, [new("$id", SqlitePersistenceValues.Format(project.Id)), new("$key", Normalize(project.Name)),
                new("$name", project.Name), new("$lifecycle", project.LifecycleState.ToString()),
                new("$created", SqlitePersistenceValues.FormatTimestamp(project.CreatedAtUtc)),
                new("$updated", SqlitePersistenceValues.FormatTimestamp(project.UpdatedAtUtc)),
                new("$recycled", project.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(project.RecycledAtUtc.Value))], cancellationToken);

    private static async Task InsertTrackerAsync(SqliteConnection connection, SqliteTransaction transaction,
        Tracker tracker, bool copiedFromIsLive, CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            INSERT INTO trackers
            (id, project_id, name_key, name, lifecycle_state, created_at_utc, updated_at_utc, recycled_at_utc, copied_from_tracker_id)
            VALUES ($id, $projectId, $key, $name, $lifecycle, $created, $updated, $recycled, $copiedFrom);
            """, [new("$id", SqlitePersistenceValues.Format(tracker.Id)), new("$projectId", SqlitePersistenceValues.Format(tracker.ProjectId)),
                new("$key", Normalize(tracker.Name)), new("$name", tracker.Name), new("$lifecycle", tracker.LifecycleState.ToString()),
                new("$created", SqlitePersistenceValues.FormatTimestamp(tracker.CreatedAtUtc)),
                new("$updated", SqlitePersistenceValues.FormatTimestamp(tracker.UpdatedAtUtc)),
                new("$recycled", tracker.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(tracker.RecycledAtUtc.Value)),
                new("$copiedFrom", copiedFromIsLive && tracker.CopiedFromTrackerId is not null ? SqlitePersistenceValues.Format(tracker.CopiedFromTrackerId) : DBNull.Value)], cancellationToken);

    private static async Task InsertEntityAsync(SqliteConnection connection, SqliteTransaction transaction,
        EntityRepositoryState value, CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            INSERT INTO tracked_entities
            (id, tracker_id, source_key, source_name, development_status, notes, lifecycle_state,
             provenance, requested_priority, responsible_developer, group_name,
             created_at_utc, schema_updated_at_utc, progress_updated_at_utc)
            VALUES ($id, $trackerId, $key, $name, $status, $notes, $lifecycle, $provenance,
                    $priority, $developer, $group, $created, $schemaUpdated, $progressUpdated);
            """, [new("$id", SqlitePersistenceValues.Format(value.Entity.Id)), new("$trackerId", SqlitePersistenceValues.Format(value.Entity.TrackerId)),
                new("$key", EntitySourceKey.From(value.Entity.SourceName).Value), new("$name", value.Entity.SourceName),
                new("$status", value.Entity.Status.ToString()), new("$notes", value.Entity.Notes),
                new("$lifecycle", value.Entity.LifecycleState.ToString()), new("$provenance", value.Entity.Provenance.ToString()),
                new("$priority", value.Entity.RequestedPriority is null ? DBNull.Value : value.Entity.RequestedPriority.Value),
                new("$developer", value.Entity.ResponsibleDeveloper), new("$group", value.Entity.GroupName),
                new("$created", SqlitePersistenceValues.FormatTimestamp(value.AuditTimestamps.CreatedAtUtc)),
                new("$schemaUpdated", SqlitePersistenceValues.FormatTimestamp(value.AuditTimestamps.SchemaUpdatedAtUtc)),
                new("$progressUpdated", SqlitePersistenceValues.FormatTimestamp(value.AuditTimestamps.ProgressUpdatedAtUtc))], cancellationToken);

    private static async Task InsertRelationshipsAsync(SqliteConnection connection, SqliteTransaction transaction,
        TrackerRepositoryState tracker, CancellationToken cancellationToken)
    {
        Dictionary<string, TrackedEntity> activeByKey = tracker.Entities.Select(item => item.Entity)
            .Where(item => item.LifecycleState == EntityLifecycleState.Active)
            .ToDictionary(item => EntitySourceKey.From(item.SourceName).Value, StringComparer.Ordinal);
        foreach (EntityRepositoryState owner in tracker.Entities)
        {
            foreach (ImportedDependencyDeclaration dependency in owner.ImportedDependencies)
            {
                string key = EntitySourceKey.From(dependency.SourceName).Value;
                if (activeByKey.TryGetValue(key, out TrackedEntity? target))
                    await ExecuteAsync(connection, transaction, "INSERT INTO schema_dependencies (dependent_entity_id, dependency_entity_id, dependency_kind, created_at_utc, updated_at_utc) VALUES ($owner, $target, $kind, $time, $time);",
                        [new("$owner", SqlitePersistenceValues.Format(owner.Entity.Id)), new("$target", SqlitePersistenceValues.Format(target.Id)), new("$kind", dependency.Kind.ToString()), new("$time", SqlitePersistenceValues.FormatTimestamp(owner.AuditTimestamps.SchemaUpdatedAtUtc))], cancellationToken);
                else
                    await ExecuteAsync(connection, transaction, "INSERT INTO unresolved_schema_dependencies (dependent_entity_id, dependency_source_key, dependency_source_name, dependency_kind, created_at_utc, updated_at_utc) VALUES ($owner, $key, $name, $kind, $time, $time);",
                        [new("$owner", SqlitePersistenceValues.Format(owner.Entity.Id)), new("$key", key), new("$name", dependency.SourceName), new("$kind", dependency.Kind.ToString()), new("$time", SqlitePersistenceValues.FormatTimestamp(owner.AuditTimestamps.SchemaUpdatedAtUtc))], cancellationToken);
            }
            foreach (ManualDependencyOverride item in owner.ManualOverrides)
                await ExecuteAsync(connection, transaction, "INSERT INTO manual_dependency_overrides (dependent_entity_id, dependency_source_key, dependency_source_name, override_action, created_at_utc, updated_at_utc) VALUES ($owner, $key, $name, $action, $time, $time);",
                    [new("$owner", SqlitePersistenceValues.Format(owner.Entity.Id)), new("$key", EntitySourceKey.From(item.DependencySourceName).Value), new("$name", item.DependencySourceName), new("$action", item.Action.ToString()), new("$time", SqlitePersistenceValues.FormatTimestamp(owner.AuditTimestamps.SchemaUpdatedAtUtc))], cancellationToken);
        }
    }

    private static async Task InsertHistoryAsync(SqliteConnection connection, SqliteTransaction transaction,
        ProjectRepositoryState state, CancellationToken cancellationToken)
    {
        HashSet<EntityId> liveEntities = state.Trackers.SelectMany(item => item.Entities).Select(item => item.Entity.Id).ToHashSet();
        HashSet<TrackerId> liveTrackers = state.Trackers.Select(item => item.Tracker.Id).ToHashSet();
        foreach (RepositoryOperation operation in state.Operations.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id.Value))
        {
            foreach (EntityStatusHistoryEntry entry in operation.StatusTransitions.Where(item => liveEntities.Contains(item.EntityId)))
                await ExecuteAsync(connection, transaction, "INSERT INTO entity_status_history (operation_id, entity_id, previous_status, new_status, entry_kind, occurred_at_utc) VALUES ($operationId, $entityId, $previous, $new, $kind, $time);",
                    [new("$operationId", SqlitePersistenceValues.Format(operation.Id)), new("$entityId", SqlitePersistenceValues.Format(entry.EntityId)),
                        new("$previous", entry.PreviousStatus is null ? DBNull.Value : entry.PreviousStatus.Value.ToString()), new("$new", entry.NewStatus.ToString()),
                        new("$kind", entry.Kind.ToString()), new("$time", SqlitePersistenceValues.FormatTimestamp(entry.OccurredAtUtc))], cancellationToken);
            foreach (RepositoryProgressSnapshot snapshot in operation.RecordedProgressSnapshots.Where(item => liveTrackers.Contains(item.TrackerId)))
                await ExecuteAsync(connection, transaction, "INSERT INTO progress_snapshots (operation_id, tracker_id, recorded_at_utc, ready_count, blocked_count, in_progress_count, rework_needed_count, development_completed_count, reconciled_count) VALUES ($operationId, $trackerId, $time, $ready, $blocked, $progress, $rework, $completed, $reconciled);",
                    [new("$operationId", SqlitePersistenceValues.Format(operation.Id)), new("$trackerId", SqlitePersistenceValues.Format(snapshot.TrackerId)),
                        new("$time", SqlitePersistenceValues.FormatTimestamp(snapshot.RecordedAtUtc)), new("$ready", snapshot.State.ReadyCount), new("$blocked", snapshot.State.BlockedCount),
                        new("$progress", snapshot.State.InProgressCount), new("$rework", snapshot.State.ReworkNeededCount), new("$completed", snapshot.State.DevelopmentCompletedCount), new("$reconciled", snapshot.State.ReconciledCount)], cancellationToken);
        }
        foreach (var latest in state.Operations.Where(item => item.ImportSummary is not null && liveTrackers.Contains(item.ImportSummary.TrackerId))
                     .GroupBy(item => item.ImportSummary!.TrackerId).Select(group => group.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id.Value).Last()))
        {
            RepositoryImportSummary summary = latest.ImportSummary!;
            await ExecuteAsync(connection, transaction, "INSERT INTO schema_import_summary (tracker_id, operation_id, applied_at_utc, source_file_name, import_mode, new_entity_count, changed_entity_count, archived_entity_count, unchanged_entity_count, unresolved_entity_count) VALUES ($trackerId, $operationId, $time, $file, $mode, $new, $changed, $archived, $unchanged, $unresolved);",
                [new("$trackerId", SqlitePersistenceValues.Format(summary.TrackerId)), new("$operationId", SqlitePersistenceValues.Format(latest.Id)), new("$time", SqlitePersistenceValues.FormatTimestamp(latest.OccurredAtUtc)),
                    new("$file", summary.SourceFileName), new("$mode", summary.Mode.ToString()), new("$new", summary.NewEntityCount), new("$changed", summary.ChangedEntityCount),
                    new("$archived", summary.ArchivedEntityCount), new("$unchanged", summary.UnchangedEntityCount), new("$unresolved", summary.UnresolvedEntityCount)], cancellationToken);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, IEnumerable<SqliteParameter> parameters, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (SqliteParameter parameter in parameters) command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed class HistoryBuilder(DateTimeOffset occurredAtUtc)
    {
        public DateTimeOffset OccurredAtUtc { get; set; } = occurredAtUtc;
        public HashSet<TrackerId> TrackerIds { get; } = [];
        public HashSet<EntityId> EntityIds { get; } = [];
        public List<EntityStatusHistoryEntry> Transitions { get; } = [];
        public List<RepositoryProgressSnapshot> Snapshots { get; } = [];
        public RepositoryImportSummary? Import { get; set; }
    }
}
