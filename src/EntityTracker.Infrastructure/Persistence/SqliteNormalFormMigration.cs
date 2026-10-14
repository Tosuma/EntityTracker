using System.Globalization;
using System.Text.Json;

using EntityTracker.Application.Tracking;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Version 22 brings the schema to Boyce-Codd normal form: no column is derived from another column
/// of its row, and every table holds atomic values.
/// <list type="bullet">
/// <item>Names are compared with the <see cref="EntityNameCollation"/>, so the derived name-key
/// columns (source_key, name_key, initials_key) go.</item>
/// <item>A Project or Tracker is recycled exactly when it has a recycle time, so lifecycle_state goes
/// from those tables.</item>
/// <item>The current development status is the newest status event (view entity_current_status), and
/// an event's previous status is the new status of the event it follows, so both stored copies go.</item>
/// <item>Status events and progress snapshots are identified by their GUIDs; an explicit sequence keeps
/// the order they were recorded in.</item>
/// <item>The Tracker sync baseline, a JSON document, becomes rows.</item>
/// </list>
/// </summary>
internal static class SqliteNormalFormMigration
{
    private const string Statuses =
        "'NotStarted', 'InProgress', 'ReworkNeeded', 'DevelopmentCompleted', 'Reconciled', 'Blocked', 'Reworking'";

    internal static async Task MigrateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await CompleteStatusHistoryAsync(connection, transaction, cancellationToken);
        IReadOnlyList<(string TrackerId, TrackerSyncBaseline Baseline)> baselines =
            await ReadBaselinesAsync(connection, transaction, cancellationToken);

        await ExecuteAsync(connection, transaction, $"""
            DROP TRIGGER IF EXISTS require_status_event_id;
            DROP TRIGGER IF EXISTS require_snapshot_id;
            DROP VIEW IF EXISTS entity_current_status;
            PRAGMA legacy_alter_table = ON;

            CREATE TABLE projects_v22
            (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL COLLATE ENTITY_NAME UNIQUE
                    CHECK (length(name) > 0 AND name = trim(name)),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                recycled_at_utc TEXT NULL
            );
            INSERT INTO projects_v22 (id, name, created_at_utc, updated_at_utc, recycled_at_utc)
            SELECT id, trim(name), created_at_utc, updated_at_utc,
                   CASE WHEN lifecycle_state = 'Recycled' THEN COALESCE(recycled_at_utc, updated_at_utc) END
            FROM projects;
            DROP TABLE projects;
            ALTER TABLE projects_v22 RENAME TO projects;

            CREATE TABLE trackers_v22
            (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL COLLATE ENTITY_NAME
                    CHECK (length(name) > 0 AND name = trim(name)),
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                recycled_at_utc TEXT NULL,
                -- The Tracker this one was copied from. It can be in another Project that is not on
                -- this machine, so it is not a foreign key.
                copied_from_tracker_id TEXT NULL,
                UNIQUE (project_id, name),
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE RESTRICT
            );
            INSERT INTO trackers_v22
            (id, project_id, name, created_at_utc, updated_at_utc, recycled_at_utc, copied_from_tracker_id)
            SELECT id, project_id, trim(name), created_at_utc, updated_at_utc,
                   CASE WHEN lifecycle_state = 'Recycled' THEN COALESCE(recycled_at_utc, updated_at_utc) END,
                   copied_from_tracker_id
            FROM trackers;
            DROP TABLE trackers;
            ALTER TABLE trackers_v22 RENAME TO trackers;
            CREATE INDEX ix_trackers_project ON trackers (project_id, recycled_at_utc);

            CREATE TABLE tracked_entities_v22
            (
                id TEXT NOT NULL PRIMARY KEY,
                tracker_id TEXT NOT NULL,
                source_name TEXT NOT NULL COLLATE ENTITY_NAME
                    CHECK (length(source_name) > 0 AND source_name = trim(source_name)),
                notes TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                schema_updated_at_utc TEXT NOT NULL,
                progress_updated_at_utc TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL CHECK (lifecycle_state IN ('Active', 'Archived', 'Placeholder')),
                provenance TEXT NOT NULL CHECK (provenance IN ('Manual', 'Copied')),
                requested_priority INTEGER NULL CHECK
                    (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5),
                group_name TEXT NOT NULL,
                filter_active TEXT NOT NULL DEFAULT '',
                shared_notes TEXT NOT NULL DEFAULT '',
                UNIQUE (tracker_id, source_name),
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE RESTRICT
            );
            INSERT INTO tracked_entities_v22
            (id, tracker_id, source_name, notes, created_at_utc, schema_updated_at_utc,
             progress_updated_at_utc, lifecycle_state, provenance, requested_priority, group_name,
             filter_active, shared_notes)
            SELECT id, tracker_id, trim(source_name), notes, created_at_utc, schema_updated_at_utc,
                   progress_updated_at_utc, lifecycle_state, provenance, requested_priority, group_name,
                   filter_active, shared_notes
            FROM tracked_entities;
            DROP TABLE tracked_entities;
            ALTER TABLE tracked_entities_v22 RENAME TO tracked_entities;
            CREATE INDEX ix_tracked_entities_tracker_lifecycle
                ON tracked_entities (tracker_id, lifecycle_state);

            CREATE TABLE project_developers_v22
            (
                id TEXT NOT NULL PRIMARY KEY,
                project_id TEXT NOT NULL,
                initials TEXT NOT NULL COLLATE ENTITY_NAME
                    CHECK (length(initials) > 0 AND initials = trim(initials)),
                display_name TEXT NOT NULL DEFAULT '',
                is_retired INTEGER NOT NULL DEFAULT 0 CHECK (is_retired IN (0, 1)),
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
            );
            INSERT INTO project_developers_v22 (id, project_id, initials, display_name, is_retired)
            SELECT id, project_id, trim(initials), display_name, is_retired FROM project_developers;
            DROP TABLE project_developers;
            ALTER TABLE project_developers_v22 RENAME TO project_developers;
            CREATE INDEX ix_project_developers_project
                ON project_developers (project_id, is_retired, initials);
            -- Initials are unique among a Project's available developers.
            CREATE UNIQUE INDEX ux_project_developers_available_initials
                ON project_developers (project_id, initials)
                WHERE is_retired = 0;

            CREATE TABLE entity_status_history_v22
            (
                event_id TEXT NOT NULL PRIMARY KEY CHECK (length(event_id) = 36),
                entity_id TEXT NOT NULL,
                -- The order events were recorded in; equal-time events keep their order by it.
                sequence INTEGER NOT NULL,
                -- The event this one follows; its new status is this event's previous status.
                previous_event_id TEXT NULL,
                new_status TEXT NOT NULL CHECK (new_status IN ({Statuses})),
                entry_kind TEXT NOT NULL CHECK (entry_kind IN ('Baseline', 'Created', 'Transition')),
                occurred_at_utc TEXT NOT NULL,
                CHECK ((entry_kind = 'Transition') = (previous_event_id IS NOT NULL)),
                FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT,
                FOREIGN KEY (previous_event_id) REFERENCES entity_status_history (event_id)
            );
            INSERT INTO entity_status_history_v22
            (event_id, entity_id, sequence, previous_event_id, new_status, entry_kind, occurred_at_utc)
            SELECT event_id, entity_id, id, previous_event_id, new_status, entry_kind, occurred_at_utc
            FROM entity_status_history;
            DROP TABLE entity_status_history;
            ALTER TABLE entity_status_history_v22 RENAME TO entity_status_history;
            CREATE INDEX ix_entity_status_history_entity_time
                ON entity_status_history (entity_id, occurred_at_utc, sequence);
            CREATE INDEX ix_entity_status_history_time
                ON entity_status_history (occurred_at_utc, sequence);
            CREATE INDEX ix_entity_status_history_previous
                ON entity_status_history (previous_event_id);

            CREATE TABLE progress_snapshots_v22
            (
                snapshot_id TEXT NOT NULL PRIMARY KEY CHECK (length(snapshot_id) = 36),
                tracker_id TEXT NOT NULL,
                -- The order snapshots were recorded in; equal-time snapshots keep their order by it.
                sequence INTEGER NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                ready_count INTEGER NOT NULL CHECK (ready_count >= 0),
                blocked_count INTEGER NOT NULL CHECK (blocked_count >= 0),
                in_progress_count INTEGER NOT NULL CHECK (in_progress_count >= 0),
                rework_needed_count INTEGER NOT NULL CHECK (rework_needed_count >= 0),
                development_completed_count INTEGER NOT NULL CHECK (development_completed_count >= 0),
                reconciled_count INTEGER NOT NULL CHECK (reconciled_count >= 0),
                manually_blocked_count INTEGER NOT NULL DEFAULT 0 CHECK (manually_blocked_count >= 0),
                reworking_count INTEGER NOT NULL DEFAULT 0 CHECK (reworking_count >= 0),
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
            );
            INSERT INTO progress_snapshots_v22
            (snapshot_id, tracker_id, sequence, recorded_at_utc, ready_count, blocked_count,
             in_progress_count, rework_needed_count, development_completed_count, reconciled_count,
             manually_blocked_count, reworking_count)
            SELECT snapshot_id, tracker_id, id, recorded_at_utc, ready_count, blocked_count,
                   in_progress_count, rework_needed_count, development_completed_count, reconciled_count,
                   manually_blocked_count, reworking_count
            FROM progress_snapshots;
            DROP TABLE progress_snapshots;
            ALTER TABLE progress_snapshots_v22 RENAME TO progress_snapshots;
            CREATE INDEX ix_progress_snapshots_tracker_time
                ON progress_snapshots (tracker_id, recorded_at_utc, sequence);

            DROP TABLE IF EXISTS tracker_sync_baselines;
            -- A Tracker copied from another has a baseline: both Trackers' structure at the last sync.
            CREATE TABLE tracker_sync_baselines
            (
                tracker_id TEXT NOT NULL PRIMARY KEY,
                FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
            );
            CREATE TABLE tracker_sync_baseline_entities
            (
                id TEXT NOT NULL PRIMARY KEY,
                tracker_id TEXT NOT NULL,
                side TEXT NOT NULL CHECK (side IN ('Source', 'Destination')),
                name TEXT NOT NULL COLLATE ENTITY_NAME CHECK (length(name) > 0),
                is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
                requested_priority INTEGER NULL CHECK
                    (requested_priority IS NULL OR requested_priority BETWEEN 1 AND 5),
                group_name TEXT NOT NULL,
                -- Null in baselines saved before Filter active was synced.
                filter_active TEXT NULL,
                UNIQUE (tracker_id, side, name),
                FOREIGN KEY (tracker_id) REFERENCES tracker_sync_baselines (tracker_id) ON DELETE CASCADE
            );
            -- A baseline compares Trackers by name, so a dependency is the name it had; it can name a
            -- placeholder, which is not a baseline entity.
            CREATE TABLE tracker_sync_baseline_dependencies
            (
                baseline_entity_id TEXT NOT NULL,
                dependency_name TEXT NOT NULL COLLATE ENTITY_NAME CHECK (length(dependency_name) > 0),
                PRIMARY KEY (baseline_entity_id, dependency_name),
                FOREIGN KEY (baseline_entity_id) REFERENCES tracker_sync_baseline_entities (id) ON DELETE CASCADE
            );

            PRAGMA legacy_alter_table = OFF;

            -- The current development status of every entity: its newest status event. A placeholder
            -- has no events and is Not started.
            CREATE VIEW entity_current_status AS
            SELECT entity.id AS entity_id,
                   COALESCE((SELECT history.new_status FROM entity_status_history history
                             WHERE history.entity_id = entity.id
                             ORDER BY history.occurred_at_utc DESC, history.sequence DESC
                             LIMIT 1), 'NotStarted') AS development_status
            FROM tracked_entities entity;
            """, cancellationToken);

        foreach ((string trackerId, TrackerSyncBaseline baseline) in baselines)
            await SqliteSyncBaselineRows.WriteAsync(connection, transaction, trackerId, baseline, cancellationToken);

        await CreateRevisionTriggersAsync(connection, transaction, cancellationToken);
    }

    /// <summary>
    /// Makes the status history able to carry everything the dropped columns held: every event has an
    /// ID, every transition names the event it follows, and every entity's newest event has the
    /// entity's current status.
    /// </summary>
    private static async Task CompleteStatusHistoryAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (long id in await ReadIdsAsync("SELECT id FROM entity_status_history WHERE event_id IS NULL;"))
            await ExecuteAsync(connection, transaction,
                $"UPDATE entity_status_history SET event_id = '{Guid.NewGuid():D}' WHERE id = {id};", cancellationToken);
        foreach (long id in await ReadIdsAsync("SELECT id FROM progress_snapshots WHERE snapshot_id IS NULL;"))
            await ExecuteAsync(connection, transaction,
                $"UPDATE progress_snapshots SET snapshot_id = '{Guid.NewGuid():D}' WHERE id = {id};", cancellationToken);

        // A transition without the event it follows gets the entity's preceding event, or becomes a
        // baseline when there is none.
        await ExecuteAsync(connection, transaction, """
            UPDATE entity_status_history
            SET previous_event_id = (SELECT prior.event_id FROM entity_status_history prior
                                     WHERE prior.entity_id = entity_status_history.entity_id
                                       AND (prior.occurred_at_utc < entity_status_history.occurred_at_utc
                                            OR (prior.occurred_at_utc = entity_status_history.occurred_at_utc
                                                AND prior.id < entity_status_history.id))
                                     ORDER BY prior.occurred_at_utc DESC, prior.id DESC LIMIT 1)
            WHERE entry_kind = 'Transition' AND previous_event_id IS NULL;
            UPDATE entity_status_history
            SET entry_kind = 'Baseline', previous_status = NULL
            WHERE entry_kind = 'Transition' AND previous_event_id IS NULL;
            UPDATE entity_status_history SET previous_event_id = NULL
            WHERE entry_kind <> 'Transition';
            """, cancellationToken);

        // An entity whose stored status is not its newest event's gets an event for it, so deriving
        // the status from the history keeps it.
        List<(string EntityId, string Status, string Time, string? Latest)> missing = [];
        using (SqliteCommand command = Command("""
                   SELECT entity.id, entity.development_status, entity.progress_updated_at_utc,
                          (SELECT history.event_id FROM entity_status_history history
                           WHERE history.entity_id = entity.id
                           ORDER BY history.occurred_at_utc DESC, history.id DESC LIMIT 1),
                          (SELECT history.new_status FROM entity_status_history history
                           WHERE history.entity_id = entity.id
                           ORDER BY history.occurred_at_utc DESC, history.id DESC LIMIT 1),
                          (SELECT MAX(history.occurred_at_utc) FROM entity_status_history history
                           WHERE history.entity_id = entity.id)
                   FROM tracked_entities entity
                   WHERE entity.lifecycle_state <> 'Placeholder';
                   """))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                string status = reader.GetString(1);
                string? latestStatus = reader.IsDBNull(4) ? null : reader.GetString(4);
                if (latestStatus == status) continue;
                string time = reader.GetString(2);
                if (!reader.IsDBNull(5) && string.CompareOrdinal(reader.GetString(5), time) > 0)
                    time = reader.GetString(5);
                missing.Add((reader.GetString(0), status, time, reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }
        foreach (var item in missing)
        {
            using SqliteCommand insert = Command("""
                INSERT INTO entity_status_history
                (entity_id, previous_status, new_status, entry_kind, occurred_at_utc, event_id, previous_event_id)
                VALUES ($entity, (SELECT new_status FROM entity_status_history WHERE event_id = $latest),
                        $status, CASE WHEN $latest IS NULL THEN 'Baseline' ELSE 'Transition' END,
                        $time, $event, $latest);
                """);
            insert.Parameters.AddWithValue("$entity", item.EntityId);
            insert.Parameters.AddWithValue("$status", item.Status);
            insert.Parameters.AddWithValue("$time", item.Time);
            insert.Parameters.AddWithValue("$event", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$latest", (object?)item.Latest ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        async Task<List<long>> ReadIdsAsync(string sql)
        {
            List<long> ids = [];
            using SqliteCommand command = Command(sql);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetInt64(0));
            return ids;
        }

        SqliteCommand Command(string sql)
        {
            SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }
    }

    private static async Task<IReadOnlyList<(string, TrackerSyncBaseline)>> ReadBaselinesAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        List<(string, TrackerSyncBaseline)> baselines = [];
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT tracker_id, baseline_json FROM tracker_sync_baselines ORDER BY tracker_id;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            baselines.Add((reader.GetString(0),
                JsonSerializer.Deserialize<TrackerSyncBaseline>(reader.GetString(1))
                ?? throw new InvalidDataException("A Tracker sync baseline is invalid.")));
        }

        return baselines;
    }

    /// <summary>
    /// Every table that is part of a Project's synchronized state advances the Project's revision
    /// when it changes.
    /// </summary>
    private static async Task CreateRevisionTriggersAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        const string ByTracker = "(SELECT project_id FROM trackers WHERE id = {0}.tracker_id)";
        const string ByEntity = "(SELECT tracker.project_id FROM tracked_entities entity JOIN trackers tracker ON tracker.id = entity.tracker_id WHERE entity.id = {0}.entity_id)";
        const string ByBaselineEntity = "(SELECT tracker.project_id FROM tracker_sync_baseline_entities baseline JOIN trackers tracker ON tracker.id = baseline.tracker_id WHERE baseline.id = {0}.baseline_entity_id)";
        (string Table, string Project)[] tables =
        [
            ("trackers", "{0}.project_id"),
            ("tracked_entities", ByTracker),
            ("project_developers", "{0}.project_id"),
            ("entity_status_history", ByEntity),
            ("progress_snapshots", ByTracker),
            ("tracker_sync_baselines", ByTracker),
            ("tracker_sync_baseline_entities", ByTracker),
            ("tracker_sync_baseline_dependencies", ByBaselineEntity)
        ];
        foreach ((string table, string project) in tables)
        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            string name = $"rev_{table}_{operation.ToLowerInvariant()}";
            string row = operation == "DELETE" ? "OLD" : "NEW";
            await ExecuteAsync(connection, transaction,
                $"DROP TRIGGER IF EXISTS {name}; " +
                $"CREATE TRIGGER {name} AFTER {operation} ON {table} BEGIN " +
                $"UPDATE project_revisions SET revision = revision + 1 WHERE project_id = {string.Format(CultureInfo.InvariantCulture, project, row)}; END;",
                cancellationToken);
        }

        await ExecuteAsync(connection, transaction, """
            DROP TRIGGER IF EXISTS rev_project_insert;
            DROP TRIGGER IF EXISTS rev_project_update;
            DROP TRIGGER IF EXISTS rev_project_delete;
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
