namespace EntityTracker.Domain;

public sealed record DependencyEdge
{
    public DependencyEdge(EntityId dependentEntityId, EntityId dependencyEntityId)
    {
        ArgumentNullException.ThrowIfNull(dependentEntityId);
        ArgumentNullException.ThrowIfNull(dependencyEntityId);

        if (dependentEntityId == dependencyEntityId)
        {
            throw new ArgumentException(
                "An entity cannot depend on itself.",
                nameof(dependencyEntityId));
        }

        DependentEntityId = dependentEntityId;
        DependencyEntityId = dependencyEntityId;
    }

    public EntityId DependentEntityId { get; }

    public EntityId DependencyEntityId { get; }
}
