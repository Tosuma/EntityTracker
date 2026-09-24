using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IEntityAuditReader _entityAuditReader;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly WorkflowReadinessEvaluator _readinessEvaluator;
    private readonly PriorityPlanningService _priorityPlanningService;

    public EntityOverviewService(
        IEntityRepository entityRepository,
        IEntityAuditReader entityAuditReader,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        IDependencyRankingService dependencyRanker,
        EffectiveDependencyResolver effectiveDependencyResolver,
        WorkflowReadinessEvaluator readinessEvaluator,
        PriorityPlanningService priorityPlanningService)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(entityAuditReader);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(readinessEvaluator);
        ArgumentNullException.ThrowIfNull(priorityPlanningService);

        _entityRepository = entityRepository;
        _entityAuditReader = entityAuditReader;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _dependencyRanker = dependencyRanker;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _readinessEvaluator = readinessEvaluator;
        _priorityPlanningService = priorityPlanningService;
    }

    public async Task<EntityOverviewResult> GetAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        Task<IReadOnlyList<TrackedEntity>> entityTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityAuditTimestamps>> auditTask =
            _entityAuditReader.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependencyTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedDependencyTask =
            _dependencyRepository.GetAllUnresolvedAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overrideTask =
            _overrideRepository.GetAllAsync(trackerId, cancellationToken);

        await Task.WhenAll(
            entityTask,
            auditTask,
            dependencyTask,
            unresolvedDependencyTask,
            overrideTask);

        IReadOnlyList<TrackedEntity> allEntities = await entityTask;
        IReadOnlyList<PersistedDependency> persistedDependencies = await dependencyTask;
        IReadOnlyList<PersistedUnresolvedDependency> persistedUnresolvedDependencies =
            await unresolvedDependencyTask;
        IReadOnlyList<ManualDependencyOverride> persistedOverrides = await overrideTask;
        TrackerStateValidator.EnsureOwned(
            trackerId,
            allEntities,
            persistedDependencies,
            persistedUnresolvedDependencies,
            persistedOverrides);
        IReadOnlyDictionary<EntityId, EntityAuditTimestamps> auditByEntityId =
            (await auditTask).ToDictionary(static audit => audit.EntityId);
        foreach (TrackedEntity entity in allEntities)
        {
            if (!auditByEntityId.ContainsKey(entity.Id))
            {
                throw new InvalidDataException(
                    $"Persisted audit timestamps are missing for entity '{entity.SourceName}'.");
            }
        }
        IReadOnlyList<TrackedEntity> entities = allEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        IReadOnlyList<TrackedEntity> archivedEntities = allEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Archived)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            allEntities,
            persistedDependencies,
            persistedUnresolvedDependencies,
            persistedOverrides);

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

        DependencyRankingResult knownGraphOrdering = await Task.Run(
            () => _dependencyRanker.Rank(
                entities,
                effectiveState.ResolvedDependencies.Select(static dependency => dependency.Edge),
                []),
            cancellationToken);
        if (!knownGraphOrdering.IsSuccess)
        {
            return new EntityOverviewResult([], knownGraphOrdering.Diagnostics);
        }

        IReadOnlyDictionary<EntityId, int?> effectivePriorities =
            _priorityPlanningService.CalculateEffectivePriorities(allEntities, effectiveState);
        IReadOnlyDictionary<EntityId, int> knownGraphPositions = knownGraphOrdering.Rankings
            .ToDictionary(static ranking => ranking.EntityId, static ranking => ranking.Rank);

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
                    entity.RequestedPriority,
                    effectivePriorities[entity.Id],
                    entity.SourceName,
                    entity.Provenance,
                    entity.Status,
                    entity.Notes,
                    entity.ResponsibleDeveloper,
                    entity.GroupName,
                    entity.LifecycleState,
                    dependencyCounts[entity.Id],
                    orderedDependencyNames[entity.Id],
                    DependencyResolutionState.Resolved,
                    [],
                    _readinessEvaluator.Classify(entity, readiness),
                    readiness.Blockers,
                    auditByEntityId[entity.Id]);
            });

        IEnumerable<EntityOverviewItem> unrankedItems = rankingResult.UnrankedEntities
            .Select(unrankedEntity =>
            {
                TrackedEntity entity = entitiesById[unrankedEntity.EntityId];
                EntityReadiness readiness = readinessById[entity.Id];
                return new EntityOverviewItem(
                    entity.Id,
                    null,
                    entity.RequestedPriority,
                    effectivePriorities[entity.Id],
                    entity.SourceName,
                    entity.Provenance,
                    entity.Status,
                    entity.Notes,
                    entity.ResponsibleDeveloper,
                    entity.GroupName,
                    entity.LifecycleState,
                    dependencyCounts[entity.Id],
                    orderedDependencyNames[entity.Id],
                    unrankedEntity.State,
                    unrankedEntity.MissingDependencyNames,
                    _readinessEvaluator.Classify(entity, readiness),
                    readiness.Blockers,
                    auditByEntityId[entity.Id]);
            });

        IReadOnlyDictionary<EntityId, string[]> archivedDependencyNames =
            BuildArchivedDependencyNames(
                allEntities,
                archivedEntities,
                persistedDependencies,
                persistedUnresolvedDependencies,
                persistedOverrides);

        IEnumerable<EntityOverviewItem> archivedItems = archivedEntities
            .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .Select(entity => new EntityOverviewItem(
                entity.Id,
                null,
                entity.RequestedPriority,
                null,
                entity.SourceName,
                entity.Provenance,
                entity.Status,
                entity.Notes,
                entity.ResponsibleDeveloper,
                entity.GroupName,
                entity.LifecycleState,
                archivedDependencyNames[entity.Id].Length,
                archivedDependencyNames[entity.Id],
                null,
                [],
                _readinessEvaluator.Classify(entity),
                [],
                auditByEntityId[entity.Id]));

        return new EntityOverviewResult(
            rankedItems
                .Concat(unrankedItems)
                .OrderBy(static item => item.EffectivePriority ?? int.MaxValue)
                .ThenBy(static item => item.Rank is null ? 1 : 0)
                .ThenBy(item => item.Rank ?? knownGraphPositions[item.EntityId])
                .ThenBy(static item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.SourceName, StringComparer.Ordinal)
                .ThenBy(static item => item.EntityId.Value),
            [],
            archivedItems);
    }

    private static IReadOnlyDictionary<EntityId, string[]> BuildArchivedDependencyNames(
        IReadOnlyList<TrackedEntity> allEntities,
        IReadOnlyList<TrackedEntity> archivedEntities,
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolvedDependencies,
        IReadOnlyList<ManualDependencyOverride> overrides)
    {
        Dictionary<EntityId, TrackedEntity> entitiesById = allEntities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> declarations =
            DependencyStateResolver.BuildCurrentDeclarations(
                dependencies,
                unresolvedDependencies,
                entitiesById);
        HashSet<EntityId> archivedIds = archivedEntities
            .Select(static entity => entity.Id)
            .ToHashSet();

        foreach (ManualDependencyOverride dependencyOverride in overrides
                     .Where(item => archivedIds.Contains(item.DependentEntityId)))
        {
            EntitySourceKey key = EntitySourceKey.From(dependencyOverride.DependencySourceName);
            if (dependencyOverride.Action == ManualDependencyOverrideAction.Suppress)
            {
                if (declarations.TryGetValue(
                        dependencyOverride.DependentEntityId,
                        out Dictionary<EntitySourceKey, DependencyDeclaration>? ownerDeclarations))
                {
                    ownerDeclarations.Remove(key);
                }

                continue;
            }

            DependencyStateResolver.AddDeclaration(
                declarations,
                dependencyOverride.DependentEntityId,
                new DependencyDeclaration(
                    key,
                    dependencyOverride.DependencySourceName,
                    ImportedDependencyKind.Mandatory,
                    null));
        }

        return archivedEntities.ToDictionary(
            static entity => entity.Id,
            entity => declarations.TryGetValue(
                    entity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? ownerDeclarations)
                ? ownerDeclarations.Values
                    .Select(static declaration => declaration.TargetName)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static name => name, StringComparer.Ordinal)
                    .ToArray()
                : []);
    }
}
