using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    private const int CurrentSchemaVersion = 4;

    private const string InitialSchemaSql = """
        CREATE TABLE tracked_entities
        (
            id TEXT NOT NULL PRIMARY KEY,
            source_key TEXT NOT NULL UNIQUE,
            source_name TEXT NOT NULL,
            development_status TEXT NOT NULL
                CHECK (development_status IN ('NotStarted', 'InProgress', 'Completed')),
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

        await ExecuteAsync(
            connection,
            transaction,
            $"PRAGMA user_version = {CurrentSchemaVersion};",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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
}
