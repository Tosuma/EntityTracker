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

    public async Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_source_name, override_action
            FROM manual_dependency_overrides
            ORDER BY dependent_entity_id, dependency_source_key;
            """;

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
