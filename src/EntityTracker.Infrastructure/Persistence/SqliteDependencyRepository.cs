using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteDependencyRepository : IDependencyRepository
{
    private readonly SqliteDatabase _database;

    public SqliteDependencyRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    internal SqliteDatabase Database => _database;

    public async Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_entity_id, dependency_kind
            FROM schema_dependencies dependency
            INNER JOIN tracked_entities owner ON owner.id = dependency.dependent_entity_id
            WHERE owner.tracker_id = $trackerId
            ORDER BY dependent_entity_id, dependency_entity_id;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<PersistedDependency> dependencies = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            EntityId dependentId = SqlitePersistenceValues.ParseEntityId(reader.GetString(0));
            EntityId dependencyId = SqlitePersistenceValues.ParseEntityId(reader.GetString(1));
            ImportedDependencyKind kind =
                SqlitePersistenceValues.ParseEnum<ImportedDependencyKind>(
                    reader.GetString(2),
                    "dependency kind");
            dependencies.Add(new PersistedDependency(
                new DependencyEdge(dependentId, dependencyId),
                kind));
        }

        return dependencies.AsReadOnly();
    }

    public async Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_source_name, dependency_kind
            FROM unresolved_schema_dependencies dependency
            INNER JOIN tracked_entities owner ON owner.id = dependency.dependent_entity_id
            WHERE owner.tracker_id = $trackerId
            ORDER BY dependent_entity_id, dependency_source_key;
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));

        List<PersistedUnresolvedDependency> dependencies = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            EntityId dependentId = SqlitePersistenceValues.ParseEntityId(reader.GetString(0));
            string dependencySourceName = reader.GetString(1);
            ImportedDependencyKind kind =
                SqlitePersistenceValues.ParseEnum<ImportedDependencyKind>(
                    reader.GetString(2),
                    "dependency kind");
            dependencies.Add(new PersistedUnresolvedDependency(
                new UnresolvedDependency(dependentId, dependencySourceName),
                kind));
        }

        return dependencies.AsReadOnly();
    }

    internal async Task SaveAsync(
        TrackerId trackerId,
        PersistedDependency dependency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(dependency);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await EnsureOwnedAsync(
            connection,
            trackerId,
            [dependency.Edge.DependentEntityId, dependency.Edge.DependencyEntityId],
            cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_dependencies
            (
                dependent_entity_id,
                dependency_entity_id,
                dependency_kind,
                created_at_utc,
                updated_at_utc
            )
            VALUES
            (
                $dependentEntityId,
                $dependencyEntityId,
                $dependencyKind,
                $createdAtUtc,
                $updatedAtUtc
            )
            ON CONFLICT (dependent_entity_id, dependency_entity_id)
            DO UPDATE SET
                dependency_kind = excluded.dependency_kind,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue(
            "$dependentEntityId",
            SqlitePersistenceValues.Format(dependency.Edge.DependentEntityId));
        command.Parameters.AddWithValue(
            "$dependencyEntityId",
            SqlitePersistenceValues.Format(dependency.Edge.DependencyEntityId));
        command.Parameters.AddWithValue("$dependencyKind", dependency.Kind.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", timestamp);
        command.Parameters.AddWithValue("$updatedAtUtc", timestamp);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The dependency cannot be stored because its entity references are invalid.",
                exception);
        }
    }

    internal async Task SaveUnresolvedAsync(
        TrackerId trackerId,
        PersistedUnresolvedDependency dependency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(dependency);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await EnsureOwnedAsync(
            connection,
            trackerId,
            [dependency.Dependency.DependentEntityId],
            cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO unresolved_schema_dependencies
            (
                dependent_entity_id,
                dependency_source_key,
                dependency_source_name,
                dependency_kind,
                created_at_utc,
                updated_at_utc
            )
            VALUES
            (
                $dependentEntityId,
                $dependencySourceKey,
                $dependencySourceName,
                $dependencyKind,
                $createdAtUtc,
                $updatedAtUtc
            )
            ON CONFLICT (dependent_entity_id, dependency_source_key)
            DO UPDATE SET
                dependency_source_name = excluded.dependency_source_name,
                dependency_kind = excluded.dependency_kind,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue(
            "$dependentEntityId",
            SqlitePersistenceValues.Format(dependency.Dependency.DependentEntityId));
        command.Parameters.AddWithValue(
            "$dependencySourceKey",
            EntitySourceKey.From(dependency.Dependency.DependencySourceName).Value);
        command.Parameters.AddWithValue(
            "$dependencySourceName",
            dependency.Dependency.DependencySourceName);
        command.Parameters.AddWithValue("$dependencyKind", dependency.Kind.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", timestamp);
        command.Parameters.AddWithValue("$updatedAtUtc", timestamp);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The unresolved dependency cannot be stored because its dependent entity is invalid.",
                exception);
        }
    }

    private static async Task EnsureOwnedAsync(
        SqliteConnection connection,
        TrackerId trackerId,
        IReadOnlyCollection<EntityId> entityIds,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM tracked_entities
            WHERE tracker_id = $trackerId
              AND id IN ({string.Join(", ", entityIds.Select((_, index) => $"$entityId{index}"))});
            """;
        command.Parameters.AddWithValue("$trackerId", SqlitePersistenceValues.Format(trackerId));
        int parameterIndex = 0;
        foreach (EntityId entityId in entityIds)
        {
            command.Parameters.AddWithValue(
                $"$entityId{parameterIndex++}",
                SqlitePersistenceValues.Format(entityId));
        }

        long ownedCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (ownedCount != entityIds.Distinct().Count())
        {
            throw new InvalidOperationException(
                "The dependency references an entity outside the selected tracker.");
        }
    }
}
