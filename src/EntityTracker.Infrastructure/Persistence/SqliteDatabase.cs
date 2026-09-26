using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.Sqlite;
using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    internal const int CurrentSchemaVersion = 15;

    private const string ProjectOperationOutboxSchemaSql = """
        CREATE TABLE IF NOT EXISTS project_operation_outbox
        (
            sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            project_id TEXT NOT NULL,
            operation_id TEXT NOT NULL UNIQUE
                CHECK (length(operation_id) = 36 AND operation_id = lower(operation_id)),
            mutation_type TEXT NOT NULL,
            mutation_json TEXT NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            committed_git_object_id TEXT NULL,
            committed_at_utc TEXT NULL,
            CHECK ((committed_git_object_id IS NULL) = (committed_at_utc IS NULL))
        );

        CREATE INDEX IF NOT EXISTS ix_project_operation_outbox_pending
            ON project_operation_outbox (project_id, committed_git_object_id, sequence);
        """;

    private const string InitialSchemaSql = """
        CREATE TABLE tracked_entities
        (
            id TEXT NOT NULL PRIMARY KEY,
            source_key TEXT NOT NULL UNIQUE,
            source_name TEXT NOT NULL,
            development_status TEXT NOT NULL
                CHECK (development_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            notes TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            schema_updated_at_utc TEXT NOT NULL,
            progress_updated_at_utc TEXT NOT NULL
        );

        CREATE TABLE schema_dependencies
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_entity_id TEXT NOT NULL,
            dependency_kind TEXT NOT NULL
                CHECK (dependency_kind IN ('Mandatory', 'Optional')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_entity_id),
            CHECK (dependent_entity_id <> dependency_entity_id),
            FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE,
            FOREIGN KEY (dependency_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
        );

        CREATE INDEX ix_schema_dependencies_dependency_entity_id
            ON schema_dependencies (dependency_entity_id);
        """;

    private const string WorkflowStatusSchemaSql = """
        CREATE TABLE tracked_entities_v7
        (
            id TEXT NOT NULL PRIMARY KEY,
            source_key TEXT NOT NULL UNIQUE,
            source_name TEXT NOT NULL,
            development_status TEXT NOT NULL
                CHECK (development_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            notes TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            schema_updated_at_utc TEXT NOT NULL,
            progress_updated_at_utc TEXT NOT NULL,
            lifecycle_state TEXT NOT NULL DEFAULT 'Active'
                CHECK (lifecycle_state IN ('Active', 'Archived')),
            provenance TEXT NOT NULL DEFAULT 'Imported'
                CHECK (provenance IN ('Imported', 'ManualOnly', 'ManualAndImported'))
        );

        INSERT INTO tracked_entities_v7
        (
            id, source_key, source_name, development_status, notes,
            created_at_utc, schema_updated_at_utc, progress_updated_at_utc,
            lifecycle_state, provenance
        )
        SELECT id,
               source_key,
               source_name,
               CASE development_status
                   WHEN 'Completed' THEN 'DevelopmentCompleted'
                   ELSE development_status
               END,
               notes,
               created_at_utc,
               schema_updated_at_utc,
               progress_updated_at_utc,
               lifecycle_state,
               provenance
        FROM tracked_entities;

        DROP TABLE tracked_entities;
        ALTER TABLE tracked_entities_v7 RENAME TO tracked_entities;
        """;

    private const string ProgressHistorySchemaSql = """
        CREATE TABLE entity_status_history
        (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entity_id TEXT NOT NULL,
            previous_status TEXT NULL
                CHECK (previous_status IS NULL OR previous_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            new_status TEXT NOT NULL
                CHECK (new_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            entry_kind TEXT NOT NULL
                CHECK (entry_kind IN ('Baseline', 'Created', 'Transition')),
            occurred_at_utc TEXT NOT NULL,
            CHECK (
                (entry_kind = 'Transition' AND previous_status IS NOT NULL) OR
                (entry_kind IN ('Baseline', 'Created') AND previous_status IS NULL)),
            FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT
        );

        CREATE INDEX ix_entity_status_history_entity_time
            ON entity_status_history (entity_id, occurred_at_utc, id);
        CREATE INDEX ix_entity_status_history_time
            ON entity_status_history (occurred_at_utc, id);

        CREATE TABLE progress_snapshots
        (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            recorded_at_utc TEXT NOT NULL,
            ready_count INTEGER NOT NULL CHECK (ready_count >= 0),
            blocked_count INTEGER NOT NULL CHECK (blocked_count >= 0),
            in_progress_count INTEGER NOT NULL CHECK (in_progress_count >= 0),
            rework_needed_count INTEGER NOT NULL CHECK (rework_needed_count >= 0),
            development_completed_count INTEGER NOT NULL CHECK (development_completed_count >= 0),
            reconciled_count INTEGER NOT NULL CHECK (reconciled_count >= 0)
        );

        CREATE INDEX ix_progress_snapshots_time
            ON progress_snapshots (recorded_at_utc, id);
        """;

    private const string OperationHistorySchemaSql = """
        CREATE TABLE entity_status_history_v13
        (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            operation_id TEXT NOT NULL
                CHECK (length(operation_id) = 36 AND operation_id = lower(operation_id)),
            entity_id TEXT NOT NULL,
            previous_status TEXT NULL
                CHECK (previous_status IS NULL OR previous_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            new_status TEXT NOT NULL
                CHECK (new_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            entry_kind TEXT NOT NULL
                CHECK (entry_kind IN ('Baseline', 'Created', 'Transition')),
            occurred_at_utc TEXT NOT NULL,
            CHECK (
                (entry_kind = 'Transition' AND previous_status IS NOT NULL) OR
                (entry_kind IN ('Baseline', 'Created') AND previous_status IS NULL)),
            FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT
        );

        INSERT INTO entity_status_history_v13
        (id, operation_id, entity_id, previous_status, new_status, entry_kind, occurred_at_utc)
        SELECT id, operation_id, entity_id, previous_status, new_status, entry_kind, occurred_at_utc
        FROM entity_status_history;

        DROP TABLE entity_status_history;
        ALTER TABLE entity_status_history_v13 RENAME TO entity_status_history;

        CREATE INDEX ix_entity_status_history_entity_time
            ON entity_status_history (entity_id, occurred_at_utc, id);
        CREATE INDEX ix_entity_status_history_time
            ON entity_status_history (occurred_at_utc, id);
        CREATE INDEX ix_entity_status_history_operation
            ON entity_status_history (operation_id, occurred_at_utc, id);
        """;

    private const string OperationProjectionHistorySchemaSql = """
        CREATE TABLE progress_snapshots_v14
        (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            operation_id TEXT NOT NULL
                CHECK (length(operation_id) = 36 AND operation_id = lower(operation_id)),
            tracker_id TEXT NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            ready_count INTEGER NOT NULL CHECK (ready_count >= 0),
            blocked_count INTEGER NOT NULL CHECK (blocked_count >= 0),
            in_progress_count INTEGER NOT NULL CHECK (in_progress_count >= 0),
            rework_needed_count INTEGER NOT NULL CHECK (rework_needed_count >= 0),
            development_completed_count INTEGER NOT NULL CHECK (development_completed_count >= 0),
            reconciled_count INTEGER NOT NULL CHECK (reconciled_count >= 0),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );

        INSERT INTO progress_snapshots_v14
        (id, operation_id, tracker_id, recorded_at_utc, ready_count, blocked_count,
         in_progress_count, rework_needed_count, development_completed_count, reconciled_count)
        SELECT id, operation_id, tracker_id, recorded_at_utc, ready_count, blocked_count,
               in_progress_count, rework_needed_count, development_completed_count, reconciled_count
        FROM progress_snapshots;

        DROP TABLE progress_snapshots;
        ALTER TABLE progress_snapshots_v14 RENAME TO progress_snapshots;
        CREATE INDEX ix_progress_snapshots_tracker_time
            ON progress_snapshots (tracker_id, recorded_at_utc, operation_id, id);
        CREATE INDEX ix_progress_snapshots_operation
            ON progress_snapshots (operation_id, tracker_id);

        CREATE TABLE schema_import_summary_v14
        (
            tracker_id TEXT NOT NULL PRIMARY KEY,
            operation_id TEXT NOT NULL
                CHECK (length(operation_id) = 36 AND operation_id = lower(operation_id)),
            applied_at_utc TEXT NOT NULL,
            source_file_name TEXT NOT NULL CHECK (length(trim(source_file_name)) > 0),
            import_mode TEXT NOT NULL CHECK (import_mode IN ('Complete', 'Partial')),
            new_entity_count INTEGER NOT NULL CHECK (new_entity_count >= 0),
            changed_entity_count INTEGER NOT NULL CHECK (changed_entity_count >= 0),
            archived_entity_count INTEGER NOT NULL CHECK (archived_entity_count >= 0),
            unchanged_entity_count INTEGER NOT NULL CHECK (unchanged_entity_count >= 0),
            unresolved_entity_count INTEGER NOT NULL CHECK (unresolved_entity_count >= 0),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );

        INSERT INTO schema_import_summary_v14
        (tracker_id, operation_id, applied_at_utc, source_file_name, import_mode,
         new_entity_count, changed_entity_count, archived_entity_count,
         unchanged_entity_count, unresolved_entity_count)
        SELECT tracker_id, operation_id, applied_at_utc, source_file_name, import_mode,
               new_entity_count, changed_entity_count, archived_entity_count,
               unchanged_entity_count, unresolved_entity_count
        FROM schema_import_summary;

        DROP TABLE schema_import_summary;
        ALTER TABLE schema_import_summary_v14 RENAME TO schema_import_summary;
        """;

    private const string SchemaImportSummarySql = """
        CREATE TABLE schema_import_summary
        (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            applied_at_utc TEXT NOT NULL,
            source_file_name TEXT NOT NULL CHECK (length(trim(source_file_name)) > 0),
            import_mode TEXT NOT NULL CHECK (import_mode IN ('Complete', 'Partial')),
            new_entity_count INTEGER NOT NULL CHECK (new_entity_count >= 0),
            changed_entity_count INTEGER NOT NULL CHECK (changed_entity_count >= 0),
            archived_entity_count INTEGER NOT NULL CHECK (archived_entity_count >= 0),
            unchanged_entity_count INTEGER NOT NULL CHECK (unchanged_entity_count >= 0),
            unresolved_entity_count INTEGER NOT NULL CHECK (unresolved_entity_count >= 0)
        );
        """;

    private const string TrackerSchemaImportSummarySql = """
        CREATE TABLE schema_import_summary
        (
            tracker_id TEXT NOT NULL PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            source_file_name TEXT NOT NULL CHECK (length(trim(source_file_name)) > 0),
            import_mode TEXT NOT NULL CHECK (import_mode IN ('Complete', 'Partial')),
            new_entity_count INTEGER NOT NULL CHECK (new_entity_count >= 0),
            changed_entity_count INTEGER NOT NULL CHECK (changed_entity_count >= 0),
            archived_entity_count INTEGER NOT NULL CHECK (archived_entity_count >= 0),
            unchanged_entity_count INTEGER NOT NULL CHECK (unchanged_entity_count >= 0),
            unresolved_entity_count INTEGER NOT NULL CHECK (unresolved_entity_count >= 0),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );
        """;

    private const string RequestedPrioritySchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN requested_priority INTEGER NULL
                CHECK (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5);
        """;

    private const string ResponsibleDeveloperSchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN responsible_developer TEXT NOT NULL DEFAULT '';
        """;

    private const string GroupNameSchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN group_name TEXT NOT NULL DEFAULT '';
        """;

    private const string ProjectTrackerSchemaSql = """
        CREATE TABLE projects
        (
            id TEXT NOT NULL PRIMARY KEY,
            name_key TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL CHECK (length(trim(name)) > 0),
            lifecycle_state TEXT NOT NULL
                CHECK (lifecycle_state IN ('Active', 'Recycled')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            recycled_at_utc TEXT NULL,
            CHECK ((lifecycle_state = 'Recycled') = (recycled_at_utc IS NOT NULL))
        );

        CREATE TABLE trackers
        (
            id TEXT NOT NULL PRIMARY KEY,
            project_id TEXT NOT NULL,
            name_key TEXT NOT NULL,
            name TEXT NOT NULL CHECK (length(trim(name)) > 0),
            lifecycle_state TEXT NOT NULL
                CHECK (lifecycle_state IN ('Active', 'Recycled')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            recycled_at_utc TEXT NULL,
            copied_from_tracker_id TEXT NULL,
            UNIQUE (project_id, name_key),
            CHECK ((lifecycle_state = 'Recycled') = (recycled_at_utc IS NOT NULL)),
            FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE RESTRICT,
            FOREIGN KEY (copied_from_tracker_id) REFERENCES trackers (id) ON DELETE SET NULL
        );

        CREATE INDEX ix_trackers_project_lifecycle
            ON trackers (project_id, lifecycle_state, name_key);

        INSERT INTO projects
        (
            id, name_key, name, lifecycle_state,
            created_at_utc, updated_at_utc, recycled_at_utc
        )
        VALUES
        (
            $defaultProjectId, $defaultProjectNameKey, $defaultProjectName, 'Active',
            $migrationTimestamp, $migrationTimestamp, NULL
        );

        INSERT INTO trackers
        (
            id, project_id, name_key, name, lifecycle_state,
            created_at_utc, updated_at_utc, recycled_at_utc, copied_from_tracker_id
        )
        VALUES
        (
            $defaultTrackerId, $defaultProjectId, $defaultTrackerNameKey,
            $defaultTrackerName, 'Active',
            $migrationTimestamp, $migrationTimestamp, NULL, NULL
        );

        CREATE TABLE tracked_entities_v12
        (
            id TEXT NOT NULL PRIMARY KEY,
            tracker_id TEXT NOT NULL,
            source_key TEXT NOT NULL,
            source_name TEXT NOT NULL,
            development_status TEXT NOT NULL
                CHECK (development_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled')),
            notes TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            schema_updated_at_utc TEXT NOT NULL,
            progress_updated_at_utc TEXT NOT NULL,
            lifecycle_state TEXT NOT NULL
                CHECK (lifecycle_state IN ('Active', 'Archived')),
            provenance TEXT NOT NULL
                CHECK (provenance IN
                    ('Imported', 'ManualOnly', 'ManualAndImported', 'Copied', 'CopiedAndImported')),
            requested_priority INTEGER NULL
                CHECK (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5),
            responsible_developer TEXT NOT NULL,
            group_name TEXT NOT NULL,
            UNIQUE (tracker_id, source_key),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE RESTRICT
        );

        INSERT INTO tracked_entities_v12
        (
            id, tracker_id, source_key, source_name, development_status, notes,
            created_at_utc, schema_updated_at_utc, progress_updated_at_utc,
            lifecycle_state, provenance, requested_priority,
            responsible_developer, group_name
        )
        SELECT id, $defaultTrackerId, source_key, source_name, development_status, notes,
               created_at_utc, schema_updated_at_utc, progress_updated_at_utc,
               lifecycle_state, provenance, requested_priority,
               responsible_developer, group_name
        FROM tracked_entities;

        DROP TABLE tracked_entities;
        ALTER TABLE tracked_entities_v12 RENAME TO tracked_entities;
        CREATE INDEX ix_tracked_entities_tracker_lifecycle
            ON tracked_entities (tracker_id, lifecycle_state, source_key);

        CREATE TABLE progress_snapshots_v12
        (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            tracker_id TEXT NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            ready_count INTEGER NOT NULL CHECK (ready_count >= 0),
            blocked_count INTEGER NOT NULL CHECK (blocked_count >= 0),
            in_progress_count INTEGER NOT NULL CHECK (in_progress_count >= 0),
            rework_needed_count INTEGER NOT NULL CHECK (rework_needed_count >= 0),
            development_completed_count INTEGER NOT NULL CHECK (development_completed_count >= 0),
            reconciled_count INTEGER NOT NULL CHECK (reconciled_count >= 0),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );

        INSERT INTO progress_snapshots_v12
        (
            id, tracker_id, recorded_at_utc, ready_count, blocked_count,
            in_progress_count, rework_needed_count, development_completed_count,
            reconciled_count
        )
        SELECT id, $defaultTrackerId, recorded_at_utc, ready_count, blocked_count,
               in_progress_count, rework_needed_count, development_completed_count,
               reconciled_count
        FROM progress_snapshots;

        DROP TABLE progress_snapshots;
        ALTER TABLE progress_snapshots_v12 RENAME TO progress_snapshots;
        CREATE INDEX ix_progress_snapshots_tracker_time
            ON progress_snapshots (tracker_id, recorded_at_utc, id);

        CREATE TABLE schema_import_summary_v12
        (
            tracker_id TEXT NOT NULL PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            source_file_name TEXT NOT NULL CHECK (length(trim(source_file_name)) > 0),
            import_mode TEXT NOT NULL CHECK (import_mode IN ('Complete', 'Partial')),
            new_entity_count INTEGER NOT NULL CHECK (new_entity_count >= 0),
            changed_entity_count INTEGER NOT NULL CHECK (changed_entity_count >= 0),
            archived_entity_count INTEGER NOT NULL CHECK (archived_entity_count >= 0),
            unchanged_entity_count INTEGER NOT NULL CHECK (unchanged_entity_count >= 0),
            unresolved_entity_count INTEGER NOT NULL CHECK (unresolved_entity_count >= 0),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );

        INSERT INTO schema_import_summary_v12
        (
            tracker_id, applied_at_utc, source_file_name, import_mode,
            new_entity_count, changed_entity_count, archived_entity_count,
            unchanged_entity_count, unresolved_entity_count
        )
        SELECT $defaultTrackerId, applied_at_utc, source_file_name, import_mode,
               new_entity_count, changed_entity_count, archived_entity_count,
               unchanged_entity_count, unresolved_entity_count
        FROM schema_import_summary;

        DROP TABLE schema_import_summary;
        ALTER TABLE schema_import_summary_v12 RENAME TO schema_import_summary;
        """;

    private const string UnresolvedDependencySchemaSql = """
        CREATE TABLE unresolved_schema_dependencies
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_source_key TEXT NOT NULL,
            dependency_source_name TEXT NOT NULL,
            dependency_kind TEXT NOT NULL
                CHECK (dependency_kind IN ('Mandatory', 'Optional')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_source_key),
            CHECK (length(trim(dependency_source_key)) > 0),
            CHECK (length(trim(dependency_source_name)) > 0),
            FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
        );
        """;

    private const string LifecycleSchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN lifecycle_state TEXT NOT NULL DEFAULT 'Active'
                CHECK (lifecycle_state IN ('Active', 'Archived'));
        """;

    private const string ProvenanceSchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN provenance TEXT NOT NULL DEFAULT 'Imported'
                CHECK (provenance IN ('Imported', 'ManualOnly', 'ManualAndImported'));
        """;

    private const string ManualDependencyOverrideSchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_dependencies
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_entity_id TEXT NOT NULL,
            dependency_kind TEXT NOT NULL
                CHECK (dependency_kind IN ('Mandatory', 'Optional')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_entity_id),
            CHECK (dependent_entity_id <> dependency_entity_id),
            FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE,
            FOREIGN KEY (dependency_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_schema_dependencies_dependency_entity_id
            ON schema_dependencies (dependency_entity_id);

        CREATE TABLE IF NOT EXISTS unresolved_schema_dependencies
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_source_key TEXT NOT NULL,
            dependency_source_name TEXT NOT NULL,
            dependency_kind TEXT NOT NULL
                CHECK (dependency_kind IN ('Mandatory', 'Optional')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_source_key),
            CHECK (length(trim(dependency_source_key)) > 0),
            CHECK (length(trim(dependency_source_name)) > 0),
            FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
        );

        CREATE TABLE manual_dependency_overrides
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_source_key TEXT NOT NULL,
            dependency_source_name TEXT NOT NULL,
            override_action TEXT NOT NULL
                CHECK (override_action IN ('Add', 'Suppress')),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_source_key),
            CHECK (length(trim(dependency_source_key)) > 0),
            CHECK (length(trim(dependency_source_name)) > 0),
            FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
        );

        INSERT INTO manual_dependency_overrides
        (
            dependent_entity_id,
            dependency_source_key,
            dependency_source_name,
            override_action,
            created_at_utc,
            updated_at_utc
        )
        SELECT dependency.dependent_entity_id,
               target.source_key,
               target.source_name,
               'Add',
               dependency.created_at_utc,
               dependency.updated_at_utc
        FROM schema_dependencies dependency
        INNER JOIN tracked_entities owner
            ON owner.id = dependency.dependent_entity_id
        INNER JOIN tracked_entities target
            ON target.id = dependency.dependency_entity_id
        WHERE owner.provenance = 'ManualOnly';

        INSERT INTO manual_dependency_overrides
        (
            dependent_entity_id,
            dependency_source_key,
            dependency_source_name,
            override_action,
            created_at_utc,
            updated_at_utc
        )
        SELECT dependency.dependent_entity_id,
               dependency.dependency_source_key,
               dependency.dependency_source_name,
               'Add',
               dependency.created_at_utc,
               dependency.updated_at_utc
        FROM unresolved_schema_dependencies dependency
        INNER JOIN tracked_entities owner
            ON owner.id = dependency.dependent_entity_id
        WHERE owner.provenance = 'ManualOnly'
        ON CONFLICT (dependent_entity_id, dependency_source_key)
        DO UPDATE SET
            dependency_source_name = excluded.dependency_source_name,
            override_action = 'Add',
            updated_at_utc = excluded.updated_at_utc;

        DELETE FROM schema_dependencies
        WHERE dependent_entity_id IN
        (
            SELECT id FROM tracked_entities WHERE provenance = 'ManualOnly'
        );

        DELETE FROM unresolved_schema_dependencies
        WHERE dependent_entity_id IN
        (
            SELECT id FROM tracked_entities WHERE provenance = 'ManualOnly'
        );
        """;

    private readonly string _connectionString;

    public SqliteDatabase(string databasePath, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(databasePath);

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "A SQLite database path cannot be empty or whitespace.",
                nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        TimeProvider = timeProvider ?? TimeProvider.System;

        SqliteConnectionStringBuilder connectionStringBuilder = new()
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };
        _connectionString = connectionStringBuilder.ToString();
    }

    public string DatabasePath { get; }

    internal TimeProvider TimeProvider { get; }

    internal async Task<int> GetStoredSchemaVersionAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        return await ReadSchemaVersionAsync(connection, cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? directoryPath = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        int schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);

        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The database schema version {schemaVersion} is newer than the supported version " +
                $"{CurrentSchemaVersion}.");
        }

        if (schemaVersion == CurrentSchemaVersion)
        {
            return;
        }

        bool requiresForeignKeyRebuild = schemaVersion < 12;
        bool rebuildWorkflowTrackedEntities = schemaVersion is > 0 and < 7;
        if (requiresForeignKeyRebuild)
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);
        }

        try
        {
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            bool catalogExisted = await TableExistsAsync(
                connection,
                transaction,
                "projects",
                cancellationToken);
            bool trackerOwnershipExisted = await ColumnExistsAsync(
                connection,
                transaction,
                "tracked_entities",
                "tracker_id",
                cancellationToken);

            if (schemaVersion < 1)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    InitialSchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 2)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    UnresolvedDependencySchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 3)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    LifecycleSchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 4)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    ProvenanceSchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 5)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    ManualDependencyOverrideSchemaSql,
                    cancellationToken);
            }

            if (rebuildWorkflowTrackedEntities && !trackerOwnershipExisted)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    WorkflowStatusSchemaSql,
                    cancellationToken);
                await EnsureNoForeignKeyViolationsAsync(
                    connection,
                    transaction,
                    cancellationToken);
            }

            if (schemaVersion < 7)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    ProgressHistorySchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 8)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    catalogExisted
                        ? TrackerSchemaImportSummarySql
                        : SchemaImportSummarySql,
                    cancellationToken);
            }

            if (schemaVersion < 9 && !await ColumnExistsAsync(
                    connection,
                    transaction,
                    "tracked_entities",
                    "requested_priority",
                    cancellationToken))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    RequestedPrioritySchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 10 && !await ColumnExistsAsync(
                    connection,
                    transaction,
                    "tracked_entities",
                    "responsible_developer",
                    cancellationToken))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    ResponsibleDeveloperSchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 11 && !await ColumnExistsAsync(
                    connection,
                    transaction,
                    "tracked_entities",
                    "group_name",
                    cancellationToken))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    GroupNameSchemaSql,
                    cancellationToken);
            }

            if (schemaVersion < 12 && catalogExisted != trackerOwnershipExisted)
            {
                throw new InvalidDataException(
                    "The Project/Tracker catalog migration is incomplete or inconsistent.");
            }

            if (schemaVersion < 12 && !catalogExisted)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = ProjectTrackerSchemaSql;
                DateTimeOffset migrationTime = TimeProvider.GetUtcNow().ToUniversalTime();
                ProjectId defaultProjectId = ProjectId.New();
                TrackerId defaultTrackerId = TrackerId.New();
                command.Parameters.AddWithValue(
                    "$defaultProjectId",
                    SqlitePersistenceValues.Format(defaultProjectId));
                command.Parameters.AddWithValue(
                    "$defaultTrackerId",
                    SqlitePersistenceValues.Format(defaultTrackerId));
                command.Parameters.AddWithValue("$defaultProjectName", "Default project");
                command.Parameters.AddWithValue("$defaultProjectNameKey", "DEFAULT PROJECT");
                command.Parameters.AddWithValue("$defaultTrackerName", "Default tracker");
                command.Parameters.AddWithValue("$defaultTrackerNameKey", "DEFAULT TRACKER");
                command.Parameters.AddWithValue(
                    "$migrationTimestamp",
                    SqlitePersistenceValues.FormatTimestamp(migrationTime));
                await command.ExecuteNonQueryAsync(cancellationToken);
                await EnsureNoForeignKeyViolationsAsync(
                    connection,
                    transaction,
                    cancellationToken);
            }

            if (schemaVersion < 13)
            {
                if (!await ColumnExistsAsync(
                        connection,
                        transaction,
                        "entity_status_history",
                        "operation_id",
                        cancellationToken))
                {
                    await AddOperationIdsToHistoryAsync(connection, transaction, cancellationToken);
                }
                await ExecuteAsync(
                    connection,
                    transaction,
                    OperationHistorySchemaSql,
                    cancellationToken);
                await EnsureNoForeignKeyViolationsAsync(
                    connection,
                    transaction,
                    cancellationToken);
            }

            if (schemaVersion < 14)
            {
                await EnsureTrackerOwnedProjectionHistoryAsync(
                    connection,
                    transaction,
                    cancellationToken);
                await AddOperationIdsToProjectionHistoryAsync(
                    connection,
                    transaction,
                    cancellationToken);
                await ExecuteAsync(
                    connection,
                    transaction,
                    OperationProjectionHistorySchemaSql,
                    cancellationToken);
                await EnsureNoForeignKeyViolationsAsync(
                    connection,
                    transaction,
                    cancellationToken);
            }

            if (schemaVersion < 15)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    ProjectOperationOutboxSchemaSql,
                    cancellationToken);
            }

            await ExecuteAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {CurrentSchemaVersion};",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (requiresForeignKeyRebuild)
            {
                await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
            }
        }
    }

    private static async Task AddOperationIdsToHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "ALTER TABLE entity_status_history ADD COLUMN operation_id TEXT NULL;",
            cancellationToken);

        List<(long Id, string Seed)> rows = [];
        using (SqliteCommand readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = """
                SELECT id, entity_id, ifnull(previous_status, ''), new_status,
                       entry_kind, occurred_at_utc
                FROM entity_status_history
                ORDER BY id;
                """;
            await using SqliteDataReader reader =
                await readCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string seed = string.Join(
                    "\n",
                    "entitytracker-status-history-v13",
                    reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5));
                rows.Add((reader.GetInt64(0), seed));
            }
        }

        foreach ((long id, string seed) in rows)
        {
            using SqliteCommand updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                "UPDATE entity_status_history SET operation_id = $operationId WHERE id = $id;";
            updateCommand.Parameters.AddWithValue("$operationId", CreateDeterministicGuid(seed));
            updateCommand.Parameters.AddWithValue("$id", id);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AddOperationIdsToProjectionHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "progress_snapshots",
                "operation_id",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE progress_snapshots ADD COLUMN operation_id TEXT NULL;",
                cancellationToken);
        }

        List<(long Id, string OperationId)> snapshots = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT snapshot.id, snapshot.tracker_id, snapshot.recorded_at_utc,
                       snapshot.ready_count, snapshot.blocked_count, snapshot.in_progress_count,
                       snapshot.rework_needed_count, snapshot.development_completed_count,
                       snapshot.reconciled_count,
                       (
                           SELECT min(history.operation_id)
                           FROM entity_status_history history
                           INNER JOIN tracked_entities entity ON entity.id = history.entity_id
                           WHERE entity.tracker_id = snapshot.tracker_id
                             AND history.occurred_at_utc = snapshot.recorded_at_utc
                       )
                FROM progress_snapshots snapshot
                ORDER BY snapshot.id;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string operationId = reader.IsDBNull(9)
                    ? CreateDeterministicGuid(string.Join(
                        "\n",
                        "entitytracker-progress-snapshot-v14",
                        reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        reader.GetString(1), reader.GetString(2),
                        reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                        reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)))
                    : reader.GetString(9);
                snapshots.Add((reader.GetInt64(0), operationId));
            }
        }

        foreach ((long id, string operationId) in snapshots)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE progress_snapshots SET operation_id = $operationId WHERE id = $id;";
            command.Parameters.AddWithValue("$operationId", operationId);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "schema_import_summary",
                "operation_id",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE schema_import_summary ADD COLUMN operation_id TEXT NULL;",
                cancellationToken);
        }

        List<(string TrackerId, string OperationId)> imports = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT summary.tracker_id, summary.applied_at_utc, summary.source_file_name,
                       summary.import_mode, summary.new_entity_count, summary.changed_entity_count,
                       summary.archived_entity_count, summary.unchanged_entity_count,
                       summary.unresolved_entity_count,
                       coalesce(
                           (SELECT min(snapshot.operation_id)
                            FROM progress_snapshots snapshot
                            WHERE snapshot.tracker_id = summary.tracker_id
                              AND snapshot.recorded_at_utc = summary.applied_at_utc),
                           (SELECT min(history.operation_id)
                            FROM entity_status_history history
                            INNER JOIN tracked_entities entity ON entity.id = history.entity_id
                            WHERE entity.tracker_id = summary.tracker_id
                              AND history.occurred_at_utc = summary.applied_at_utc))
                FROM schema_import_summary summary
                ORDER BY summary.tracker_id;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string operationId = reader.IsDBNull(9)
                    ? CreateDeterministicGuid(string.Join(
                        "\n",
                        "entitytracker-import-summary-v14",
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5),
                        reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)))
                    : reader.GetString(9);
                imports.Add((reader.GetString(0), operationId));
            }
        }

        foreach ((string trackerId, string operationId) in imports)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE schema_import_summary
                SET operation_id = $operationId
                WHERE tracker_id = $trackerId;
                """;
            command.Parameters.AddWithValue("$operationId", operationId);
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureTrackerOwnedProjectionHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(
                connection,
                transaction,
                "progress_snapshots",
                cancellationToken))
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE progress_snapshots
                (
                    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    tracker_id TEXT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    ready_count INTEGER NOT NULL CHECK (ready_count >= 0),
                    blocked_count INTEGER NOT NULL CHECK (blocked_count >= 0),
                    in_progress_count INTEGER NOT NULL CHECK (in_progress_count >= 0),
                    rework_needed_count INTEGER NOT NULL CHECK (rework_needed_count >= 0),
                    development_completed_count INTEGER NOT NULL CHECK (development_completed_count >= 0),
                    reconciled_count INTEGER NOT NULL CHECK (reconciled_count >= 0)
                );
                """, cancellationToken);
        }
        else if (!await ColumnExistsAsync(
                     connection,
                     transaction,
                     "progress_snapshots",
                     "tracker_id",
                     cancellationToken))
        {
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE progress_snapshots ADD COLUMN tracker_id TEXT NULL;
                UPDATE progress_snapshots
                SET tracker_id = (SELECT id FROM trackers ORDER BY id LIMIT 1);
                """, cancellationToken);
        }

        if (!await TableExistsAsync(
                connection,
                transaction,
                "schema_import_summary",
                cancellationToken))
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE schema_import_summary
                (
                    tracker_id TEXT NULL,
                    applied_at_utc TEXT NOT NULL,
                    source_file_name TEXT NOT NULL CHECK (length(trim(source_file_name)) > 0),
                    import_mode TEXT NOT NULL CHECK (import_mode IN ('Complete', 'Partial')),
                    new_entity_count INTEGER NOT NULL CHECK (new_entity_count >= 0),
                    changed_entity_count INTEGER NOT NULL CHECK (changed_entity_count >= 0),
                    archived_entity_count INTEGER NOT NULL CHECK (archived_entity_count >= 0),
                    unchanged_entity_count INTEGER NOT NULL CHECK (unchanged_entity_count >= 0),
                    unresolved_entity_count INTEGER NOT NULL CHECK (unresolved_entity_count >= 0)
                );
                """, cancellationToken);
        }
        else if (!await ColumnExistsAsync(
                     connection,
                     transaction,
                     "schema_import_summary",
                     "tracker_id",
                     cancellationToken))
        {
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE schema_import_summary ADD COLUMN tracker_id TEXT NULL;
                UPDATE schema_import_summary
                SET tracker_id = (SELECT id FROM trackers ORDER BY id LIMIT 1);
                """, cancellationToken);
        }
    }

    private static string CreateDeterministicGuid(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> bytes = hash.AsSpan(0, 16);
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D", System.Globalization.CultureInfo.InvariantCulture)
            .ToLowerInvariant();
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureNoForeignKeyViolationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The database migration found invalid dependency references and was rolled back.");
        }
    }
}
