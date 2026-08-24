using System.Globalization;

using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteProgressHistoryRepository : IProgressHistoryRepository
{
    private readonly SqliteDatabase _database;

    public SqliteProgressHistoryRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT entity_id, previous_status, new_status, occurred_at_utc, entry_kind
            FROM entity_status_history
            ORDER BY occurred_at_utc, id;
            """;

        List<EntityStatusHistoryEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new EntityStatusHistoryEntry(
                SqlitePersistenceValues.ParseEntityId(reader.GetString(0)),
                reader.IsDBNull(1)
                    ? null
                    : SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(
                        reader.GetString(1),
                        "previous development status"),
                SqlitePersistenceValues.ParseEnum<DevelopmentStatus>(
                    reader.GetString(2),
                    "development status"),
                ParseTimestamp(reader.GetString(3)),
                SqlitePersistenceValues.ParseEnum<StatusHistoryEntryKind>(
                    reader.GetString(4),
                    "status history entry kind")));
        }

        return entries;
    }

    public async Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT recorded_at_utc, ready_count, blocked_count, in_progress_count,
                   rework_needed_count, development_completed_count, reconciled_count
            FROM progress_snapshots
            ORDER BY recorded_at_utc, id;
            """;

        List<ProgressSnapshot> snapshots = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new ProgressSnapshot(
                ParseTimestamp(reader.GetString(0)),
                new ProgressSnapshotState(
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6))));
        }

        return snapshots;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp))
        {
            throw new InvalidDataException($"Stored timestamp '{value}' is invalid.");
        }

        return timestamp.ToUniversalTime();
    }
}
