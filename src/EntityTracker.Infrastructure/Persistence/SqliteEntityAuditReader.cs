using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteEntityAuditReader(SqliteDatabase database) : IEntityAuditReader
{
    private readonly SqliteDatabase _database = database ??
        throw new ArgumentNullException(nameof(database));

    public async Task<IReadOnlyList<EntityAuditTimestamps>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);

        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at_utc, schema_updated_at_utc, progress_updated_at_utc
            FROM tracked_entities
            WHERE tracker_id = $trackerId
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<EntityAuditTimestamps> timestamps = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            timestamps.Add(new EntityAuditTimestamps(
                SqlitePersistenceValues.ParseEntityId(reader.GetString(0)),
                SqlitePersistenceValues.ParseTimestamp(reader.GetString(1), "entity creation timestamp"),
                SqlitePersistenceValues.ParseTimestamp(reader.GetString(2), "entity schema timestamp"),
                SqlitePersistenceValues.ParseTimestamp(reader.GetString(3), "entity progress timestamp")));
        }

        return timestamps.AsReadOnly();
    }
}
