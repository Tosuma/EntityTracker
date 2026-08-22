using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

/// <summary>
/// Describes the complete persistence mutation for one approved synchronization.
/// </summary>
public sealed class SchemaSynchronizationChangeSet
{
    public SchemaSynchronizationChangeSet(
        IEnumerable<TrackedEntity> entitiesToAdd,
        IEnumerable<TrackedEntity> entitiesToUpdate,
        IEnumerable<EntityId> entityIdsToArchive,
        IEnumerable<EntityId> reconciledOwnerIds,
        IEnumerable<PersistedDependency> resolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies)
    {
        EntitiesToAdd = entitiesToAdd.ToArray();
        EntitiesToUpdate = entitiesToUpdate.ToArray();
        EntityIdsToArchive = entityIdsToArchive.ToArray();
        ReconciledOwnerIds = reconciledOwnerIds.ToArray();
        ResolvedDependencies = resolvedDependencies.ToArray();
        UnresolvedDependencies = unresolvedDependencies.ToArray();
    }

    public IReadOnlyList<TrackedEntity> EntitiesToAdd { get; }

    public IReadOnlyList<TrackedEntity> EntitiesToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToArchive { get; }

    public IReadOnlyList<EntityId> ReconciledOwnerIds { get; }

    public IReadOnlyList<PersistedDependency> ResolvedDependencies { get; }

    public IReadOnlyList<PersistedUnresolvedDependency> UnresolvedDependencies { get; }

    public bool HasChanges =>
        EntitiesToAdd.Count > 0 ||
        EntitiesToUpdate.Count > 0 ||
        EntityIdsToArchive.Count > 0 ||
        ReconciledOwnerIds.Count > 0;
}
