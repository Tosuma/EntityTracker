using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class ProjectTrackerMigrationTests
{
    private static readonly EntityId ActiveId = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly EntityId ArchivedId = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public async Task InitializeAsync_EmptyDatabaseCreatesSoleDefaultProjectAndTracker()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();

        Project project = Assert.Single(await new SqliteProjectRepository(database).GetAllAsync());
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        Assert.Equal("Default project", project.Name);
        Assert.Equal(CatalogLifecycleState.Active, project.LifecycleState);
        Assert.Equal(project.Id, tracker.ProjectId);
        Assert.Equal("Default tracker", tracker.Name);
        Assert.Equal(CatalogLifecycleState.Active, tracker.LifecycleState);
        Assert.Null(tracker.CopiedFromTrackerId);
    }

    [Fact]
    public async Task InitializeAsync_VersionElevenPreservesCompleteStateInDefaultTracker()
    {
        await using TemporarySqliteFile file = new();
        await CreatePopulatedVersionElevenDatabaseAsync(file.DatabasePath);
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();

        Project project = Assert.Single(await new SqliteProjectRepository(database).GetAllAsync());
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        Assert.Equal(project.Id, tracker.ProjectId);
        SqliteEntityRepository entities = new(database);
        TrackedEntity[] migrated = (await entities.GetAllAsync(tracker.Id)).ToArray();
        Assert.Equal(2, migrated.Length);
        TrackedEntity active = Assert.Single(migrated, entity => entity.Id == ActiveId);
        Assert.Equal(tracker.Id, active.TrackerId);
        Assert.Equal("Active Entity", active.SourceName);
        Assert.Equal(DevelopmentStatus.InProgress, active.Status);
        Assert.Equal("Preserved notes", active.Notes);
        Assert.Equal(EntityLifecycleState.Active, active.LifecycleState);
        Assert.Equal(EntityProvenance.ManualAndImported, active.Provenance);
        Assert.Equal(4, active.RequestedPriority);
        Assert.Equal("Ada", active.ResponsibleDeveloper);
        Assert.Equal("Core", active.GroupName);
        TrackedEntity archived = Assert.Single(migrated, entity => entity.Id == ArchivedId);
        Assert.Equal(EntityLifecycleState.Archived, archived.LifecycleState);
        Assert.Equal(DevelopmentStatus.Reconciled, archived.Status);

        PersistedDependency resolved = Assert.Single(
            await new SqliteDependencyRepository(database).GetAllAsync(tracker.Id));
        Assert.Equal(ActiveId, resolved.Edge.DependentEntityId);
        Assert.Equal(ArchivedId, resolved.Edge.DependencyEntityId);
        Assert.Equal(ImportedDependencyKind.Optional, resolved.Kind);
        PersistedUnresolvedDependency unresolved = Assert.Single(
            await new SqliteDependencyRepository(database).GetAllUnresolvedAsync(tracker.Id));
        Assert.Equal(ActiveId, unresolved.Dependency.DependentEntityId);
        Assert.Equal("Missing Entity", unresolved.Dependency.DependencySourceName);
        Assert.Equal(ImportedDependencyKind.Mandatory, unresolved.Kind);
        ManualDependencyOverride dependencyOverride = Assert.Single(
            await new SqliteManualDependencyOverrideRepository(database).GetAllAsync(tracker.Id));
        Assert.Equal(ActiveId, dependencyOverride.DependentEntityId);
        Assert.Equal("Missing Entity", dependencyOverride.DependencySourceName);
        Assert.Equal(ManualDependencyOverrideAction.Suppress, dependencyOverride.Action);

        SqliteProgressHistoryRepository history = new(database);
        IReadOnlyList<EntityStatusHistoryEntry> entries = await history.GetStatusHistoryAsync(tracker.Id);
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityId == ActiveId &&
            entry.PreviousStatus == DevelopmentStatus.NotStarted &&
            entry.NewStatus == DevelopmentStatus.InProgress &&
            entry.Kind == StatusHistoryEntryKind.Transition);
        ProgressSnapshot snapshot = Assert.Single(await history.GetProgressSnapshotsAsync(tracker.Id));
        Assert.Equal(new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero), snapshot.RecordedAtUtc);
        Assert.Equal(2, snapshot.State.ReadyCount);
        Assert.Equal(3, snapshot.State.BlockedCount);
        Assert.Equal(4, snapshot.State.InProgressCount);
        Assert.Equal(5, snapshot.State.ReworkNeededCount);
        Assert.Equal(6, snapshot.State.DevelopmentCompletedCount);
        Assert.Equal(7, snapshot.State.ReconciledCount);
        SchemaImportSummary summary = Assert.IsType<SchemaImportSummary>(
            await new SqliteTrackedStateStore(database).GetLatestImportAsync(tracker.Id));
        Assert.Equal(new DateTimeOffset(2025, 1, 7, 0, 0, 0, TimeSpan.Zero), summary.AppliedAtUtc);
        Assert.Equal("preserved.csv", summary.SourceFileName);
        Assert.Equal(SchemaImportMode.Partial, summary.Mode);
        Assert.Equal(1, summary.NewEntityCount);
        Assert.Equal(2, summary.ChangedEntityCount);
        Assert.Equal(3, summary.ArchivedEntityCount);
        Assert.Equal(4, summary.UnchangedEntityCount);
        Assert.Equal(5, summary.UnresolvedEntityCount);

        await using SqliteConnection connection = await OpenAsync(file.DatabasePath);
        Assert.Equal(14L, await ScalarInt64Async(connection, "PRAGMA user_version;"));
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_key, created_at_utc, schema_updated_at_utc, progress_updated_at_utc
            FROM tracked_entities
            WHERE id = $id AND tracker_id = $trackerId;
            """;
        command.Parameters.AddWithValue("$id", ActiveId.Value.ToString("D"));
        command.Parameters.AddWithValue("$trackerId", tracker.Id.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ACTIVE ENTITY", reader.GetString(0));
        Assert.Equal("2025-01-01T00:00:00.0000000+00:00", reader.GetString(1));
        Assert.Equal("2025-01-02T00:00:00.0000000+00:00", reader.GetString(2));
        Assert.Equal("2025-01-03T00:00:00.0000000+00:00", reader.GetString(3));
    }

    private static async Task CreatePopulatedVersionElevenDatabaseAsync(string databasePath)
    {
        await using SqliteConnection connection = await OpenAsync(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE tracked_entities
            (
                id TEXT NOT NULL PRIMARY KEY,
                source_key TEXT NOT NULL UNIQUE,
                source_name TEXT NOT NULL,
                development_status TEXT NOT NULL,
                notes TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                schema_updated_at_utc TEXT NOT NULL,
                progress_updated_at_utc TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL,
                provenance TEXT NOT NULL,
                requested_priority INTEGER NULL,
                responsible_developer TEXT NOT NULL,
                group_name TEXT NOT NULL
            );

            CREATE TABLE schema_dependencies
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_entity_id TEXT NOT NULL,
                dependency_kind TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_entity_id),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE,
                FOREIGN KEY (dependency_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );

            CREATE TABLE unresolved_schema_dependencies
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_source_key TEXT NOT NULL,
                dependency_source_name TEXT NOT NULL,
                dependency_kind TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_source_key),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );

            CREATE TABLE manual_dependency_overrides
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_source_key TEXT NOT NULL,
                dependency_source_name TEXT NOT NULL,
                override_action TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_source_key),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );

            CREATE TABLE entity_status_history
            (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                entity_id TEXT NOT NULL,
                previous_status TEXT NULL,
                new_status TEXT NOT NULL,
                entry_kind TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT
            );

            CREATE TABLE progress_snapshots
            (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                recorded_at_utc TEXT NOT NULL,
                ready_count INTEGER NOT NULL,
                blocked_count INTEGER NOT NULL,
                in_progress_count INTEGER NOT NULL,
                rework_needed_count INTEGER NOT NULL,
                development_completed_count INTEGER NOT NULL,
                reconciled_count INTEGER NOT NULL
            );

            CREATE TABLE schema_import_summary
            (
                singleton_id INTEGER NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL,
                source_file_name TEXT NOT NULL,
                import_mode TEXT NOT NULL,
                new_entity_count INTEGER NOT NULL,
                changed_entity_count INTEGER NOT NULL,
                archived_entity_count INTEGER NOT NULL,
                unchanged_entity_count INTEGER NOT NULL,
                unresolved_entity_count INTEGER NOT NULL
            );

            INSERT INTO tracked_entities VALUES
            ($activeId, 'ACTIVE ENTITY', 'Active Entity', 'InProgress', 'Preserved notes',
             '2025-01-01T00:00:00.0000000+00:00', '2025-01-02T00:00:00.0000000+00:00',
             '2025-01-03T00:00:00.0000000+00:00', 'Active', 'ManualAndImported', 4, 'Ada', 'Core'),
            ($archivedId, 'ARCHIVED ENTITY', 'Archived Entity', 'Reconciled', 'Archived notes',
             '2025-01-01T00:00:00.0000000+00:00', '2025-01-02T00:00:00.0000000+00:00',
             '2025-01-03T00:00:00.0000000+00:00', 'Archived', 'Imported', 2, 'Grace', 'Legacy');

            INSERT INTO schema_dependencies VALUES
            ($activeId, $archivedId, 'Optional',
             '2025-01-02T00:00:00.0000000+00:00', '2025-01-03T00:00:00.0000000+00:00');
            INSERT INTO unresolved_schema_dependencies VALUES
            ($activeId, 'MISSING ENTITY', 'Missing Entity', 'Mandatory',
             '2025-01-02T00:00:00.0000000+00:00', '2025-01-03T00:00:00.0000000+00:00');
            INSERT INTO manual_dependency_overrides VALUES
            ($activeId, 'MISSING ENTITY', 'Missing Entity', 'Suppress',
             '2025-01-02T00:00:00.0000000+00:00', '2025-01-03T00:00:00.0000000+00:00');

            INSERT INTO entity_status_history
            (entity_id, previous_status, new_status, entry_kind, occurred_at_utc)
            VALUES
            ($activeId, NULL, 'NotStarted', 'Baseline', '2025-01-04T00:00:00.0000000+00:00'),
            ($activeId, 'NotStarted', 'InProgress', 'Transition', '2025-01-05T00:00:00.0000000+00:00'),
            ($archivedId, NULL, 'Reconciled', 'Baseline', '2025-01-04T00:00:00.0000000+00:00');
            INSERT INTO progress_snapshots
            (recorded_at_utc, ready_count, blocked_count, in_progress_count,
             rework_needed_count, development_completed_count, reconciled_count)
            VALUES ('2025-01-06T00:00:00.0000000+00:00', 2, 3, 4, 5, 6, 7);
            INSERT INTO schema_import_summary VALUES
            (1, '2025-01-07T00:00:00.0000000+00:00', 'preserved.csv', 'Partial', 1, 2, 3, 4, 5);

            PRAGMA user_version = 11;
            """;
        command.Parameters.AddWithValue("$activeId", ActiveId.Value.ToString("D"));
        command.Parameters.AddWithValue("$archivedId", ArchivedId.Value.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
