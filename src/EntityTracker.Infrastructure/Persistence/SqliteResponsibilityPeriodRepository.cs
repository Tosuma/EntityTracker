using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteResponsibilityPeriodRepository(SqliteDatabase database)
    : IResponsibilityPeriodRepository
{
    public Task<IReadOnlyList<ResponsibilityPeriod>> GetByEntityAsync(EntityId entityId,
        CancellationToken cancellationToken = default) => ReadAsync(
        "WHERE period.entity_id = $id", SqlitePersistenceValues.Format(entityId), cancellationToken);

    public Task<IReadOnlyList<ResponsibilityPeriod>> GetByTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default) => ReadAsync(
        "JOIN tracked_entities entity ON entity.id = period.entity_id WHERE entity.tracker_id = $id",
        SqlitePersistenceValues.Format(trackerId), cancellationToken);

    private async Task<IReadOnlyList<ResponsibilityPeriod>> ReadAsync(string scope, string id,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT period.id, period.entity_id, period.developer_id,
                   period.started_at_utc, period.ended_at_utc
            FROM responsibility_periods period {scope}
            ORDER BY period.started_at_utc, period.id;
            """;
        command.Parameters.AddWithValue("$id", id);
        List<ResponsibilityPeriod> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ResponsibilityPeriod(Guid.Parse(reader.GetString(0)),
                new EntityId(Guid.Parse(reader.GetString(1))),
                new DeveloperId(Guid.Parse(reader.GetString(2))),
                DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4),
                    System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }
}
