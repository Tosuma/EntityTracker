using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

public sealed class ArchivedEntityDetails
{
    internal ArchivedEntityDetails(
        TrackedEntity entity,
        IEnumerable<EntityDependencyEditItem> dependencies)
    {
        Entity = entity;
        Dependencies = dependencies.ToArray();
    }

    public TrackedEntity Entity { get; }

    public IReadOnlyList<EntityDependencyEditItem> Dependencies { get; }
}
