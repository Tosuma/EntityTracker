using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Describes a complete atomic mutation of tracked entities and their dependencies.
/// </summary>
public sealed class TrackedStateChangeSet
{
    public TrackedStateChangeSet(
        IEnumerable<TrackedEntity> entitiesToAdd,
        IEnumerable<TrackedEntity> entitiesToUpdate,
        IEnumerable<EntityId> entityIdsToArchive,
        IEnumerable<EntityId>? dependencyOwnerIds = null,
        IEnumerable<EntityDependency>? dependencies = null,
        IEnumerable<TrackedEntity>? entitiesWithProgressToUpdate = null,
        IEnumerable<EntityId>? entityIdsToRestore = null,
        ProgressSnapshotState? progressSnapshotAfterChanges = null,
        IEnumerable<TrackedEntity>? entitiesWithRequestedPriorityToUpdate = null,
        IEnumerable<TrackedEntity>? entitiesWithResponsibleDeveloperToUpdate = null,
        IEnumerable<TrackedEntity>? entitiesWithGroupNameToUpdate = null,
        IEnumerable<EntityId>? entityIdsToPurge = null,
        IEnumerable<ResponsibilitySelection>? responsibilitySelections = null,
        IEnumerable<ResponsibilityPeriod>? responsibilityPeriodsToCopy = null,
        IEnumerable<ResponsibilityAddition>? responsibilityAdditions = null,
        IEnumerable<ResponsibilityRemoval>? responsibilityRemovals = null,
        IEnumerable<TrackedEntity>? entitiesWithDueDateToUpdate = null)
    {
        EntitiesToAdd = entitiesToAdd.ToArray();
        EntitiesToUpdate = entitiesToUpdate.ToArray();
        EntityIdsToArchive = entityIdsToArchive.ToArray();
        DependencyOwnerIds = (dependencyOwnerIds ?? []).ToArray();
        Dependencies = (dependencies ?? []).ToArray();
        EntitiesWithProgressToUpdate = (entitiesWithProgressToUpdate ?? []).ToArray();
        EntityIdsToRestore = (entityIdsToRestore ?? []).ToArray();
        ProgressSnapshotAfterChanges = progressSnapshotAfterChanges;
        EntitiesWithRequestedPriorityToUpdate =
            (entitiesWithRequestedPriorityToUpdate ?? []).ToArray();
        EntitiesWithResponsibleDeveloperToUpdate =
            (entitiesWithResponsibleDeveloperToUpdate ?? []).ToArray();
        EntitiesWithGroupNameToUpdate =
            (entitiesWithGroupNameToUpdate ?? []).ToArray();
        EntityIdsToPurge = (entityIdsToPurge ?? []).ToArray();
        ResponsibilitySelections = (responsibilitySelections ?? []).ToArray();
        ResponsibilityPeriodsToCopy = (responsibilityPeriodsToCopy ?? []).ToArray();
        ResponsibilityAdditions = (responsibilityAdditions ?? []).ToArray();
        ResponsibilityRemovals = (responsibilityRemovals ?? []).ToArray();
        EntitiesWithDueDateToUpdate = (entitiesWithDueDateToUpdate ?? []).ToArray();
    }

    public IReadOnlyList<TrackedEntity> EntitiesToAdd { get; }

    public IReadOnlyList<TrackedEntity> EntitiesToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToArchive { get; }

    /// <summary>The entities whose dependency lists are replaced by <see cref="Dependencies"/>.</summary>
    public IReadOnlyList<EntityId> DependencyOwnerIds { get; }

    /// <summary>The complete dependency lists of <see cref="DependencyOwnerIds"/>.</summary>
    public IReadOnlyList<EntityDependency> Dependencies { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithProgressToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToRestore { get; }

    public ProgressSnapshotState? ProgressSnapshotAfterChanges { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithRequestedPriorityToUpdate { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithResponsibleDeveloperToUpdate { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithGroupNameToUpdate { get; }

    public IReadOnlyList<TrackedEntity> EntitiesWithDueDateToUpdate { get; }

    public IReadOnlyList<EntityId> EntityIdsToPurge { get; }
    public IReadOnlyList<ResponsibilitySelection> ResponsibilitySelections { get; }
    public IReadOnlyList<ResponsibilityPeriod> ResponsibilityPeriodsToCopy { get; }
    public IReadOnlyList<ResponsibilityAddition> ResponsibilityAdditions { get; }
    public IReadOnlyList<ResponsibilityRemoval> ResponsibilityRemovals { get; }

    public bool HasChanges =>
        EntitiesToAdd.Count > 0 ||
        EntitiesToUpdate.Count > 0 ||
        EntityIdsToArchive.Count > 0 ||
        DependencyOwnerIds.Count > 0 ||
        EntitiesWithProgressToUpdate.Count > 0 ||
        EntityIdsToRestore.Count > 0 ||
            EntitiesWithRequestedPriorityToUpdate.Count > 0 ||
            EntitiesWithResponsibleDeveloperToUpdate.Count > 0 ||
            EntitiesWithGroupNameToUpdate.Count > 0 ||
            EntitiesWithDueDateToUpdate.Count > 0 ||
            EntityIdsToPurge.Count > 0 ||
            ResponsibilitySelections.Count > 0 ||
            ResponsibilityPeriodsToCopy.Count > 0 ||
            ResponsibilityAdditions.Count > 0 ||
            ResponsibilityRemovals.Count > 0;
}
