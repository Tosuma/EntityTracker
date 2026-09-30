using System.Globalization;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteProjectSnapshotStore(SqliteDatabase database) : IProjectSnapshotStore
{
    public async Task<ProjectSnapshotRead> ReadAsync(
        ProjectId projectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        string id = SqlitePersistenceValues.Format(projectId);
        long revision = await RevisionAsync(connection, transaction, id, cancellationToken);
        Row? project = (await RowsAsync(connection, transaction,
            "SELECT * FROM projects WHERE id = $projectId;", id, cancellationToken)).SingleOrDefault();
        if (project is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProjectSnapshotRead(null, revision);
        }

        string? canonicalName = await CanonicalNameAsync(connection, transaction, id, cancellationToken);

        List<Row> trackerRows = await RowsAsync(connection, transaction,
            "SELECT * FROM trackers WHERE project_id = $projectId;", id, cancellationToken);
        List<Row> entityRows = await RowsAsync(connection, transaction, """
            SELECT entity.* FROM tracked_entities entity
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> dependencies = await RowsAsync(connection, transaction, """
            SELECT dependency.* FROM schema_dependencies dependency
            JOIN tracked_entities entity ON entity.id = dependency.dependent_entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> unresolved = await RowsAsync(connection, transaction, """
            SELECT dependency.* FROM unresolved_schema_dependencies dependency
            JOIN tracked_entities entity ON entity.id = dependency.dependent_entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> overrides = await RowsAsync(connection, transaction, """
            SELECT item.* FROM manual_dependency_overrides item
            JOIN tracked_entities entity ON entity.id = item.dependent_entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> history = await RowsAsync(connection, transaction, """
            SELECT entry.*, entity.tracker_id FROM entity_status_history entry
            JOIN tracked_entities entity ON entity.id = entry.entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId
            ORDER BY entry.occurred_at_utc, entry.id;
            """, id, cancellationToken);
        List<Row> progress = await RowsAsync(connection, transaction, """
            SELECT snapshot.* FROM progress_snapshots snapshot
            JOIN trackers tracker ON tracker.id = snapshot.tracker_id
            WHERE tracker.project_id = $projectId
            ORDER BY snapshot.recorded_at_utc, snapshot.id;
            """, id, cancellationToken);
        List<Row> summaries = await RowsAsync(connection, transaction, """
            SELECT summary.* FROM schema_import_summary summary
            JOIN trackers tracker ON tracker.id = summary.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> syncBaselines = await RowsAsync(connection, transaction, """
            SELECT baseline.* FROM tracker_sync_baselines baseline
            JOIN trackers tracker ON tracker.id = baseline.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var entitiesByTracker = entityRows.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var dependenciesByEntity = dependencies.ToLookup(row => row.Str("dependent_entity_id"), StringComparer.Ordinal);
        var unresolvedByEntity = unresolved.ToLookup(row => row.Str("dependent_entity_id"), StringComparer.Ordinal);
        var overridesByEntity = overrides.ToLookup(row => row.Str("dependent_entity_id"), StringComparer.Ordinal);
        var historyByTracker = history.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var progressByTracker = progress.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var summariesByTracker = summaries.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var baselinesByTracker = syncBaselines.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);

        SnapshotProject projectModel = new(project.Guid("id"), canonicalName ?? project.Str("name"),
            project.Str("lifecycle_state"), project.Time("created_at_utc"),
            project.Time("updated_at_utc"), project.TimeOrNull("recycled_at_utc"));
        SnapshotTracker[] trackers = trackerRows.Select(tracker =>
        {
            string trackerId = tracker.Str("id");
            SnapshotEntity[] entities = entitiesByTracker[trackerId]
                .Select(entity =>
                {
                    string entityId = entity.Str("id");
                    return new SnapshotEntity(entity.Guid("id"), entity.Guid("tracker_id"),
                        entity.Str("source_name"), entity.Str("development_status"), entity.Str("notes"),
                        entity.Str("lifecycle_state"), entity.Str("provenance"), entity.IntOrNull("requested_priority"),
                        entity.Str("responsible_developer"), entity.Str("group_name"),
                        entity.Time("created_at_utc"), entity.Time("schema_updated_at_utc"),
                        entity.Time("progress_updated_at_utc"),
                        dependenciesByEntity[entityId]
                            .Select(d => new SnapshotDependency(d.Guid("dependent_entity_id"),
                                d.Guid("dependency_entity_id"), d.Str("dependency_kind"),
                                d.Time("created_at_utc"), d.Time("updated_at_utc"))).ToArray(),
                        unresolvedByEntity[entityId]
                            .Select(d => new SnapshotUnresolvedDependency(d.Guid("dependent_entity_id"),
                                d.Str("dependency_source_name"), d.Str("dependency_kind"),
                                d.Time("created_at_utc"), d.Time("updated_at_utc"))).ToArray(),
                        overridesByEntity[entityId]
                            .Select(d => new SnapshotOverride(d.Guid("dependent_entity_id"),
                                d.Str("dependency_source_name"), d.Str("override_action"),
                                d.Time("created_at_utc"), d.Time("updated_at_utc"))).ToArray());
                }).ToArray();
            Row? summary = summariesByTracker[trackerId].SingleOrDefault();
            return new SnapshotTracker(tracker.Guid("id"), tracker.Guid("project_id"),
                tracker.Str("name"), tracker.Str("lifecycle_state"),
                tracker.Time("created_at_utc"), tracker.Time("updated_at_utc"),
                tracker.TimeOrNull("recycled_at_utc"), tracker.GuidOrNull("copied_from_tracker_id"),
                entities,
                historyByTracker[trackerId]
                    .Select((h, order) => new SnapshotStatusEvent(h.Guid("event_id"), h.Guid("entity_id"),
                        h.GuidOrNull("previous_event_id"), h.StrOrNull("previous_status"),
                        h.Str("new_status"), h.Str("entry_kind"), h.Time("occurred_at_utc"), order)).ToArray(),
                progressByTracker[trackerId]
                    .Select((p, order) => new SnapshotProgress(p.Guid("snapshot_id"), p.Time("recorded_at_utc"),
                        p.Int("ready_count"), p.Int("blocked_count"), p.Int("in_progress_count"),
                        p.Int("rework_needed_count"), p.Int("development_completed_count"),
                        p.Int("reconciled_count"), order, p.Int("manually_blocked_count"),
                        p.Int("reworking_count"))).ToArray(),
                summary is null ? null : new SnapshotImportSummary(summary.Time("applied_at_utc"),
                    summary.Str("source_file_name"), summary.Str("import_mode"),
                    summary.Int("new_entity_count"), summary.Int("changed_entity_count"),
                    summary.Int("archived_entity_count"), summary.Int("unchanged_entity_count"),
                    summary.Int("unresolved_entity_count")),
                baselinesByTracker[trackerId].SingleOrDefault()?.Str("baseline_json"));
        }).ToArray();
        ProjectSnapshot snapshot = new(ProjectSnapshot.CurrentFormatVersion, projectModel, trackers);
        ProjectSnapshotValidator.Validate(snapshot);
        return new ProjectSnapshotRead(snapshot, revision);
    }

    public async Task<long> ApplyAsync(
        ProjectSnapshot snapshot, long expectedRevision,
        CancellationToken cancellationToken = default) =>
        await ApplyAsync(snapshot, expectedRevision, null, cancellationToken);

    public async Task<long> ApplyAsync(
        ProjectSnapshot snapshot, long expectedRevision, string? localName,
        CancellationToken cancellationToken = default)
    {
        ProjectSnapshotValidator.Validate(snapshot);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        string projectId = snapshot.Project.Id.ToString("D");
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long current = await RevisionAsync(connection, transaction, projectId, cancellationToken);
        if (current != expectedRevision)
            throw new InvalidOperationException("The Project changed since its snapshot was read.");

        if (localName is null && await CanonicalNameAsync(connection, transaction, projectId, cancellationToken) is not null)
        {
            using SqliteCommand existingName = Command(connection, transaction,
                "SELECT name FROM projects WHERE id = $projectId;");
            existingName.Parameters.AddWithValue("$projectId", projectId);
            localName = (string?)await existingName.ExecuteScalarAsync(cancellationToken);
        }
        string storedName = localName?.Trim() ?? snapshot.Project.Name;
        if (storedName.Length == 0) throw new ArgumentException("A local Project name is required.", nameof(localName));

        await RemovePristineDefaultNameConflictAsync(
            connection, transaction, storedName, projectId, cancellationToken);
        await DeleteProjectAsync(connection, transaction, projectId, cancellationToken);
        await InsertAsync(connection, transaction, "projects",
            ["id", "name_key", "name", "lifecycle_state", "created_at_utc", "updated_at_utc", "recycled_at_utc"],
            [projectId, Key(storedName), storedName, snapshot.Project.LifecycleState,
             Time(snapshot.Project.CreatedAtUtc), Time(snapshot.Project.UpdatedAtUtc),
             TimeOrNull(snapshot.Project.RecycledAtUtc)], cancellationToken);
        if (!string.Equals(storedName, snapshot.Project.Name, StringComparison.Ordinal))
            await InsertAsync(connection, transaction, "project_snapshot_names",
                ["project_id", "canonical_name"], [projectId, snapshot.Project.Name], cancellationToken);
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        {
            await InsertAsync(connection, transaction, "trackers",
                ["id", "project_id", "name_key", "name", "lifecycle_state", "created_at_utc", "updated_at_utc", "recycled_at_utc", "copied_from_tracker_id"],
                [Id(tracker.Id), projectId, Key(tracker.Name), tracker.Name, tracker.LifecycleState,
                 Time(tracker.CreatedAtUtc), Time(tracker.UpdatedAtUtc), TimeOrNull(tracker.RecycledAtUtc),
                 tracker.CopiedFromTrackerId is { } origin ? Id(origin) : null], cancellationToken);
        }
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        foreach (SnapshotEntity entity in tracker.Entities)
        {
            await InsertAsync(connection, transaction, "tracked_entities",
                ["id", "tracker_id", "source_key", "source_name", "development_status", "notes", "lifecycle_state", "provenance", "requested_priority", "responsible_developer", "group_name", "created_at_utc", "schema_updated_at_utc", "progress_updated_at_utc"],
                [Id(entity.Id), Id(tracker.Id), EntitySourceKey.From(entity.SourceName).Value,
                 entity.SourceName, entity.DevelopmentStatus, entity.Notes, entity.LifecycleState,
                 entity.Provenance, entity.RequestedPriority, entity.ResponsibleDeveloper, entity.GroupName,
                 Time(entity.CreatedAtUtc), Time(entity.SchemaUpdatedAtUtc), Time(entity.ProgressUpdatedAtUtc)], cancellationToken);
        }
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        {
            await InsertTrackerContentsAsync(connection, transaction, tracker, cancellationToken);
        }
        return await CommitRevisionAsync(connection, transaction, projectId, cancellationToken);
    }

    public async Task PurgeAsync(ProjectId projectId, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        string id = SqlitePersistenceValues.Format(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (await RevisionAsync(connection, transaction, id, cancellationToken) != expectedRevision)
            throw new InvalidOperationException("The Project changed before permanent deletion.");
        using SqliteCommand state = Command(connection, transaction,
            "SELECT lifecycle_state FROM projects WHERE id = $projectId;");
        state.Parameters.AddWithValue("$projectId", id);
        if ((string?)await state.ExecuteScalarAsync(cancellationToken) != "Recycled")
            throw new InvalidOperationException("Only a recycled Project can be permanently deleted.");
        await DeleteProjectAsync(connection, transaction, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyTombstoneAsync(ProjectId projectId, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        string id = SqlitePersistenceValues.Format(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (await RevisionAsync(connection, transaction, id, cancellationToken) != expectedRevision)
            throw new InvalidOperationException("The Project changed before inbound deletion.");
        await DeleteProjectAsync(connection, transaction, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertTrackerContentsAsync(
        SqliteConnection connection, SqliteTransaction transaction, SnapshotTracker tracker,
        CancellationToken cancellationToken)
    {
        foreach (SnapshotEntity entity in tracker.Entities)
        {
            foreach (SnapshotDependency dependency in entity.Dependencies)
                await InsertAsync(connection, transaction, "schema_dependencies",
                    ["dependent_entity_id", "dependency_entity_id", "dependency_kind", "created_at_utc", "updated_at_utc"],
                    [Id(entity.Id), Id(dependency.DependencyEntityId), dependency.Kind,
                     Time(dependency.CreatedAtUtc), Time(dependency.UpdatedAtUtc)], cancellationToken);
            foreach (SnapshotUnresolvedDependency dependency in entity.UnresolvedDependencies)
                await InsertAsync(connection, transaction, "unresolved_schema_dependencies",
                    ["dependent_entity_id", "dependency_source_key", "dependency_source_name", "dependency_kind", "created_at_utc", "updated_at_utc"],
                    [Id(entity.Id), Key(dependency.DependencySourceName), dependency.DependencySourceName,
                     dependency.Kind, Time(dependency.CreatedAtUtc), Time(dependency.UpdatedAtUtc)], cancellationToken);
            foreach (SnapshotOverride item in entity.ManualOverrides)
                await InsertAsync(connection, transaction, "manual_dependency_overrides",
                    ["dependent_entity_id", "dependency_source_key", "dependency_source_name", "override_action", "created_at_utc", "updated_at_utc"],
                    [Id(entity.Id), Key(item.DependencySourceName), item.DependencySourceName,
                     item.Action, Time(item.CreatedAtUtc), Time(item.UpdatedAtUtc)], cancellationToken);
        }
        foreach (SnapshotStatusEvent entry in tracker.StatusHistory.OrderBy(e => e.Order))
            await InsertAsync(connection, transaction, "entity_status_history",
                ["entity_id", "previous_status", "new_status", "entry_kind", "occurred_at_utc", "event_id", "previous_event_id"],
                [Id(entry.EntityId), entry.PreviousStatus, entry.NewStatus, entry.Kind,
                 Time(entry.OccurredAtUtc), Id(entry.EventId),
                 entry.PreviousEventId is { } prior ? Id(prior) : null], cancellationToken);
        foreach (SnapshotProgress progress in tracker.ProgressHistory.OrderBy(p => p.Order))
            await InsertAsync(connection, transaction, "progress_snapshots",
                ["tracker_id", "recorded_at_utc", "ready_count", "blocked_count", "in_progress_count", "rework_needed_count", "development_completed_count", "reconciled_count", "snapshot_id", "manually_blocked_count", "reworking_count"],
                [Id(tracker.Id), Time(progress.RecordedAtUtc), progress.ReadyCount,
                 progress.BlockedCount, progress.InProgressCount, progress.ReworkNeededCount,
                 progress.DevelopmentCompletedCount, progress.ReconciledCount, Id(progress.SnapshotId),
                 progress.ManuallyBlockedCount, progress.ReworkingCount], cancellationToken);
        if (tracker.ImportSummary is { } summary)
            await InsertAsync(connection, transaction, "schema_import_summary",
                ["tracker_id", "applied_at_utc", "source_file_name", "import_mode", "new_entity_count", "changed_entity_count", "archived_entity_count", "unchanged_entity_count", "unresolved_entity_count"],
                [Id(tracker.Id), Time(summary.AppliedAtUtc), summary.SourceFileName, summary.Mode,
                 summary.NewEntityCount, summary.ChangedEntityCount, summary.ArchivedEntityCount,
                 summary.UnchangedEntityCount, summary.UnresolvedEntityCount], cancellationToken);
        if (tracker.SyncBaselineJson is { } baselineJson)
            await InsertAsync(connection, transaction, "tracker_sync_baselines",
                ["tracker_id", "baseline_json"],
                [Id(tracker.Id), baselineJson], cancellationToken);
    }

    private static async Task DeleteProjectAsync(
        SqliteConnection connection, SqliteTransaction transaction, string projectId,
        CancellationToken cancellationToken)
    {
        string trackerIds = "SELECT id FROM trackers WHERE project_id = $projectId";
        string entityIds = $"SELECT id FROM tracked_entities WHERE tracker_id IN ({trackerIds})";
        foreach (string sql in new[]
        {
            $"DELETE FROM entity_status_history WHERE entity_id IN ({entityIds});",
            $"DELETE FROM progress_snapshots WHERE tracker_id IN ({trackerIds});",
            $"DELETE FROM schema_import_summary WHERE tracker_id IN ({trackerIds});",
            $"DELETE FROM tracked_entities WHERE tracker_id IN ({trackerIds});",
            "DELETE FROM trackers WHERE project_id = $projectId;",
            "DELETE FROM projects WHERE id = $projectId;"
        })
            await ExecuteAsync(connection, transaction, sql, projectId, cancellationToken);
    }

    private static async Task RemovePristineDefaultNameConflictAsync(
        SqliteConnection connection, SqliteTransaction transaction, string incomingName,
        string incomingProjectId, CancellationToken cancellationToken)
    {
        if (Key(incomingName) != "DEFAULT PROJECT") return;
        using SqliteCommand command = Command(connection, transaction, """
            SELECT project.id FROM projects project
            WHERE project.name_key = 'DEFAULT PROJECT' AND project.id <> $projectId
              AND project.lifecycle_state = 'Active'
              AND (SELECT COUNT(*) FROM projects) = 1
              AND (SELECT COUNT(*) FROM trackers tracker
                   WHERE tracker.project_id = project.id AND tracker.name = 'Default tracker'
                     AND tracker.lifecycle_state = 'Active'
                     AND tracker.copied_from_tracker_id IS NULL) = 1
              AND (SELECT COUNT(*) FROM trackers) = 1
              AND (SELECT COUNT(*) FROM tracked_entities) = 0
              AND (SELECT COUNT(*) FROM entity_status_history) = 0
              AND (SELECT COUNT(*) FROM schema_import_summary) = 0
              AND (SELECT COUNT(*) FROM progress_snapshots) <= 1
              AND (SELECT COUNT(*) FROM progress_snapshots
                   WHERE ready_count <> 0 OR blocked_count <> 0 OR in_progress_count <> 0
                      OR rework_needed_count <> 0 OR development_completed_count <> 0
                      OR reconciled_count <> 0 OR manually_blocked_count <> 0
                      OR reworking_count <> 0) = 0;
            """);
        command.Parameters.AddWithValue("$projectId", incomingProjectId);
        string? placeholderId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (placeholderId is not null)
            await DeleteProjectAsync(connection, transaction, placeholderId, cancellationToken);
    }

    private static async Task<long> CommitRevisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id,
        CancellationToken cancellationToken)
    {
        long revision = await RevisionAsync(connection, transaction, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    private static async Task<long> RevisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction,
            "SELECT revision FROM project_revisions WHERE project_id = $projectId;");
        command.Parameters.AddWithValue("$projectId", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L,
            CultureInfo.InvariantCulture);
    }

    private static async Task<string?> CanonicalNameAsync(
        SqliteConnection connection, SqliteTransaction transaction, string id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction,
            "SELECT canonical_name FROM project_snapshot_names WHERE project_id = $projectId;");
        command.Parameters.AddWithValue("$projectId", id);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<List<Row>> RowsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string projectId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction, sql);
        command.Parameters.AddWithValue("$projectId", projectId);
        List<Row> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Dictionary<string, object?> values = new(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
                values.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(new Row(values));
        }
        return rows;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string projectId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction, sql);
        command.Parameters.AddWithValue("$projectId", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAsync(
        SqliteConnection connection, SqliteTransaction transaction, string table,
        string[] columns, object?[] values, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction,
            $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))});");
        for (int i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue("$p" + i, values[i] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
    private static string Key(string name) => name.Trim().ToUpperInvariant();
    private static string Time(DateTimeOffset value) => SqlitePersistenceValues.FormatTimestamp(value);
    private static string? TimeOrNull(DateTimeOffset? value) => value is { } timestamp ? Time(timestamp) : null;

    private sealed record Row(IReadOnlyDictionary<string, object?> Values)
    {
        public string Str(string column) => (string)Values[column]!;
        public string? StrOrNull(string column) => (string?)Values[column];
        public int Int(string column) => Convert.ToInt32(Values[column], CultureInfo.InvariantCulture);
        public int? IntOrNull(string column) => Values[column] is null ? null : Int(column);
        public Guid Guid(string column) => System.Guid.Parse(Str(column));
        public Guid? GuidOrNull(string column) => Values[column] is null ? null : Guid(column);
        public DateTimeOffset Time(string column) =>
            SqlitePersistenceValues.ParseTimestamp(Str(column), column);
        public DateTimeOffset? TimeOrNull(string column) => Values[column] is null ? null : Time(column);
    }
}
