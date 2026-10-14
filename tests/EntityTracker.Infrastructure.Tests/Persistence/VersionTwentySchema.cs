using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

/// <summary>
/// Turns a current database back into the shape it had at schema version 20, before the schema
/// import was removed, so tests that roll the version back further run the old migration steps on a
/// realistic database. It only handles the data those tests use: no dependencies and no placeholders.
/// </summary>
internal static class VersionTwentySchema
{
    public static async Task RevertAsync(string databasePath)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = OFF;
            DROP TABLE IF EXISTS entity_dependencies;

            CREATE TABLE tracked_entities_v20
            (
                id TEXT NOT NULL PRIMARY KEY,
                tracker_id TEXT NOT NULL,
                source_key TEXT NOT NULL,
                source_name TEXT NOT NULL,
                development_status TEXT NOT NULL CHECK (development_status IN
                    ('NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted',
                     'Reconciled', 'Blocked', 'Reworking')),
                notes TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                schema_updated_at_utc TEXT NOT NULL,
                progress_updated_at_utc TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL CHECK (lifecycle_state IN ('Active', 'Archived')),
                provenance TEXT NOT NULL CHECK (provenance IN
                    ('Imported', 'ManualOnly', 'ManualAndImported', 'Copied', 'CopiedAndImported')),
                requested_priority INTEGER NULL CHECK
                    (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5),
                group_name TEXT NOT NULL,
                filter_active TEXT NOT NULL DEFAULT '',
                shared_notes TEXT NOT NULL DEFAULT '',
                UNIQUE (tracker_id, source_key),
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE RESTRICT
            );
            INSERT INTO tracked_entities_v20
            SELECT id, tracker_id, source_key, source_name, development_status, notes,
                   created_at_utc, schema_updated_at_utc, progress_updated_at_utc, lifecycle_state,
                   CASE provenance WHEN 'Manual' THEN 'ManualOnly' ELSE provenance END,
                   requested_priority, group_name, filter_active, shared_notes
            FROM tracked_entities;
            PRAGMA legacy_alter_table = ON;
            DROP TABLE tracked_entities;
            ALTER TABLE tracked_entities_v20 RENAME TO tracked_entities;
            PRAGMA legacy_alter_table = OFF;
            CREATE INDEX ix_tracked_entities_tracker_lifecycle
                ON tracked_entities (tracker_id, lifecycle_state, source_key);

            CREATE TABLE schema_dependencies
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
            CREATE TABLE unresolved_schema_dependencies
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
            CREATE TABLE manual_dependency_overrides
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
            PRAGMA user_version = 20;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
