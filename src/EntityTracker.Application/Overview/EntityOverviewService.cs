using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly DependencyRanker _dependencyRanker;

    public EntityOverviewService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        DependencyRanker dependencyRanker)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _dependencyRanker = dependencyRanker;
    }

    public async Task<EntityOverviewResult> GetAsync(
        CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TrackedEntity>> entityTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependencyTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedDependencyTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);

        await Task.WhenAll(entityTask, dependencyTask, unresolvedDependencyTask);

        IReadOnlyList<TrackedEntity> entities = await entityTask;
        IReadOnlyList<PersistedDependency> persistedDependencies = await dependencyTask;
        IReadOnlyList<PersistedUnresolvedDependency> persistedUnresolvedDependencies =
            await unresolvedDependencyTask;

        DependencyRankingResult rankingResult = await Task.Run(
            () => _dependencyRanker.Rank(
                entities,
                persistedDependencies.Select(static dependency => dependency.Edge),
                persistedUnresolvedDependencies.Select(
                    static dependency => dependency.Dependency)),
            cancellationToken);

        if (!rankingResult.IsSuccess)
        {
            return new EntityOverviewResult([], rankingResult.Diagnostics);
        }

        IReadOnlyDictionary<EntityId, TrackedEntity> entitiesById =
            entities.ToDictionary(static entity => entity.Id);

        Dictionary<EntityId, int> dependencyCounts = entities.ToDictionary(
            static entity => entity.Id,
            static _ => 0);

        foreach (PersistedDependency dependency in persistedDependencies)
        {
            dependencyCounts[dependency.Edge.DependentEntityId]++;
        }

        foreach (PersistedUnresolvedDependency dependency in persistedUnresolvedDependencies)
        {
            dependencyCounts[dependency.Dependency.DependentEntityId]++;
        }

        IEnumerable<EntityOverviewItem> rankedItems = rankingResult.Rankings
            .Select(ranking =>
            {
                TrackedEntity entity = entitiesById[ranking.EntityId];
                return new EntityOverviewItem(
                    entity.Id,
                    ranking.Rank,
                    entity.SourceName,
                    entity.Status,
                    entity.Notes,
                    dependencyCounts[entity.Id],
                    DependencyResolutionState.Resolved,
                    []);
            });

        IEnumerable<EntityOverviewItem> unrankedItems = rankingResult.UnrankedEntities
            .Select(unrankedEntity =>
            {
                TrackedEntity entity = entitiesById[unrankedEntity.EntityId];
                return new EntityOverviewItem(
                    entity.Id,
                    null,
                    entity.SourceName,
                    entity.Status,
                    entity.Notes,
                    dependencyCounts[entity.Id],
                    unrankedEntity.State,
                    unrankedEntity.MissingDependencyNames);
            });

        return new EntityOverviewResult(rankedItems.Concat(unrankedItems), []);
    }
}
