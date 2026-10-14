using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>Reads the dependencies the entities of a Tracker declare, from entity_dependencies.</summary>
public sealed class SqliteDependencyRepository : IDependencyRepository
{
    private readonly SqliteDatabase _database;

    public SqliteDependencyRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    internal SqliteDatabase Database => _database;

    public async Task<IReadOnlyList<EntityDependency>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependency.dependent_entity_id, dependency.dependency_entity_id
            FROM entity_dependencies dependency
            INNER JOIN tracked_entities owner
                ON owner.id = dependency.dependent_entity_id
            WHERE owner.tracker_id = $trackerId
            ORDER BY dependency.dependent_entity_id, dependency.dependency_entity_id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<EntityDependency> dependencies = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dependencies.Add(new EntityDependency(
                SqlitePersistenceValues.ParseEntityId(reader.GetString(0)),
                SqlitePersistenceValues.ParseEntityId(reader.GetString(1))));
        }

        return dependencies.AsReadOnly();
    }

    /// <summary>Adds one dependency directly; for tests that need a stored dependency.</summary>
    internal async Task SaveAsync(
        TrackerId trackerId,
        EntityDependency dependency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(dependency);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using (SqliteCommand owned = connection.CreateCommand())
        {
            owned.CommandText = "SELECT COUNT(*) FROM tracked_entities WHERE tracker_id = $trackerId AND id = $id;";
            owned.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
            owned.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(dependency.DependentEntityId));
            if (Convert.ToInt64(await owned.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException(
                    "The dependency references an entity outside the selected tracker.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entity_dependencies
            (dependent_entity_id, dependency_entity_id, created_at_utc, updated_at_utc)
            VALUES ($dependentEntityId, $targetId, $timestamp, $timestamp)
            ON CONFLICT (dependent_entity_id, dependency_entity_id)
            DO UPDATE SET updated_at_utc = excluded.updated_at_utc;
            """;
        SqliteDependencyValues.Add(command, dependency);
        command.Parameters.AddWithValue("$timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
