namespace EntityTracker.Domain;

/// <summary>
/// An entity's dependency on another entity of the same Tracker, by ID. An unresolved reference is a
/// dependency on a placeholder entity (<see cref="EntityLifecycleState.Placeholder"/>).
/// </summary>
public sealed record EntityDependency
{
    public EntityDependency(EntityId dependentEntityId, EntityId dependencyEntityId)
    {
        ArgumentNullException.ThrowIfNull(dependentEntityId);
        ArgumentNullException.ThrowIfNull(dependencyEntityId);
        if (dependentEntityId == dependencyEntityId)
            throw new ArgumentException("An entity cannot depend on itself.", nameof(dependencyEntityId));
        DependentEntityId = dependentEntityId;
        DependencyEntityId = dependencyEntityId;
    }

    public EntityId DependentEntityId { get; }

    public EntityId DependencyEntityId { get; }
}
