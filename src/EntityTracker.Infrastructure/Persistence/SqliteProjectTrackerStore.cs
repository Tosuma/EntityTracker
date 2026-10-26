using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using System.Text.Json;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteProjectTrackerStore(
    SqliteDatabase database) : IProjectTrackerStore
{
    public async Task CreateProjectAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects
            (id, name, created_at_utc, updated_at_utc, recycled_at_utc)
            VALUES ($id, $name, $created, $updated, $recycled);
            """;
        AddProjectParameters(command, project);
        await ExecuteConstraintMappedAsync(command, "The project name is already reserved.", cancellationToken);
    }

    public async Task CreateTrackerAsync(
        TrackerCreationState creation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creation);
        Tracker tracker = creation.Tracker;
        TrackedStateChangeSet changeSet = creation.ChangeSet;
        if (changeSet.EntitiesToUpdate.Count > 0 ||
            changeSet.EntityIdsToArchive.Count > 0 ||
            changeSet.EntityIdsToRestore.Count > 0 ||
            changeSet.EntitiesWithProgressToUpdate.Count > 0 ||
            changeSet.EntitiesWithRequestedPriorityToUpdate.Count > 0 ||
            changeSet.EntitiesWithResponsibleDeveloperToUpdate.Count > 0 ||
            changeSet.EntitiesWithGroupNameToUpdate.Count > 0 ||
            changeSet.EntitiesWithDueDateToUpdate.Count > 0)
        {
            throw new ArgumentException("A tracker creation state may only contain initial data.", nameof(creation));
        }

        if (changeSet.EntitiesToAdd.Any(entity => entity.TrackerId != tracker.Id))
        {
            throw new InvalidOperationException("A created entity belongs to another tracker.");
        }

        HashSet<EntityId> entityIds = changeSet.EntitiesToAdd.Select(static entity => entity.Id).ToHashSet();
        if (changeSet.Dependencies.Any(dependency => !entityIds.Contains(dependency.DependentEntityId)))
        {
            throw new InvalidOperationException("Tracker creation contains a cross-tracker relationship.");
        }

        string timestamp = SqlitePersistenceValues.FormatTimestamp(database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            using (SqliteCommand trackerCommand = CreateCommand(connection, transaction, """
                INSERT INTO trackers
                (id, project_id, name, created_at_utc, updated_at_utc, recycled_at_utc,
                 copied_from_tracker_id)
                SELECT $id, $projectId, $name, $created, $updated, $recycled, $copiedFrom
                FROM projects
                WHERE id = $projectId AND recycled_at_utc IS NULL;
                """))
            {
                AddTrackerParameters(trackerCommand, tracker);
                if (await trackerCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("The destination project is not active.");
                }
            }

            foreach (TrackedEntity entity in changeSet.EntitiesToAdd)
            {
                await InsertEntityAsync(connection, transaction, entity, timestamp, cancellationToken);
            }

            foreach (ResponsibilityPeriod period in changeSet.ResponsibilityPeriodsToCopy)
                await SqliteResponsibilityWriter.InsertCopiedPeriodAsync(connection, transaction,
                    tracker.ProjectId, period, cancellationToken);

            foreach (EntityDependency dependency in changeSet.Dependencies)
            {
                using SqliteCommand command = CreateCommand(connection, transaction, """
                    INSERT INTO entity_dependencies
                    (dependent_entity_id, dependency_entity_id, created_at_utc, updated_at_utc)
                    VALUES ($dependentEntityId, $targetId, $timestamp, $timestamp);
                    """);
                SqliteDependencyValues.Add(command, dependency);
                command.Parameters.AddWithValue("$timestamp", timestamp);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertSnapshotAsync(
                connection,
                transaction,
                tracker.Id,
                creation.InitialSnapshot,
                timestamp,
                cancellationToken);

            if (creation.SyncBaseline is { } syncBaseline)
                await SqliteSyncBaselineRows.WriteAsync(connection, transaction,
                    SqlitePersistenceValues.Format(tracker.Id), syncBaseline, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The tracker name is reserved or its initial state is invalid.",
                exception);
        }
    }

    public Task RenameProjectAsync(ProjectId projectId, string name, CancellationToken cancellationToken = default) =>
        UpdateNameAsync("projects", SqlitePersistenceValues.Format(projectId), name, cancellationToken);

    public Task RenameTrackerAsync(TrackerId trackerId, string name, CancellationToken cancellationToken = default) =>
        UpdateNameAsync("trackers", SqlitePersistenceValues.Format(trackerId), name, cancellationToken);

    public Task SetProjectLifecycleAsync(
        ProjectId projectId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default) =>
        SetLifecycleAsync("projects", SqlitePersistenceValues.Format(projectId), lifecycleState, cancellationToken);

    public Task SetTrackerLifecycleAsync(
        TrackerId trackerId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default) =>
        SetLifecycleAsync("trackers", SqlitePersistenceValues.Format(trackerId), lifecycleState, cancellationToken);

    public Task PurgeProjectAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
        PurgeAsync(SqlitePersistenceValues.Format(projectId), true, cancellationToken);

    public Task PurgeTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default) =>
        PurgeAsync(SqlitePersistenceValues.Format(trackerId), false, cancellationToken);

    private async Task UpdateNameAsync(
        string table,
        string id,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {table}
            SET name = $name, updated_at_utc = $timestamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", normalized);
        command.Parameters.AddWithValue("$timestamp", SqlitePersistenceValues.FormatTimestamp(database.TimeProvider.GetUtcNow()));
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The catalog item no longer exists.");
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("The catalog name is already reserved.", exception);
        }
    }

    private async Task SetLifecycleAsync(
        string table,
        string id,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(lifecycleState))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleState));
        }

        string timestamp = SqlitePersistenceValues.FormatTimestamp(database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {table}
            SET recycled_at_utc = CASE WHEN $state = 'Recycled' THEN $timestamp ELSE NULL END,
                updated_at_utc = $timestamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$state", lifecycleState.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The catalog item no longer exists.");
        }
    }

    private async Task PurgeAsync(string id, bool project, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string trackerPredicate = project
            ? "tracker_id IN (SELECT id FROM trackers WHERE project_id = $id)"
            : "tracker_id = $id";
        string entityPredicate = $"entity_id IN (SELECT id FROM tracked_entities WHERE {trackerPredicate})";
        await ExecuteDeleteAsync(connection, transaction, $"DELETE FROM entity_status_history WHERE {entityPredicate};", id, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, $"DELETE FROM progress_snapshots WHERE {trackerPredicate};", id, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, $"DELETE FROM tracked_entities WHERE {trackerPredicate};", id, cancellationToken);
        await ExecuteDeleteAsync(
            connection,
            transaction,
            project ? "DELETE FROM trackers WHERE project_id = $id;" : "DELETE FROM trackers WHERE id = $id;",
            id,
            cancellationToken);
        if (project)
        {
            await ExecuteDeleteAsync(connection, transaction, "DELETE FROM projects WHERE id = $id;", id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
            (id, tracker_id, source_name, notes, filter_active, shared_notes,
             lifecycle_state, requested_priority, due_date, group_name,
             created_at_utc, schema_updated_at_utc, progress_updated_at_utc)
            VALUES
            ($id, $trackerId, $sourceName, $notes, $filterActive, $sharedNotes,
             $lifecycle, $priority, $dueDate, $group,
             $timestamp, $timestamp, $timestamp);
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(entity.TrackerId));
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$filterActive", entity.FilterActive);
        command.Parameters.AddWithValue("$sharedNotes", entity.SharedNotes);
        command.Parameters.AddWithValue("$lifecycle", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue("$priority", entity.RequestedPriority is null ? DBNull.Value : entity.RequestedPriority.Value);
        command.Parameters.AddWithValue("$dueDate", SqlitePersistenceValues.FormatDate(entity.DueDate));
        command.Parameters.AddWithValue("$group", entity.GroupName);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
        // The copy's status starts its history; a placeholder is only an unresolved reference and has none.
        if (!entity.IsPlaceholder)
            await SqliteStatusEvents.AppendAsync(connection, transaction, entity.Id, entity.Status,
                StatusHistoryEntryKind.Baseline, timestamp, cancellationToken);
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
            (tracker_id, sequence, recorded_at_utc, ready_count, blocked_count, in_progress_count,
             rework_needed_count, development_completed_count, reconciled_count, snapshot_id,
             manually_blocked_count, reworking_count)
            VALUES ($trackerId, (SELECT COALESCE(MAX(sequence), 0) + 1 FROM progress_snapshots),
                    $timestamp, $ready, $blocked, $inProgress, $rework,
                    $completed, $reconciled, $snapshotId, $manualBlocked, $reworking);
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$ready", snapshot.ReadyCount);
        command.Parameters.AddWithValue("$blocked", snapshot.BlockedCount);
        command.Parameters.AddWithValue("$inProgress", snapshot.InProgressCount);
        command.Parameters.AddWithValue("$rework", snapshot.ReworkNeededCount);
        command.Parameters.AddWithValue("$completed", snapshot.DevelopmentCompletedCount);
        command.Parameters.AddWithValue("$reconciled", snapshot.ReconciledCount);
        command.Parameters.AddWithValue("$manualBlocked", snapshot.ManuallyBlockedCount);
        command.Parameters.AddWithValue("$reworking", snapshot.ReworkingCount);
        command.Parameters.AddWithValue("$snapshotId", Guid.NewGuid().ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProjectParameters(SqliteCommand command, Project project)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(project.Id));
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(project.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(project.UpdatedAtUtc));
        command.Parameters.AddWithValue("$recycled", project.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(project.RecycledAtUtc.Value));
    }

    private static void AddTrackerParameters(SqliteCommand command, Tracker tracker)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(tracker.Id));
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(tracker.ProjectId));
        command.Parameters.AddWithValue("$name", tracker.Name);
        command.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(tracker.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(tracker.UpdatedAtUtc));
        command.Parameters.AddWithValue("$recycled", tracker.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(tracker.RecycledAtUtc.Value));
        command.Parameters.AddWithValue("$copiedFrom", tracker.CopiedFromTrackerId is null ? DBNull.Value : SqlitePersistenceValues.Format(tracker.CopiedFromTrackerId));
    }


    private static async Task ExecuteConstraintMappedAsync(
        SqliteCommand command,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(message, exception);
        }
    }

    private static async Task ExecuteDeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        string id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
