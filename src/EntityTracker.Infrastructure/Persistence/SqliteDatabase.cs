using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    internal const int CurrentSchemaVersion = 10;

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

    private const string RequestedPrioritySchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN requested_priority INTEGER NULL
                CHECK (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5);
        """;

    private const string ResponsibleDeveloperSchemaSql = """
        ALTER TABLE tracked_entities
            ADD COLUMN responsible_developer TEXT NOT NULL DEFAULT '';
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

        bool rebuildTrackedEntities = schemaVersion is > 0 and < 7;
        if (rebuildTrackedEntities)
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);
        }

        try
        {
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

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

            if (rebuildTrackedEntities)
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
                    SchemaImportSummarySql,
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

            await ExecuteAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {CurrentSchemaVersion};",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (rebuildTrackedEntities)
            {
                await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
            }
        }
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
