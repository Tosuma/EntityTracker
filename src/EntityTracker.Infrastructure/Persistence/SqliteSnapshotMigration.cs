using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

internal static class SqliteSnapshotMigration
{
    internal static async Task MigrateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        string[] tables = ["trackers", "tracked_entities", "schema_dependencies",
            "unresolved_schema_dependencies", "manual_dependency_overrides",
            "entity_status_history", "progress_snapshots", "schema_import_summary"];
        foreach (string table in tables.Append("project"))
        foreach (string operation in new[] { "insert", "update", "delete" })
            await ExecuteAsync(connection, transaction,
                $"DROP TRIGGER IF EXISTS rev_{table}_{operation};", cancellationToken);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS schema_dependencies
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_entity_id TEXT NOT NULL,
                dependency_kind TEXT NOT NULL CHECK (dependency_kind IN ('Mandatory', 'Optional')),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_entity_id),
                CHECK (dependent_entity_id <> dependency_entity_id),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE,
                FOREIGN KEY (dependency_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS unresolved_schema_dependencies
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_source_key TEXT NOT NULL,
                dependency_source_name TEXT NOT NULL,
                dependency_kind TEXT NOT NULL CHECK (dependency_kind IN ('Mandatory', 'Optional')),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_source_key),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS manual_dependency_overrides
            (
                dependent_entity_id TEXT NOT NULL,
                dependency_source_key TEXT NOT NULL,
                dependency_source_name TEXT NOT NULL,
                override_action TEXT NOT NULL CHECK (override_action IN ('Add', 'Suppress')),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (dependent_entity_id, dependency_source_key),
                FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id) ON DELETE CASCADE
            );
            """, cancellationToken);

        if (await HasTrackerCopyForeignKeyAsync(connection, transaction, cancellationToken))
            await ExecuteAsync(connection, transaction, """
            CREATE TABLE trackers_v13
            (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                name_key TEXT NOT NULL,
                name TEXT NOT NULL CHECK (length(trim(name)) > 0),
                lifecycle_state TEXT NOT NULL CHECK (lifecycle_state IN ('Active', 'Recycled')),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                recycled_at_utc TEXT NULL,
                copied_from_tracker_id TEXT NULL,
                UNIQUE (project_id, name_key),
                CHECK ((lifecycle_state = 'Recycled') = (recycled_at_utc IS NOT NULL)),
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE RESTRICT
            );
            INSERT INTO trackers_v13 SELECT * FROM trackers;
            DROP TABLE trackers;
            ALTER TABLE trackers_v13 RENAME TO trackers;
            CREATE INDEX ix_trackers_project_lifecycle
                ON trackers (project_id, lifecycle_state, name_key);
            """, cancellationToken);

        if (!await ColumnExistsAsync(connection, transaction, "entity_status_history", "event_id", cancellationToken))
            await ExecuteAsync(connection, transaction,
                "ALTER TABLE entity_status_history ADD COLUMN event_id TEXT NULL;", cancellationToken);
        if (!await ColumnExistsAsync(connection, transaction, "entity_status_history", "previous_event_id", cancellationToken))
            await ExecuteAsync(connection, transaction,
                "ALTER TABLE entity_status_history ADD COLUMN previous_event_id TEXT NULL;", cancellationToken);
        if (!await ColumnExistsAsync(connection, transaction, "progress_snapshots", "snapshot_id", cancellationToken))
            await ExecuteAsync(connection, transaction,
                "ALTER TABLE progress_snapshots ADD COLUMN snapshot_id TEXT NULL;", cancellationToken);
        if (!await ColumnExistsAsync(connection, transaction, "progress_snapshots", "tracker_id", cancellationToken))
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE progress_snapshots ADD COLUMN tracker_id TEXT NULL;
                UPDATE progress_snapshots SET tracker_id =
                    (SELECT id FROM trackers ORDER BY id LIMIT 1);
                """, cancellationToken);

        List<(long RowId, string EntityId, string EventId, string? PreviousId)> events = [];
        using (SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT id, entity_id, event_id FROM entity_status_history
            ORDER BY entity_id, occurred_at_utc, id;
            """))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            string? currentEntity = null;
            string? predecessor = null;
            while (await reader.ReadAsync(cancellationToken))
            {
                long rowId = reader.GetInt64(0);
                string entityId = reader.GetString(1);
                if (entityId != currentEntity)
                {
                    currentEntity = entityId;
                    predecessor = null;
                }

                string eventId = reader.IsDBNull(2)
                    ? LegacyGuid($"status:{entityId}:{rowId}") : reader.GetString(2);
                if (reader.IsDBNull(2)) events.Add((rowId, entityId, eventId, predecessor));
                predecessor = eventId;
            }
        }

        foreach (var entry in events)
        {
            using SqliteCommand update = CreateCommand(connection, transaction, """
                UPDATE entity_status_history
                SET event_id = $eventId, previous_event_id = $previousId
                WHERE id = $rowId;
                """);
            update.Parameters.AddWithValue("$eventId", entry.EventId);
            update.Parameters.AddWithValue("$previousId", (object?)entry.PreviousId ?? DBNull.Value);
            update.Parameters.AddWithValue("$rowId", entry.RowId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        List<(long RowId, string SnapshotId)> snapshots = [];
        using (SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT id, tracker_id, snapshot_id FROM progress_snapshots ORDER BY id;
            """))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                long rowId = reader.GetInt64(0);
                if (reader.IsDBNull(2))
                    snapshots.Add((rowId, LegacyGuid($"progress:{reader.GetString(1)}:{rowId}")));
            }
        }

        foreach (var snapshot in snapshots)
        {
            using SqliteCommand update = CreateCommand(connection, transaction, """
                UPDATE progress_snapshots SET snapshot_id = $snapshotId WHERE id = $rowId;
                """);
            update.Parameters.AddWithValue("$snapshotId", snapshot.SnapshotId);
            update.Parameters.AddWithValue("$rowId", snapshot.RowId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(connection, transaction, """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_entity_status_history_event_id ON entity_status_history (event_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_progress_snapshots_snapshot_id ON progress_snapshots (snapshot_id);
            CREATE TRIGGER IF NOT EXISTS require_status_event_id BEFORE INSERT ON entity_status_history
            WHEN NEW.event_id IS NULL OR length(NEW.event_id) <> 36
            BEGIN SELECT RAISE(ABORT, 'A status event ID is required.'); END;
            CREATE TRIGGER IF NOT EXISTS require_snapshot_id BEFORE INSERT ON progress_snapshots
            WHEN NEW.snapshot_id IS NULL OR length(NEW.snapshot_id) <> 36
            BEGIN SELECT RAISE(ABORT, 'A progress snapshot ID is required.'); END;
            CREATE TABLE IF NOT EXISTS project_revisions
            (
                project_id TEXT NOT NULL PRIMARY KEY,
                revision INTEGER NOT NULL CHECK (revision >= 0)
            );
            INSERT OR IGNORE INTO project_revisions (project_id, revision)
            SELECT id, 0 FROM projects;
            CREATE TRIGGER rev_project_insert AFTER INSERT ON projects BEGIN
                INSERT INTO project_revisions (project_id, revision) VALUES (NEW.id, 1)
                ON CONFLICT (project_id) DO UPDATE SET revision = revision + 1;
            END;
            CREATE TRIGGER rev_project_update AFTER UPDATE ON projects BEGIN
                UPDATE project_revisions SET revision = revision + 1 WHERE project_id = NEW.id;
            END;
            CREATE TRIGGER rev_project_delete AFTER DELETE ON projects BEGIN
                UPDATE project_revisions SET revision = revision + 1 WHERE project_id = OLD.id;
            END;
            """, cancellationToken);

        foreach ((string table, string owner) in new[]
        {
            ("trackers", "tracker"),
            ("tracked_entities", "entity"),
            ("schema_dependencies", "relationship"),
            ("unresolved_schema_dependencies", "relationship"),
            ("manual_dependency_overrides", "relationship"),
            ("entity_status_history", "history"),
            ("progress_snapshots", "snapshot"),
            ("schema_import_summary", "snapshot")
        })
        {
            foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                string row = operation == "DELETE" ? "OLD" : "NEW";
                string expression = owner switch
                {
                    "tracker" => $"{row}.project_id",
                    "entity" => $"(SELECT project_id FROM trackers WHERE id = {row}.tracker_id)",
                    "snapshot" => $"(SELECT project_id FROM trackers WHERE id = {row}.tracker_id)",
                    "relationship" => $"(SELECT tracker.project_id FROM tracked_entities entity JOIN trackers tracker ON tracker.id = entity.tracker_id WHERE entity.id = {row}.dependent_entity_id)",
                    "history" => $"(SELECT tracker.project_id FROM tracked_entities entity JOIN trackers tracker ON tracker.id = entity.tracker_id WHERE entity.id = {row}.entity_id)",
                    _ => throw new InvalidOperationException()
                };
                string sql = $"CREATE TRIGGER rev_{table}_{operation.ToLowerInvariant()} AFTER {operation} ON {table} BEGIN " +
                             $"UPDATE project_revisions SET revision = revision + 1 WHERE project_id = {expression}; END;";
                await ExecuteAsync(connection, transaction, sql, cancellationToken);
            }
        }
    }

    private static string LegacyGuid(string source)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("EntityTracker.GS01." + source));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D");
    }

    private static async Task<bool> HasTrackerCopyForeignKeyAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction,
            "PRAGMA foreign_key_list(trackers);");
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (reader.GetString(3) == "copied_from_tracker_id") return true;
        return false;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction,
            $"PRAGMA table_info({table});");
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (reader.GetString(1) == column) return true;
        return false;
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateCommand(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
