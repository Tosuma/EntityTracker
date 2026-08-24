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

    public async Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_entity_id, dependency_kind
            FROM schema_dependencies
            ORDER BY dependent_entity_id, dependency_entity_id;
            """;

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
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT dependent_entity_id, dependency_source_name, dependency_kind
            FROM unresolved_schema_dependencies
            ORDER BY dependent_entity_id, dependency_source_key;
            """;

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
        PersistedDependency dependency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
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
        PersistedUnresolvedDependency dependency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        string timestamp = SqlitePersistenceValues.FormatTimestamp(
            _database.TimeProvider.GetUtcNow());
        await using SqliteConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
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
}
