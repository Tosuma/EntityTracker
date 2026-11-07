using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Version 27: a Project's schemas. A schema has entities (a name and a group) and dependencies between
/// them, all by ID. A dependency always points at an entity of the same schema; a schema has no
/// placeholders.
/// </summary>
internal static class SqliteSchemaMigration
{
    internal const string TablesSql = """
        CREATE TABLE IF NOT EXISTS schemas
        (
            id TEXT NOT NULL PRIMARY KEY,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL COLLATE ENTITY_NAME
                CHECK (length(name) > 0 AND name = trim(name)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            recycled_at_utc TEXT NULL,
            UNIQUE (project_id, name),
            FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE RESTRICT
        );
        CREATE TABLE IF NOT EXISTS schema_entities
        (
            id TEXT NOT NULL PRIMARY KEY,
            schema_id TEXT NOT NULL,
            name TEXT NOT NULL COLLATE ENTITY_NAME
                CHECK (length(name) > 0 AND name = trim(name)),
            group_name TEXT NOT NULL CHECK (group_name = trim(group_name)),
            UNIQUE (schema_id, name),
            FOREIGN KEY (schema_id) REFERENCES schemas (id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS schema_entity_dependencies
        (
            dependent_entity_id TEXT NOT NULL,
            dependency_entity_id TEXT NOT NULL,
            PRIMARY KEY (dependent_entity_id, dependency_entity_id),
            CHECK (dependency_entity_id <> dependent_entity_id),
            FOREIGN KEY (dependent_entity_id) REFERENCES schema_entities (id) ON DELETE CASCADE,
            FOREIGN KEY (dependency_entity_id) REFERENCES schema_entities (id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_schema_entity_dependencies_target
            ON schema_entity_dependencies (dependency_entity_id);

        -- Both entities of a dependency belong to the same schema.
        DROP TRIGGER IF EXISTS validate_schema_dependency_insert;
        CREATE TRIGGER validate_schema_dependency_insert
        BEFORE INSERT ON schema_entity_dependencies
        WHEN (SELECT schema_id FROM schema_entities WHERE id = NEW.dependent_entity_id) IS NOT
             (SELECT schema_id FROM schema_entities WHERE id = NEW.dependency_entity_id)
        BEGIN
            SELECT RAISE(ABORT, 'A schema dependency must stay within one schema.');
        END;
        """;

    internal static async Task MigrateAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, TablesSql, cancellationToken);
        await CreateRevisionTriggersAsync(connection, transaction, cancellationToken);
    }

    /// <summary>A schema is part of its Project's synchronized state, so changing it advances the revision.</summary>
    private static async Task CreateRevisionTriggersAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        const string BySchema = "(SELECT project_id FROM schemas WHERE id = {0}.schema_id)";
        const string ByEntity = "(SELECT owner.project_id FROM schema_entities entity JOIN schemas owner ON owner.id = entity.schema_id WHERE entity.id = {0}.dependent_entity_id)";
        (string Table, string Project)[] tables =
        [
            ("schemas", "{0}.project_id"),
            ("schema_entities", BySchema),
            ("schema_entity_dependencies", ByEntity)
        ];
        foreach ((string table, string project) in tables)
        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            string name = $"rev_{table}_{operation.ToLowerInvariant()}";
            string row = operation == "DELETE" ? "OLD" : "NEW";
            await ExecuteAsync(connection, transaction,
                $"DROP TRIGGER IF EXISTS {name}; " +
                $"CREATE TRIGGER {name} AFTER {operation} ON {table} BEGIN " +
                "UPDATE project_revisions SET revision = revision + 1 WHERE project_id = " +
                $"{string.Format(System.Globalization.CultureInfo.InvariantCulture, project, row)}; END;",
                cancellationToken);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
