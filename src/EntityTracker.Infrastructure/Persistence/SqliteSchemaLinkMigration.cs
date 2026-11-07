using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Version 28: a Tracker's link to the schema it was created from, and its baseline: per schema entity, the
/// name, group and dependencies the Tracker last took from the schema, and the Tracker entity linked to it.
/// Purging the Tracker or the schema removes the link; permanently deleting the linked entity only clears
/// it.
/// </summary>
internal static class SqliteSchemaLinkMigration
{
    internal const string TablesSql = """
        CREATE TABLE IF NOT EXISTS tracker_schemas
        (
            tracker_id TEXT NOT NULL PRIMARY KEY,
            schema_id TEXT NOT NULL,
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE,
            FOREIGN KEY (schema_id) REFERENCES schemas (id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_tracker_schemas_schema ON tracker_schemas (schema_id);
        CREATE TABLE IF NOT EXISTS tracker_schema_entities
        (
            tracker_id TEXT NOT NULL,
            schema_entity_id TEXT NOT NULL,
            entity_id TEXT NULL UNIQUE,
            name TEXT NOT NULL CHECK (length(name) > 0 AND name = trim(name)),
            group_name TEXT NOT NULL CHECK (group_name = trim(group_name)),
            PRIMARY KEY (tracker_id, schema_entity_id),
            FOREIGN KEY (tracker_id) REFERENCES tracker_schemas (tracker_id) ON DELETE CASCADE,
            FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE SET NULL
        );
        CREATE TABLE IF NOT EXISTS tracker_schema_dependencies
        (
            tracker_id TEXT NOT NULL,
            dependent_schema_entity_id TEXT NOT NULL,
            dependency_schema_entity_id TEXT NOT NULL,
            PRIMARY KEY (tracker_id, dependent_schema_entity_id, dependency_schema_entity_id),
            CHECK (dependency_schema_entity_id <> dependent_schema_entity_id),
            FOREIGN KEY (tracker_id, dependent_schema_entity_id)
                REFERENCES tracker_schema_entities (tracker_id, schema_entity_id) ON DELETE CASCADE,
            FOREIGN KEY (tracker_id, dependency_schema_entity_id)
                REFERENCES tracker_schema_entities (tracker_id, schema_entity_id) ON DELETE CASCADE
        );

        -- A trigger on tracked_entities would break the older migrations' table rebuilds, so whether a
        -- linked entity belongs to its Tracker is checked where links are written.
        DROP TRIGGER IF EXISTS validate_tracker_schema_entity;
        """;

    internal static async Task MigrateAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, TablesSql, cancellationToken);
        // The link is part of the Tracker, so changing it advances its Project's revision.
        foreach (string table in new[] { "tracker_schemas", "tracker_schema_entities", "tracker_schema_dependencies" })
        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            string name = $"rev_{table}_{operation.ToLowerInvariant()}";
            string row = operation == "DELETE" ? "OLD" : "NEW";
            await ExecuteAsync(connection, transaction,
                $"DROP TRIGGER IF EXISTS {name}; " +
                $"CREATE TRIGGER {name} AFTER {operation} ON {table} BEGIN " +
                "UPDATE project_revisions SET revision = revision + 1 WHERE project_id = " +
                $"(SELECT project_id FROM trackers WHERE id = {row}.tracker_id); END;",
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
