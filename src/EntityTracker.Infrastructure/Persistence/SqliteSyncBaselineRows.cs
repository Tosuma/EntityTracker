using EntityTracker.Application.Tracking;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Reads and writes a Tracker's sync baseline (tracker_sync_baselines and its entity and dependency
/// rows). Entities and dependencies are kept in name order, the order the baseline is captured in.
/// </summary>
internal static class SqliteSyncBaselineRows
{
    private const string Source = "Source";
    private const string Destination = "Destination";

    /// <summary>Replaces the Tracker's baseline.</summary>
    public static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string trackerId,
        TrackerSyncBaseline baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        await DeleteAsync(connection, transaction, trackerId, cancellationToken);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO tracker_sync_baselines (tracker_id) VALUES ($trackerId);",
            cancellationToken, ("$trackerId", trackerId));
        await WriteSideAsync(Source, baseline.Source);
        await WriteSideAsync(Destination, baseline.Destination);

        async Task WriteSideAsync(string side, TrackerSyncStructure structure)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (TrackerSyncEntity entity in structure.Entities)
            {
                if (!names.Add(Key(entity.Name))) continue;
                string id = Guid.NewGuid().ToString("D");
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO tracker_sync_baseline_entities
                    (id, tracker_id, side, name, is_active, requested_priority, group_name, filter_active, due_date)
                    VALUES ($id, $trackerId, $side, $name, $active, $priority, $group, $filterActive, $dueDate);
                    """, cancellationToken,
                    ("$id", id), ("$trackerId", trackerId), ("$side", side), ("$name", entity.Name),
                    ("$active", entity.Active ? 1 : 0), ("$priority", entity.RequestedPriority),
                    ("$group", entity.GroupName), ("$filterActive", entity.FilterActive),
                    ("$dueDate", SqlitePersistenceValues.FormatDate(entity.DueDate)));
                foreach (TrackerSyncDependency dependency in entity.Dependencies
                             .DistinctBy(static item => Key(item.Name)))
                {
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO tracker_sync_baseline_dependencies (baseline_entity_id, dependency_name)
                        VALUES ($id, $name);
                        """, cancellationToken, ("$id", id), ("$name", dependency.Name));
                }
            }
        }
    }

    public static Task DeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string trackerId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction,
            "DELETE FROM tracker_sync_baselines WHERE tracker_id = $trackerId;",
            cancellationToken, ("$trackerId", trackerId));

    /// <summary>The Tracker's baseline, or null when it has none.</summary>
    public static async Task<TrackerSyncBaseline?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string trackerId,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText = "SELECT COUNT(*) FROM tracker_sync_baselines WHERE tracker_id = $trackerId;";
            exists.Parameters.AddWithValue("$trackerId", trackerId);
            if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture) == 0)
                return null;
        }

        Dictionary<string, List<string>> dependencies = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT dependency.baseline_entity_id, dependency.dependency_name
                FROM tracker_sync_baseline_dependencies dependency
                JOIN tracker_sync_baseline_entities entity ON entity.id = dependency.baseline_entity_id
                WHERE entity.tracker_id = $trackerId
                ORDER BY dependency.dependency_name;
                """;
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!dependencies.TryGetValue(reader.GetString(0), out List<string>? list))
                    dependencies[reader.GetString(0)] = list = [];
                list.Add(reader.GetString(1));
            }
        }

        List<TrackerSyncEntity> source = [];
        List<TrackerSyncEntity> destination = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, side, name, is_active, requested_priority, group_name, filter_active, due_date
                FROM tracker_sync_baseline_entities
                WHERE tracker_id = $trackerId
                ORDER BY name;
                """;
            command.Parameters.AddWithValue("$trackerId", trackerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                TrackerSyncEntity entity = new(
                    reader.GetString(2),
                    reader.GetInt64(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.GetString(5),
                    (dependencies.GetValueOrDefault(reader.GetString(0)) ?? [])
                        .Select(static name => new TrackerSyncDependency(name)).ToArray(),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    SqlitePersistenceValues.ParseDate(reader.IsDBNull(7) ? null : reader.GetString(7),
                        "baseline due date"));
                (reader.GetString(1) == Source ? source : destination).Add(entity);
            }
        }

        return new TrackerSyncBaseline(new TrackerSyncStructure(source), new TrackerSyncStructure(destination));
    }

    private static string Key(string name) => name.Trim().ToUpperInvariant();

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
