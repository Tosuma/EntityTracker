using System.Globalization;

using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Version 29: the dependency history. Like the status history it is append-only, so a past day's ready
/// and waiting entities can be worked out from how things were then (#179). Each entry is a dependency
/// added or removed, an entity's lifecycle state, or an entity leaving its Tracker. Entries keep their
/// entity's ID without a foreign key, so a placeholder that is gone still explains the days it blocked.
/// </summary>
internal static class SqliteDependencyHistory
{
    internal const string TableSql = """
        CREATE TABLE IF NOT EXISTS entity_dependency_history
        (
            event_id TEXT NOT NULL PRIMARY KEY CHECK (length(event_id) = 36),
            tracker_id TEXT NOT NULL,
            entity_id TEXT NOT NULL,
            change TEXT NOT NULL
                CHECK (change IN ('DependencyAdded', 'DependencyRemoved', 'Lifecycle', 'Removed')),
            dependency_entity_id TEXT NULL,
            lifecycle_state TEXT NULL
                CHECK (lifecycle_state IS NULL OR lifecycle_state IN ('Active', 'Archived', 'Placeholder')),
            is_baseline INTEGER NOT NULL CHECK (is_baseline IN (0, 1)),
            occurred_at_utc TEXT NOT NULL,
            CHECK ((change IN ('DependencyAdded', 'DependencyRemoved')) = (dependency_entity_id IS NOT NULL)),
            CHECK ((change = 'Lifecycle') = (lifecycle_state IS NOT NULL)),
            CHECK (dependency_entity_id IS NULL OR dependency_entity_id <> entity_id),
            FOREIGN KEY (tracker_id) REFERENCES trackers (id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_entity_dependency_history_tracker
            ON entity_dependency_history (tracker_id, occurred_at_utc);
        """;

    /// <summary>Creates the table, and gives every Tracker a baseline of how its dependencies are now.</summary>
    internal static async Task MigrateAsync(SqliteConnection connection, SqliteTransaction transaction,
        DateTimeOffset migratedAtUtc, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, TableSql, cancellationToken);
        // The history is part of the Tracker, so adding to it advances its Project's revision.
        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            string name = $"rev_entity_dependency_history_{operation.ToLowerInvariant()}";
            string row = operation == "DELETE" ? "OLD" : "NEW";
            await ExecuteAsync(connection, transaction,
                $"DROP TRIGGER IF EXISTS {name}; " +
                $"CREATE TRIGGER {name} AFTER {operation} ON entity_dependency_history BEGIN " +
                "UPDATE project_revisions SET revision = revision + 1 WHERE project_id = " +
                $"(SELECT project_id FROM trackers WHERE id = {row}.tracker_id); END;",
                cancellationToken);
        }

        string timestamp = SqlitePersistenceValues.FormatTimestamp(migratedAtUtc);
        List<string> trackers = [];
        using (SqliteCommand command = Command(connection, transaction, "SELECT id FROM trackers ORDER BY id;"))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                trackers.Add(reader.GetString(0));
        foreach (string tracker in trackers)
            await RecordAsync(connection, transaction, tracker, timestamp, cancellationToken);
    }

    /// <summary>
    /// Records how the Tracker's dependencies and entities differ from what its dependency history says,
    /// so every path that changes them is covered by one writer, and nothing is recorded when nothing
    /// changed. A Tracker without any history whose entities are older than now (one from before the
    /// history, or from an older snapshot) gets a baseline: how things already were, not a change now.
    /// </summary>
    internal static async Task RecordAsync(SqliteConnection connection, SqliteTransaction transaction,
        string trackerId, string timestamp, CancellationToken cancellationToken)
    {
        // What the history says now: each entity's last lifecycle entry, and the dependencies it has.
        Dictionary<string, string> recordedLifecycle = new(StringComparer.Ordinal);
        HashSet<(string Entity, string Dependency)> recordedDependencies = [];
        bool hasHistory = false;
        using (SqliteCommand command = Command(connection, transaction, """
                   SELECT entity_id, change, dependency_entity_id, lifecycle_state
                   FROM entity_dependency_history WHERE tracker_id = $trackerId
                   ORDER BY occurred_at_utc, event_id;
                   """))
        {
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hasHistory = true;
                string entity = reader.GetString(0);
                switch (reader.GetString(1))
                {
                    case nameof(DependencyHistoryChange.DependencyAdded):
                        recordedDependencies.Add((entity, reader.GetString(2)));
                        break;
                    case nameof(DependencyHistoryChange.DependencyRemoved):
                        recordedDependencies.Remove((entity, reader.GetString(2)));
                        break;
                    case nameof(DependencyHistoryChange.Lifecycle):
                        recordedLifecycle[entity] = reader.GetString(3);
                        break;
                    default:
                        recordedLifecycle[entity] = nameof(DependencyHistoryChange.Removed);
                        break;
                }
            }
        }

        Dictionary<string, string> lifecycle = new(StringComparer.Ordinal);
        using (SqliteCommand command = Command(connection, transaction,
                   "SELECT id, lifecycle_state FROM tracked_entities WHERE tracker_id = $trackerId ORDER BY id;"))
        {
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                lifecycle[reader.GetString(0)] = reader.GetString(1);
        }

        List<(string Entity, string Dependency)> dependencies = [];
        using (SqliteCommand command = Command(connection, transaction, """
                   SELECT dependency.dependent_entity_id, dependency.dependency_entity_id
                   FROM entity_dependencies dependency
                   JOIN tracked_entities entity ON entity.id = dependency.dependent_entity_id
                   WHERE entity.tracker_id = $trackerId
                   ORDER BY dependency.dependent_entity_id, dependency.dependency_entity_id;
                   """))
        {
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                dependencies.Add((reader.GetString(0), reader.GetString(1)));
        }

        bool baseline = false;
        if (!hasHistory)
        {
            using SqliteCommand older = Command(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM tracked_entities WHERE tracker_id = $trackerId AND created_at_utc < $now);");
            older.Parameters.AddWithValue("$trackerId", trackerId);
            older.Parameters.AddWithValue("$now", timestamp);
            baseline = Convert.ToInt64(await older.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
        }
        foreach ((string entity, string state) in lifecycle)
            if (!recordedLifecycle.TryGetValue(entity, out string? recorded) || recorded != state)
                await InsertAsync(DependencyHistoryChange.Lifecycle, entity, null, state);
        foreach ((string entity, string recorded) in recordedLifecycle.OrderBy(static item => item.Key, StringComparer.Ordinal))
            if (recorded != nameof(DependencyHistoryChange.Removed) && !lifecycle.ContainsKey(entity))
                await InsertAsync(DependencyHistoryChange.Removed, entity, null, null);
        HashSet<(string Entity, string Dependency)> current = [.. dependencies];
        foreach ((string entity, string dependency) in recordedDependencies
                     .Where(item => !current.Contains(item))
                     .OrderBy(static item => item.Entity, StringComparer.Ordinal)
                     .ThenBy(static item => item.Dependency, StringComparer.Ordinal))
            await InsertAsync(DependencyHistoryChange.DependencyRemoved, entity, dependency, null);
        foreach ((string entity, string dependency) in dependencies.Where(item => !recordedDependencies.Contains(item)))
            await InsertAsync(DependencyHistoryChange.DependencyAdded, entity, dependency, null);

        async Task InsertAsync(DependencyHistoryChange change, string entity, string? dependency, string? state)
        {
            using SqliteCommand insert = Command(connection, transaction, """
                INSERT INTO entity_dependency_history
                    (event_id, tracker_id, entity_id, change, dependency_entity_id, lifecycle_state, is_baseline, occurred_at_utc)
                VALUES ($eventId, $trackerId, $entityId, $change, $dependency, $state, $baseline, $occurredAt);
                """);
            insert.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$trackerId", trackerId);
            insert.Parameters.AddWithValue("$entityId", entity);
            insert.Parameters.AddWithValue("$change", change.ToString());
            insert.Parameters.AddWithValue("$dependency", (object?)dependency ?? DBNull.Value);
            insert.Parameters.AddWithValue("$state", (object?)state ?? DBNull.Value);
            insert.Parameters.AddWithValue("$baseline", baseline ? 1 : 0);
            insert.Parameters.AddWithValue("$occurredAt", timestamp);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>Reads a Tracker's dependency history, oldest first.</summary>
    internal static async Task<IReadOnlyList<DependencyHistoryEntry>> ReadAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string trackerId, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction, """
            SELECT event_id, entity_id, change, dependency_entity_id, lifecycle_state, is_baseline, occurred_at_utc
            FROM entity_dependency_history WHERE tracker_id = $trackerId
            ORDER BY occurred_at_utc, event_id;
            """);
        command.Parameters.AddWithValue("$trackerId", trackerId);
        List<DependencyHistoryEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new DependencyHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                new EntityId(Guid.Parse(reader.GetString(1))),
                Enum.Parse<DependencyHistoryChange>(reader.GetString(2)),
                SqlitePersistenceValues.ParseTimestamp(reader.GetString(6), "dependency history time"),
                reader.IsDBNull(3) ? null : new EntityId(Guid.Parse(reader.GetString(3))),
                reader.IsDBNull(4) ? null : Enum.Parse<EntityLifecycleState>(reader.GetString(4)),
                reader.GetInt64(5) == 1));
        return entries;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
