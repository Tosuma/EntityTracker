using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Describes a complete atomic mutation of tracked entities and their schema dependencies.
/// </summary>
public sealed class TrackedStateChangeSet
{
    public TrackedStateChangeSet(
        IEnumerable<TrackedEntity> entitiesToAdd,
        IEnumerable<TrackedEntity> entitiesToUpdate,
        IEnumerable<EntityId> entityIdsToArchive,
        IEnumerable<EntityId> reconciledOwnerIds,
        IEnumerable<PersistedDependency> resolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies,
        IEnumerable<EntityId>? reconciledOverrideOwnerIds = null,
        IEnumerable<ManualDependencyOverride>? manualDependencyOverrides = null,
        IEnumerable<TrackedEntity>? entitiesWithProgressToUpdate = null,
        IEnumerable<EntityId>? entityIdsToRestore = null,
        ProgressSnapshotState? progressSnapshotAfterChanges = null)
    {
        EntitiesToAdd = entitiesToAdd.ToArray();
        EntitiesToUpdate = entitiesToUpdate.ToArray();
        EntityIdsToArchive = entityIdsToArchive.ToArray();
        ReconciledOwnerIds = reconciledOwnerIds.ToArray();
        ResolvedDependencies = resolvedDependencies.ToArray();
        UnresolvedDependencies = unresolvedDependencies.ToArray();
        ReconciledOverrideOwnerIds = (reconciledOverrideOwnerIds ?? []).ToArray();
        ManualDependencyOverrides = (manualDependencyOverrides ?? []).ToArray();
        EntitiesWithProgressToUpdate = (entitiesWithProgressToUpdate ?? []).ToArray();
        EntityIdsToRestore = (entityIdsToRestore ?? []).ToArray();
        ProgressSnapshotAfterChanges = progressSnapshotAfterChanges;
    }

    public IReadOnlyList<TrackedEntity> EntitiesToAdd { get; }

    public IReadOnlyList<TrackedEntity> EntitiesToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToArchive { get; }

    public IReadOnlyList<EntityId> ReconciledOwnerIds { get; }

    public IReadOnlyList<PersistedDependency> ResolvedDependencies { get; }

    public IReadOnlyList<PersistedUnresolvedDependency> UnresolvedDependencies { get; }

    public IReadOnlyList<EntityId> ReconciledOverrideOwnerIds { get; }

    public IReadOnlyList<ManualDependencyOverride> ManualDependencyOverrides { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithProgressToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToRestore { get; }

    public ProgressSnapshotState? ProgressSnapshotAfterChanges { get; }

    public bool HasChanges =>
        EntitiesToAdd.Count > 0 ||
        EntitiesToUpdate.Count > 0 ||
        EntityIdsToArchive.Count > 0 ||
        ReconciledOwnerIds.Count > 0 ||
        ReconciledOverrideOwnerIds.Count > 0 ||
        EntitiesWithProgressToUpdate.Count > 0 ||
        EntityIdsToRestore.Count > 0;
}
