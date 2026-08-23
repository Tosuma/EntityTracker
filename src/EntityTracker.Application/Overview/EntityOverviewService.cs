using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly DependencyRanker _dependencyRanker;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly WorkflowReadinessEvaluator _readinessEvaluator;

    public EntityOverviewService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        DependencyRanker dependencyRanker,
        EffectiveDependencyResolver effectiveDependencyResolver,
        WorkflowReadinessEvaluator readinessEvaluator)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(readinessEvaluator);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _dependencyRanker = dependencyRanker;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _readinessEvaluator = readinessEvaluator;
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
        Task<IReadOnlyList<ManualDependencyOverride>> overrideTask =
            _overrideRepository.GetAllAsync(cancellationToken);

        await Task.WhenAll(entityTask, dependencyTask, unresolvedDependencyTask, overrideTask);

        IReadOnlyList<TrackedEntity> allEntities = await entityTask;
        IReadOnlyList<TrackedEntity> entities = allEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        IReadOnlyList<TrackedEntity> archivedEntities = allEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Archived)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            allEntities,
            await dependencyTask,
            await unresolvedDependencyTask,
            await overrideTask);

        DependencyRankingResult rankingResult = await Task.Run(
            () => _dependencyRanker.Rank(
                entities,
                effectiveState.ResolvedDependencies.Select(static dependency => dependency.Edge),
                effectiveState.UnresolvedDependencies.Select(
                    static dependency => dependency.Dependency)),
            cancellationToken);

        if (!rankingResult.IsSuccess)
        {
            return new EntityOverviewResult([], rankingResult.Diagnostics);
        }

        IReadOnlyDictionary<EntityId, TrackedEntity> entitiesById =
            entities.ToDictionary(static entity => entity.Id);
        IReadOnlyDictionary<EntityId, EntityReadiness> readinessById =
            _readinessEvaluator.Evaluate(entities, effectiveState);

        Dictionary<EntityId, int> dependencyCounts = entities.ToDictionary(
            static entity => entity.Id,
            static _ => 0);
        Dictionary<EntityId, Dictionary<EntitySourceKey, string>> dependencyNames =
            entities.ToDictionary(
                static entity => entity.Id,
                static _ => new Dictionary<EntitySourceKey, string>());

        foreach (PersistedDependency dependency in effectiveState.ResolvedDependencies)
        {
            if (dependencyCounts.ContainsKey(dependency.Edge.DependentEntityId))
            {
                dependencyCounts[dependency.Edge.DependentEntityId]++;
            }

            if (dependencyNames.TryGetValue(
                    dependency.Edge.DependentEntityId,
                    out Dictionary<EntitySourceKey, string>? ownerNames) &&
                entitiesById.TryGetValue(
                    dependency.Edge.DependencyEntityId,
                    out TrackedEntity? dependencyEntity))
            {
                ownerNames.TryAdd(
                    EntitySourceKey.From(dependencyEntity.SourceName),
                    dependencyEntity.SourceName);
            }
        }

        foreach (PersistedUnresolvedDependency dependency in effectiveState.UnresolvedDependencies)
        {
            dependencyCounts[dependency.Dependency.DependentEntityId]++;
            if (dependencyNames.TryGetValue(
                    dependency.Dependency.DependentEntityId,
                    out Dictionary<EntitySourceKey, string>? ownerNames))
            {
                ownerNames.TryAdd(
                    EntitySourceKey.From(dependency.Dependency.DependencySourceName),
                    dependency.Dependency.DependencySourceName);
            }
        }

        IReadOnlyDictionary<EntityId, string[]> orderedDependencyNames = dependencyNames
            .ToDictionary(
                static item => item.Key,
                static item => item.Value.Values
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static name => name, StringComparer.Ordinal)
                    .ToArray());

        IEnumerable<EntityOverviewItem> rankedItems = rankingResult.Rankings
            .Select(ranking =>
            {
                TrackedEntity entity = entitiesById[ranking.EntityId];
                EntityReadiness readiness = readinessById[entity.Id];
                return new EntityOverviewItem(
                    entity.Id,
                    ranking.Rank,
                    entity.SourceName,
                    entity.Provenance,
                    entity.Status,
                    entity.Notes,
                    entity.LifecycleState,
                    dependencyCounts[entity.Id],
                    orderedDependencyNames[entity.Id],
                    DependencyResolutionState.Resolved,
                    [],
                    _readinessEvaluator.Classify(entity, readiness),
                    readiness.Blockers);
            });

        IEnumerable<EntityOverviewItem> unrankedItems = rankingResult.UnrankedEntities
            .Select(unrankedEntity =>
            {
                TrackedEntity entity = entitiesById[unrankedEntity.EntityId];
                EntityReadiness readiness = readinessById[entity.Id];
                return new EntityOverviewItem(
                    entity.Id,
                    null,
                    entity.SourceName,
                    entity.Provenance,
                    entity.Status,
                    entity.Notes,
                    entity.LifecycleState,
                    dependencyCounts[entity.Id],
                    orderedDependencyNames[entity.Id],
                    unrankedEntity.State,
                    unrankedEntity.MissingDependencyNames,
                    _readinessEvaluator.Classify(entity, readiness),
                    readiness.Blockers);
            });

        IEnumerable<EntityOverviewItem> archivedItems = archivedEntities
            .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .Select(entity => new EntityOverviewItem(
                entity.Id,
                null,
                entity.SourceName,
                entity.Provenance,
                entity.Status,
                entity.Notes,
                entity.LifecycleState,
                0,
                [],
                null,
                [],
                _readinessEvaluator.Classify(entity),
                []));

        return new EntityOverviewResult(
            rankedItems.Concat(unrankedItems),
            [],
            archivedItems);
    }
}
