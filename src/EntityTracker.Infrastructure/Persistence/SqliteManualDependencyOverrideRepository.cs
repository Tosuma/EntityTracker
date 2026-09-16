using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteManualDependencyOverrideRepository
    : IManualDependencyOverrideRepository
{
    private readonly SqliteDatabase _database;

    public SqliteManualDependencyOverrideRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    internal SqliteDatabase Database => _database;

    public async Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_source_name, override_action
            FROM manual_dependency_overrides dependency_override
            INNER JOIN tracked_entities owner
                ON owner.id = dependency_override.dependent_entity_id
            WHERE owner.tracker_id = $trackerId
            ORDER BY dependent_entity_id, dependency_source_key;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<ManualDependencyOverride> overrides = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            overrides.Add(new ManualDependencyOverride(
                SqlitePersistenceValues.ParseEntityId(reader.GetString(0)),
                reader.GetString(1),
                SqlitePersistenceValues.ParseEnum<ManualDependencyOverrideAction>(
                    reader.GetString(2),
                    "manual dependency override action")));
        }

        return overrides.AsReadOnly();
    }
}
