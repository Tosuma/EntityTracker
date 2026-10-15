using System.Globalization;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Dependencies;
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
            "SELECT *, CASE WHEN recycled_at_utc IS NULL THEN 'Active' ELSE 'Recycled' END AS lifecycle_state FROM projects WHERE id = $projectId;", id, cancellationToken)).SingleOrDefault();
        if (project is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProjectSnapshotRead(null, revision);
        }

        string? canonicalName = await CanonicalNameAsync(connection, transaction, id, cancellationToken);

        List<Row> trackerRows = await RowsAsync(connection, transaction,
            "SELECT *, CASE WHEN recycled_at_utc IS NULL THEN 'Active' ELSE 'Recycled' END AS lifecycle_state FROM trackers WHERE project_id = $projectId;", id, cancellationToken);
        List<Row> developerRows = await RowsAsync(connection, transaction,
            "SELECT * FROM project_developers WHERE project_id = $projectId ORDER BY id;",
            id, cancellationToken);
        List<Row> entityRows = await RowsAsync(connection, transaction, """
            SELECT entity.*, status.development_status FROM tracked_entities entity
            JOIN entity_current_status status ON status.entity_id = entity.id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> responsibilityRows = await RowsAsync(connection, transaction, """
            SELECT period.* FROM responsibility_periods period
            JOIN tracked_entities entity ON entity.id = period.entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> dependencies = await RowsAsync(connection, transaction, """
            SELECT dependency.* FROM entity_dependencies dependency
            JOIN tracked_entities entity ON entity.id = dependency.dependent_entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            WHERE tracker.project_id = $projectId;
            """, id, cancellationToken);
        List<Row> history = await RowsAsync(connection, transaction, """
            SELECT entry.*, entity.tracker_id, previous.new_status AS previous_status
            FROM entity_status_history entry
            JOIN tracked_entities entity ON entity.id = entry.entity_id
            JOIN trackers tracker ON tracker.id = entity.tracker_id
            LEFT JOIN entity_status_history previous ON previous.event_id = entry.previous_event_id
            WHERE tracker.project_id = $projectId
            ORDER BY entry.occurred_at_utc, entry.sequence;
            """, id, cancellationToken);
        List<Row> progress = await RowsAsync(connection, transaction, """
            SELECT snapshot.* FROM progress_snapshots snapshot
            JOIN trackers tracker ON tracker.id = snapshot.tracker_id
            WHERE tracker.project_id = $projectId
            ORDER BY snapshot.recorded_at_utc, snapshot.sequence;
            """, id, cancellationToken);
        Dictionary<string, TrackerSyncBaseline> baselines = new(StringComparer.Ordinal);
        foreach (Row tracker in trackerRows)
        {
            if (await SqliteSyncBaselineRows.ReadAsync(connection, transaction, tracker.Str("id"),
                    cancellationToken) is { } baseline)
                baselines[tracker.Str("id")] = baseline.Normalize();
        }
        await transaction.CommitAsync(cancellationToken);

        var entitiesByTracker = entityRows.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var responsibilityByEntity = responsibilityRows.ToLookup(row => row.Str("entity_id"), StringComparer.Ordinal);
        var dependenciesByEntity = dependencies.ToLookup(row => row.Str("dependent_entity_id"), StringComparer.Ordinal);
        var historyByTracker = history.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);
        var progressByTracker = progress.ToLookup(row => row.Str("tracker_id"), StringComparer.Ordinal);

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
                        entity.Str("lifecycle_state"), entity.IntOrNull("requested_priority"),
                        string.Empty, entity.Str("group_name"),
                        entity.Time("created_at_utc"), entity.Time("schema_updated_at_utc"),
                        entity.Time("progress_updated_at_utc"),
                        dependenciesByEntity[entityId]
                            .Select(d => new SnapshotDependency(d.Guid("dependency_entity_id"),
                                d.Time("created_at_utc"), d.Time("updated_at_utc")))
                            .OrderBy(d => d.DependencyEntityId).ToArray(),
                        responsibilityByEntity[entityId]
                            .Select(d => new SnapshotResponsibilityPeriod(d.Guid("id"),
                                d.Guid("entity_id"), d.Guid("developer_id"),
                                d.Time("started_at_utc"), d.TimeOrNull("ended_at_utc")))
                            .OrderBy(d => d.StartedAtUtc).ThenBy(d => d.Id).ToArray(),
                        entity.Str("filter_active"), entity.Str("shared_notes"));
                }).ToArray();
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
                baselines.GetValueOrDefault(trackerId));
        }).ToArray();
        SnapshotDeveloper[] developers = developerRows.Select(row => new SnapshotDeveloper(
            row.Guid("id"), row.Guid("project_id"), row.Str("initials"),
            row.Str("display_name"), row.Int("is_retired") != 0)).ToArray();
        ProjectSnapshot snapshot = new(ProjectSnapshot.CurrentFormatVersion, projectModel,
            trackers, developers);
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
        snapshot = ProjectSnapshotUpgrade.ToCurrent(snapshot, database.TimeProvider.GetUtcNow().ToUniversalTime());
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
            ["id", "name", "created_at_utc", "updated_at_utc", "recycled_at_utc"],
            [projectId, storedName, Time(snapshot.Project.CreatedAtUtc), Time(snapshot.Project.UpdatedAtUtc),
             TimeOrNull(snapshot.Project.RecycledAtUtc)], cancellationToken);
        if (!string.Equals(storedName, snapshot.Project.Name, StringComparison.Ordinal))
            await InsertAsync(connection, transaction, "project_snapshot_names",
                ["project_id", "canonical_name"], [projectId, snapshot.Project.Name], cancellationToken);
        foreach (SnapshotDeveloper developer in snapshot.Developers ?? [])
            await InsertAsync(connection, transaction, "project_developers",
                ["id", "project_id", "initials", "display_name", "is_retired"],
                [Id(developer.Id), projectId, developer.Initials, developer.DisplayName,
                 developer.IsRetired ? 1 : 0],
                cancellationToken);
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        {
            await InsertAsync(connection, transaction, "trackers",
                ["id", "project_id", "name", "created_at_utc", "updated_at_utc", "recycled_at_utc", "copied_from_tracker_id"],
                [Id(tracker.Id), projectId, tracker.Name,
                 Time(tracker.CreatedAtUtc), Time(tracker.UpdatedAtUtc), TimeOrNull(tracker.RecycledAtUtc),
                 tracker.CopiedFromTrackerId is { } origin ? Id(origin) : null], cancellationToken);
        }
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        foreach (SnapshotEntity entity in tracker.Entities)
        {
            await InsertAsync(connection, transaction, "tracked_entities",
                ["id", "tracker_id", "source_name", "notes", "filter_active", "shared_notes", "lifecycle_state", "requested_priority", "group_name", "created_at_utc", "schema_updated_at_utc", "progress_updated_at_utc"],
                [Id(entity.Id), Id(tracker.Id),
                 entity.SourceName, entity.Notes, entity.FilterActive, entity.SharedNotes, entity.LifecycleState,
                 entity.RequestedPriority, entity.GroupName,
                 Time(entity.CreatedAtUtc), Time(entity.SchemaUpdatedAtUtc), Time(entity.ProgressUpdatedAtUtc)], cancellationToken);
        }
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        foreach (SnapshotEntity entity in tracker.Entities)
        foreach (SnapshotResponsibilityPeriod period in entity.ResponsibilityPeriods ?? [])
            await InsertAsync(connection, transaction, "responsibility_periods",
                ["id", "entity_id", "developer_id", "started_at_utc", "ended_at_utc"],
                [Id(period.Id), Id(entity.Id), Id(period.DeveloperId),
                 Time(period.StartedAtUtc), TimeOrNull(period.EndedAtUtc)], cancellationToken);
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
            "SELECT recycled_at_utc IS NOT NULL FROM projects WHERE id = $projectId;");
        state.Parameters.AddWithValue("$projectId", id);
        if (Convert.ToInt64(await state.ExecuteScalarAsync(cancellationToken) ?? 0L,
                System.Globalization.CultureInfo.InvariantCulture) != 1)
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
                await InsertAsync(connection, transaction, "entity_dependencies",
                    ["dependent_entity_id", "dependency_entity_id", "created_at_utc", "updated_at_utc"],
                    [Id(entity.Id), Id(dependency.DependencyEntityId), Time(dependency.CreatedAtUtc),
                     Time(dependency.UpdatedAtUtc)], cancellationToken);
        }
        // The status is the history (its previous statuses follow from the events they follow), and the
        // sequence keeps the snapshot's order.
        foreach (SnapshotStatusEvent entry in tracker.StatusHistory.OrderBy(e => e.Order))
            await InsertAsync(connection, transaction, "entity_status_history",
                ["event_id", "entity_id", "sequence", "previous_event_id", "new_status", "entry_kind", "occurred_at_utc"],
                [Id(entry.EventId), Id(entry.EntityId), await NextSequenceAsync("entity_status_history"),
                 entry.PreviousEventId is { } prior ? Id(prior) : null, entry.NewStatus, entry.Kind,
                 Time(entry.OccurredAtUtc)], cancellationToken);
        foreach (SnapshotProgress progress in tracker.ProgressHistory.OrderBy(p => p.Order))
            await InsertAsync(connection, transaction, "progress_snapshots",
                ["tracker_id", "sequence", "recorded_at_utc", "ready_count", "blocked_count", "in_progress_count", "rework_needed_count", "development_completed_count", "reconciled_count", "snapshot_id", "manually_blocked_count", "reworking_count"],
                [Id(tracker.Id), await NextSequenceAsync("progress_snapshots"), Time(progress.RecordedAtUtc), progress.ReadyCount,
                 progress.BlockedCount, progress.InProgressCount, progress.ReworkNeededCount,
                 progress.DevelopmentCompletedCount, progress.ReconciledCount, Id(progress.SnapshotId),
                 progress.ManuallyBlockedCount, progress.ReworkingCount], cancellationToken);
        if (tracker.SyncBaseline is { } baseline)
            await SqliteSyncBaselineRows.WriteAsync(connection, transaction, Id(tracker.Id),
                baseline, cancellationToken);

        async Task<long> NextSequenceAsync(string table)
        {
            using SqliteCommand command = Command(connection, transaction,
                $"SELECT COALESCE(MAX(sequence), 0) + 1 FROM {table};");
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }
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
            WHERE project.name = 'Default project' AND project.id <> $projectId
              AND project.recycled_at_utc IS NULL
              AND (SELECT COUNT(*) FROM projects) = 1
              AND (SELECT COUNT(*) FROM trackers tracker
                   WHERE tracker.project_id = project.id AND tracker.name = 'Default tracker'
                     AND tracker.recycled_at_utc IS NULL
                     AND tracker.copied_from_tracker_id IS NULL) = 1
              AND (SELECT COUNT(*) FROM trackers) = 1
              AND (SELECT COUNT(*) FROM tracked_entities) = 0
              AND (SELECT COUNT(*) FROM entity_status_history) = 0
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
