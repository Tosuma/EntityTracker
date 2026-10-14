using System.Text.Json;

using EntityTracker.Application.Tracking;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

/// <summary>
/// Turns a current database back into the shape it had at schema version 20, before the schema import
/// was removed and before the schema was brought to Boyce-Codd normal form, so tests that roll the
/// version back run the old migration steps on a realistic database. Dependencies are not kept (version
/// 20 stored imported dependencies differently), and placeholders are left out.
/// </summary>
internal static class VersionTwentySchema
{
    private const string Statuses =
        "'NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled', 'Blocked', 'Reworking'";

    public static async Task RevertAsync(string databasePath)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath}");
        await connection.OpenAsync();
        EntityNameCollation.Register(connection);

        List<(string TrackerId, string Json)> baselines = [];
        using (SqliteCommand trackers = connection.CreateCommand())
        {
            trackers.CommandText = "SELECT tracker_id FROM tracker_sync_baselines;";
            List<string> ids = [];
            await using (SqliteDataReader reader = await trackers.ExecuteReaderAsync())
                while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
            await using SqliteTransaction read = connection.BeginTransaction();
            foreach (string id in ids)
            {
                TrackerSyncBaseline baseline = (await SqliteSyncBaselineRows.ReadAsync(
                    connection, read, id, CancellationToken.None))!;
                baselines.Add((id, JsonSerializer.Serialize(baseline)));
            }
            await read.CommitAsync();
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA foreign_keys = OFF;
            DROP VIEW IF EXISTS entity_current_status_v20;
            CREATE TEMP TABLE status_v20 AS
                SELECT entity_id, development_status FROM entity_current_status;
            DROP VIEW entity_current_status;
            CREATE TEMP TABLE dependencies_v20 AS
                SELECT dependency.*, target.lifecycle_state AS target_state, target.source_name AS target_name
                FROM entity_dependencies dependency
                JOIN tracked_entities target ON target.id = dependency.dependency_entity_id;
            DROP TABLE IF EXISTS entity_dependencies;
            DROP TABLE IF EXISTS tracker_sync_baseline_dependencies;
            DROP TABLE IF EXISTS tracker_sync_baseline_entities;
            PRAGMA legacy_alter_table = ON;

            CREATE TABLE projects_v20
            (
                id TEXT NOT NULL PRIMARY KEY,
                name_key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL CHECK (length(trim(name)) > 0),
                lifecycle_state TEXT NOT NULL CHECK (lifecycle_state IN ('Active', 'Recycled')),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                recycled_at_utc TEXT NULL,
                CHECK ((lifecycle_state = 'Recycled') = (recycled_at_utc IS NOT NULL))
            );
            INSERT INTO projects_v20
            SELECT id, upper(name), name, CASE WHEN recycled_at_utc IS NULL THEN 'Active' ELSE 'Recycled' END,
                   created_at_utc, updated_at_utc, recycled_at_utc
            FROM projects;
            DROP TABLE projects;
            ALTER TABLE projects_v20 RENAME TO projects;

            CREATE TABLE trackers_v20
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
            INSERT INTO trackers_v20
            SELECT id, project_id, upper(name), name,
                   CASE WHEN recycled_at_utc IS NULL THEN 'Active' ELSE 'Recycled' END,
                   created_at_utc, updated_at_utc, recycled_at_utc, copied_from_tracker_id
            FROM trackers;
            DROP TABLE trackers;
            ALTER TABLE trackers_v20 RENAME TO trackers;
            CREATE INDEX ix_trackers_project_lifecycle ON trackers (project_id, lifecycle_state, name_key);

            CREATE TABLE tracked_entities_v20
            (
                id TEXT NOT NULL PRIMARY KEY,
                tracker_id TEXT NOT NULL,
                source_key TEXT NOT NULL,
                source_name TEXT NOT NULL,
                development_status TEXT NOT NULL CHECK (development_status IN ({Statuses})),
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
            SELECT entity.id, entity.tracker_id, upper(entity.source_name), entity.source_name,
                   status.development_status, entity.notes, entity.created_at_utc,
                   entity.schema_updated_at_utc, entity.progress_updated_at_utc, entity.lifecycle_state,
                   CASE entity.provenance WHEN 'Manual' THEN 'ManualOnly' ELSE entity.provenance END,
                   entity.requested_priority, entity.group_name, entity.filter_active, entity.shared_notes
            FROM tracked_entities entity JOIN status_v20 status ON status.entity_id = entity.id
            WHERE entity.lifecycle_state <> 'Placeholder';
            DROP TABLE tracked_entities;
            ALTER TABLE tracked_entities_v20 RENAME TO tracked_entities;
            CREATE INDEX ix_tracked_entities_tracker_lifecycle
                ON tracked_entities (tracker_id, lifecycle_state, source_key);

            CREATE TABLE project_developers_v20
            (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                initials_key TEXT NOT NULL,
                initials TEXT NOT NULL CHECK (length(trim(initials)) > 0),
                display_name TEXT NOT NULL DEFAULT '',
                is_retired INTEGER NOT NULL DEFAULT 0 CHECK (is_retired IN (0, 1)),
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
            );
            INSERT INTO project_developers_v20
            SELECT id, project_id, upper(initials), initials, display_name, is_retired FROM project_developers;
            DROP TABLE project_developers;
            ALTER TABLE project_developers_v20 RENAME TO project_developers;
            CREATE INDEX ix_project_developers_project
                ON project_developers (project_id, is_retired, initials_key, id);
            CREATE UNIQUE INDEX ux_project_developers_available_initials
                ON project_developers (project_id, initials_key) WHERE is_retired = 0;

            CREATE TABLE entity_status_history_v20
            (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                entity_id TEXT NOT NULL,
                previous_status TEXT NULL CHECK (previous_status IS NULL OR previous_status IN ({Statuses})),
                new_status TEXT NOT NULL CHECK (new_status IN ({Statuses})),
                entry_kind TEXT NOT NULL CHECK (entry_kind IN ('Baseline', 'Created', 'Transition')),
                occurred_at_utc TEXT NOT NULL,
                event_id TEXT NOT NULL,
                previous_event_id TEXT NULL,
                CHECK ((entry_kind = 'Transition' AND previous_status IS NOT NULL) OR
                       (entry_kind IN ('Baseline', 'Created') AND previous_status IS NULL)),
                FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT
            );
            INSERT INTO entity_status_history_v20
            (id, entity_id, previous_status, new_status, entry_kind, occurred_at_utc, event_id, previous_event_id)
            SELECT entry.sequence, entry.entity_id, previous.new_status, entry.new_status, entry.entry_kind,
                   entry.occurred_at_utc, entry.event_id, entry.previous_event_id
            FROM entity_status_history entry
            LEFT JOIN entity_status_history previous ON previous.event_id = entry.previous_event_id;
            DROP TABLE entity_status_history;
            ALTER TABLE entity_status_history_v20 RENAME TO entity_status_history;
            CREATE INDEX ix_entity_status_history_entity_time
                ON entity_status_history (entity_id, occurred_at_utc, id);
            CREATE INDEX ix_entity_status_history_time ON entity_status_history (occurred_at_utc, id);
            CREATE UNIQUE INDEX ux_entity_status_history_event_id ON entity_status_history (event_id);

            CREATE TABLE progress_snapshots_v20
            (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                tracker_id TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                ready_count INTEGER NOT NULL,
                blocked_count INTEGER NOT NULL,
                in_progress_count INTEGER NOT NULL,
                rework_needed_count INTEGER NOT NULL,
                development_completed_count INTEGER NOT NULL,
                reconciled_count INTEGER NOT NULL,
                snapshot_id TEXT NULL,
                manually_blocked_count INTEGER NOT NULL DEFAULT 0,
                reworking_count INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
            );
            INSERT INTO progress_snapshots_v20
            SELECT sequence, tracker_id, recorded_at_utc, ready_count, blocked_count, in_progress_count,
                   rework_needed_count, development_completed_count, reconciled_count, snapshot_id,
                   manually_blocked_count, reworking_count
            FROM progress_snapshots;
            DROP TABLE progress_snapshots;
            ALTER TABLE progress_snapshots_v20 RENAME TO progress_snapshots;
            CREATE INDEX ix_progress_snapshots_tracker_time
                ON progress_snapshots (tracker_id, recorded_at_utc, id);
            CREATE UNIQUE INDEX ux_progress_snapshots_snapshot_id ON progress_snapshots (snapshot_id);

            DROP TABLE tracker_sync_baselines;
            CREATE TABLE tracker_sync_baselines
            (
                tracker_id TEXT NOT NULL PRIMARY KEY,
                baseline_json TEXT NOT NULL,
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
            );

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
            -- Version 20 kept a dependency on an entity by ID, and one on a name no entity had by name.
            INSERT INTO schema_dependencies
            SELECT dependent_entity_id, dependency_entity_id, 'Mandatory', created_at_utc, updated_at_utc
            FROM temp.dependencies_v20 WHERE target_state <> 'Placeholder';
            INSERT INTO unresolved_schema_dependencies
            SELECT dependent_entity_id, upper(target_name), target_name, 'Mandatory', created_at_utc, updated_at_utc
            FROM temp.dependencies_v20 WHERE target_state = 'Placeholder';
            PRAGMA legacy_alter_table = OFF;
            DROP TABLE temp.status_v20;
            DROP TABLE temp.dependencies_v20;
            PRAGMA user_version = 20;
            """;
        await command.ExecuteNonQueryAsync();

        foreach ((string trackerId, string json) in baselines)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO tracker_sync_baselines (tracker_id, baseline_json) VALUES ($id, $json);";
            insert.Parameters.AddWithValue("$id", trackerId);
            insert.Parameters.AddWithValue("$json", json);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
