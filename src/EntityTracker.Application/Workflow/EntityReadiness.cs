using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

public sealed class EntityReadiness
{
    internal EntityReadiness(EntityId entityId, IEnumerable<DependencyBlocker> blockers)
    {
        EntityId = entityId;
        Blockers = blockers.ToArray();
    }

    public EntityId EntityId { get; }

    public IReadOnlyList<DependencyBlocker> Blockers { get; }

    public bool IsReady => Blockers.Count == 0;
}
