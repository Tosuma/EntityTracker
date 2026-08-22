using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

public sealed class EntityRanking
{
    internal EntityRanking(
        EntityId entityId,
        int rank,
        int impactScore,
        IEnumerable<EntityId> directDependencies,
        IEnumerable<EntityId> directDependents)
    {
        EntityId = entityId;
        Rank = rank;
        ImpactScore = impactScore;
        DirectDependencies = Array.AsReadOnly(directDependencies.ToArray());
        DirectDependents = Array.AsReadOnly(directDependents.ToArray());
    }

    public EntityId EntityId { get; }

    /// <summary>
    /// Gets the one-based position in the dependency-safe ordering.
    /// </summary>
    public int Rank { get; }

    /// <summary>
    /// Gets the number of unique entities transitively downstream from this entity.
    /// </summary>
    public int ImpactScore { get; }

    public IReadOnlyList<EntityId> DirectDependencies { get; }

    public IReadOnlyList<EntityId> DirectDependents { get; }
}
