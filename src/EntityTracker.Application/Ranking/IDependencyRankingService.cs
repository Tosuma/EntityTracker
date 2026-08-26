using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

public interface IDependencyRankingService
{
    DependencyRankingResult Rank(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<DependencyEdge> dependencyEdges,
        IEnumerable<UnresolvedDependency> unresolvedDependencies);
}
