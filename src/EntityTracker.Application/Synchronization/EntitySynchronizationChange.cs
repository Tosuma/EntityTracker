using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public enum EntitySynchronizationChangeKind
{
    New,
    Changed,
    Missing
}

public sealed class EntitySynchronizationChange
{
    public EntitySynchronizationChange(
        TrackedEntity entity,
        EntitySynchronizationChangeKind changeKind,
        IEnumerable<DependencySynchronizationChange>? dependencyChanges = null,
        bool isReactivation = false,
        DependencyResolutionState resolutionState = DependencyResolutionState.Resolved,
        IEnumerable<string>? missingDependencyNames = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!Enum.IsDefined(changeKind))
        {
            throw new ArgumentOutOfRangeException(nameof(changeKind));
        }

        Entity = entity;
        ChangeKind = changeKind;
        DependencyChanges = (dependencyChanges ?? []).ToArray();
        IsReactivation = isReactivation;
        ResolutionState = resolutionState;
        MissingDependencyNames = (missingDependencyNames ?? []).ToArray();
    }

    public TrackedEntity Entity { get; }

    public EntitySynchronizationChangeKind ChangeKind { get; }

    public IReadOnlyList<DependencySynchronizationChange> DependencyChanges { get; }

    public bool IsReactivation { get; }

    public DependencyResolutionState ResolutionState { get; }

    public IReadOnlyList<string> MissingDependencyNames { get; }
}
