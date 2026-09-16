using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
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
            (id, name_key, name, lifecycle_state, created_at_utc, updated_at_utc, recycled_at_utc)
            VALUES ($id, $nameKey, $name, $lifecycle, $created, $updated, $recycled);
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
            changeSet.EntitiesWithGroupNameToUpdate.Count > 0)
        {
            throw new ArgumentException("A tracker creation state may only contain initial data.", nameof(creation));
        }

        if (changeSet.EntitiesToAdd.Any(entity => entity.TrackerId != tracker.Id))
        {
            throw new InvalidOperationException("A created entity belongs to another tracker.");
        }

        HashSet<EntityId> entityIds = changeSet.EntitiesToAdd.Select(static entity => entity.Id).ToHashSet();
        if (changeSet.ResolvedDependencies.Any(dependency =>
                !entityIds.Contains(dependency.Edge.DependentEntityId) ||
                !entityIds.Contains(dependency.Edge.DependencyEntityId)) ||
            changeSet.UnresolvedDependencies.Any(dependency =>
                !entityIds.Contains(dependency.Dependency.DependentEntityId)) ||
            changeSet.ManualDependencyOverrides.Any(item =>
                !entityIds.Contains(item.DependentEntityId)))
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
                (id, project_id, name_key, name, lifecycle_state, created_at_utc,
                 updated_at_utc, recycled_at_utc, copied_from_tracker_id)
                SELECT $id, $projectId, $nameKey, $name, $lifecycle, $created,
                       $updated, $recycled, $copiedFrom
                FROM projects
                WHERE id = $projectId AND lifecycle_state = 'Active';
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

            foreach (PersistedDependency dependency in changeSet.ResolvedDependencies)
            {
                using SqliteCommand command = CreateCommand(connection, transaction, """
                    INSERT INTO schema_dependencies
                    (dependent_entity_id, dependency_entity_id, dependency_kind, created_at_utc, updated_at_utc)
                    VALUES ($owner, $target, $kind, $timestamp, $timestamp);
                    """);
                command.Parameters.AddWithValue("$owner", SqlitePersistenceValues.Format(dependency.Edge.DependentEntityId));
                command.Parameters.AddWithValue("$target", SqlitePersistenceValues.Format(dependency.Edge.DependencyEntityId));
                command.Parameters.AddWithValue("$kind", dependency.Kind.ToString());
                command.Parameters.AddWithValue("$timestamp", timestamp);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (PersistedUnresolvedDependency dependency in changeSet.UnresolvedDependencies)
            {
                using SqliteCommand command = CreateCommand(connection, transaction, """
                    INSERT INTO unresolved_schema_dependencies
                    (dependent_entity_id, dependency_source_key, dependency_source_name,
                     dependency_kind, created_at_utc, updated_at_utc)
                    VALUES ($owner, $key, $name, $kind, $timestamp, $timestamp);
                    """);
                command.Parameters.AddWithValue("$owner", SqlitePersistenceValues.Format(dependency.Dependency.DependentEntityId));
                command.Parameters.AddWithValue("$key", EntitySourceKey.From(dependency.Dependency.DependencySourceName).Value);
                command.Parameters.AddWithValue("$name", dependency.Dependency.DependencySourceName);
                command.Parameters.AddWithValue("$kind", dependency.Kind.ToString());
                command.Parameters.AddWithValue("$timestamp", timestamp);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (ManualDependencyOverride dependencyOverride in changeSet.ManualDependencyOverrides)
            {
                using SqliteCommand command = CreateCommand(connection, transaction, """
                    INSERT INTO manual_dependency_overrides
                    (dependent_entity_id, dependency_source_key, dependency_source_name,
                     override_action, created_at_utc, updated_at_utc)
                    VALUES ($owner, $key, $name, $action, $timestamp, $timestamp);
                    """);
                command.Parameters.AddWithValue("$owner", SqlitePersistenceValues.Format(dependencyOverride.DependentEntityId));
                command.Parameters.AddWithValue("$key", EntitySourceKey.From(dependencyOverride.DependencySourceName).Value);
                command.Parameters.AddWithValue("$name", dependencyOverride.DependencySourceName);
                command.Parameters.AddWithValue("$action", dependencyOverride.Action.ToString());
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

            if (creation.ImportCompletion is { } completion)
            {
                await InsertImportSummaryAsync(
                    connection,
                    transaction,
                    tracker.Id,
                    completion,
                    timestamp,
                    cancellationToken);
            }

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
            SET name_key = $nameKey, name = $name, updated_at_utc = $timestamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", normalized);
        command.Parameters.AddWithValue("$nameKey", NormalizeNameKey(normalized));
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
            SET lifecycle_state = $state,
                recycled_at_utc = CASE WHEN $state = 'Recycled' THEN $timestamp ELSE NULL END,
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
        await ExecuteDeleteAsync(connection, transaction, $"DELETE FROM schema_import_summary WHERE {trackerPredicate};", id, cancellationToken);
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
            (id, tracker_id, source_key, source_name, development_status, notes,
             lifecycle_state, provenance, requested_priority, responsible_developer, group_name,
             created_at_utc, schema_updated_at_utc, progress_updated_at_utc)
            VALUES
            ($id, $trackerId, $sourceKey, $sourceName, $status, $notes,
             $lifecycle, $provenance, $priority, $developer, $group,
             $timestamp, $timestamp, $timestamp);

            INSERT INTO entity_status_history
            (entity_id, previous_status, new_status, entry_kind, occurred_at_utc)
            VALUES ($id, NULL, $status, 'Baseline', $timestamp);
            """);
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(entity.TrackerId));
        command.Parameters.AddWithValue("$sourceKey", EntitySourceKey.From(entity.SourceName).Value);
        command.Parameters.AddWithValue("$sourceName", entity.SourceName);
        command.Parameters.AddWithValue("$status", entity.Status.ToString());
        command.Parameters.AddWithValue("$notes", entity.Notes);
        command.Parameters.AddWithValue("$lifecycle", entity.LifecycleState.ToString());
        command.Parameters.AddWithValue("$provenance", entity.Provenance.ToString());
        command.Parameters.AddWithValue("$priority", entity.RequestedPriority is null ? DBNull.Value : entity.RequestedPriority.Value);
        command.Parameters.AddWithValue("$developer", entity.ResponsibleDeveloper);
        command.Parameters.AddWithValue("$group", entity.GroupName);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            (tracker_id, recorded_at_utc, ready_count, blocked_count, in_progress_count,
             rework_needed_count, development_completed_count, reconciled_count)
            VALUES ($trackerId, $timestamp, $ready, $blocked, $inProgress, $rework, $completed, $reconciled);
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$ready", snapshot.ReadyCount);
        command.Parameters.AddWithValue("$blocked", snapshot.BlockedCount);
        command.Parameters.AddWithValue("$inProgress", snapshot.InProgressCount);
        command.Parameters.AddWithValue("$rework", snapshot.ReworkNeededCount);
        command.Parameters.AddWithValue("$completed", snapshot.DevelopmentCompletedCount);
        command.Parameters.AddWithValue("$reconciled", snapshot.ReconciledCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertImportSummaryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TrackerId trackerId,
        SchemaImportCompletion completion,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO schema_import_summary
            (tracker_id, applied_at_utc, source_file_name, import_mode, new_entity_count,
             changed_entity_count, archived_entity_count, unchanged_entity_count,
             unresolved_entity_count)
            VALUES ($trackerId, $timestamp, $file, $mode, $new, $changed, $archived, $unchanged, $unresolved);
            """);
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        command.Parameters.AddWithValue("$timestamp", timestamp);
        command.Parameters.AddWithValue("$file", completion.SourceFileName);
        command.Parameters.AddWithValue("$mode", completion.Mode.ToString());
        command.Parameters.AddWithValue("$new", completion.NewEntityCount);
        command.Parameters.AddWithValue("$changed", completion.ChangedEntityCount);
        command.Parameters.AddWithValue("$archived", completion.ArchivedEntityCount);
        command.Parameters.AddWithValue("$unchanged", completion.UnchangedEntityCount);
        command.Parameters.AddWithValue("$unresolved", completion.UnresolvedEntityCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProjectParameters(SqliteCommand command, Project project)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(project.Id));
        command.Parameters.AddWithValue("$nameKey", NormalizeNameKey(project.Name));
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$lifecycle", project.LifecycleState.ToString());
        command.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(project.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(project.UpdatedAtUtc));
        command.Parameters.AddWithValue("$recycled", project.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(project.RecycledAtUtc.Value));
    }

    private static void AddTrackerParameters(SqliteCommand command, Tracker tracker)
    {
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(tracker.Id));
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(tracker.ProjectId));
        command.Parameters.AddWithValue("$nameKey", NormalizeNameKey(tracker.Name));
        command.Parameters.AddWithValue("$name", tracker.Name);
        command.Parameters.AddWithValue("$lifecycle", tracker.LifecycleState.ToString());
        command.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(tracker.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(tracker.UpdatedAtUtc));
        command.Parameters.AddWithValue("$recycled", tracker.RecycledAtUtc is null ? DBNull.Value : SqlitePersistenceValues.FormatTimestamp(tracker.RecycledAtUtc.Value));
        command.Parameters.AddWithValue("$copiedFrom", tracker.CopiedFromTrackerId is null ? DBNull.Value : SqlitePersistenceValues.Format(tracker.CopiedFromTrackerId));
    }

    private static string NormalizeNameKey(string name) => name.Trim().ToUpperInvariant();

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
