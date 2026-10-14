using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>Sets an entity_dependencies row's $dependentEntityId and $targetId.</summary>
internal static class SqliteDependencyValues
{
    public static void Add(SqliteCommand command, EntityDependency dependency)
    {
        command.Parameters.AddWithValue(
            "$dependentEntityId", SqlitePersistenceValues.Format(dependency.DependentEntityId));
        command.Parameters.AddWithValue(
            "$targetId", SqlitePersistenceValues.Format(dependency.DependencyEntityId));
    }
}
