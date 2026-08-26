using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Lifecycle;

/// <summary>
/// Soft-archives active entities while preserving their identity and related persisted data.
/// </summary>
public sealed class EntityLifecycleService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly ITrackedStateStore _store;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public EntityLifecycleService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        IDependencyRankingService dependencyRanker,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(dependencyRanker);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _store = store;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _dependencyRanker = dependencyRanker;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<bool> TryArchiveAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        TrackedEntity[] entities = (await entitiesTask).ToArray();
        TrackedEntity? entity = entities.SingleOrDefault(item => item.Id == entityId);
        if (entity?.LifecycleState != EntityLifecycleState.Active)
        {
            return false;
        }

        TrackedEntity archived = new(
            entity.Id,
            entity.SourceName,
            entity.Status,
            entity.Notes,
            EntityLifecycleState.Archived,
            entity.Provenance,
            entity.RequestedPriority);
        TrackedEntity[] candidateEntities = entities
            .Select(item => item.Id == entityId ? archived : item)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        await _store.ApplyAsync(
            new TrackedStateChangeSet(
                [], [], [entityId], [], [], [],
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    effectiveState)),
            cancellationToken);
        return true;
    }

    public async Task<EntityRestorationResult> RestoreAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        TrackedEntity[] currentEntities = (await entitiesTask).ToArray();
        TrackedEntity? archived = currentEntities.SingleOrDefault(entity => entity.Id == entityId);
        if (archived is null)
        {
            return EntityRestorationResult.Failure("The selected entity no longer exists.");
        }

        if (archived.LifecycleState != EntityLifecycleState.Archived)
        {
            return EntityRestorationResult.Failure("The selected entity is already active.");
        }

        TrackedEntity restored = new(
            archived.Id,
            archived.SourceName,
            archived.Status,
            archived.Notes,
            EntityLifecycleState.Active,
            archived.Provenance,
            archived.RequestedPriority);
        TrackedEntity[] candidateEntities = currentEntities
            .Select(entity => entity.Id == entityId ? restored : entity)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        DependencyRankingResult ranking = _dependencyRanker.Rank(
            candidateEntities.Where(static entity =>
                entity.LifecycleState == EntityLifecycleState.Active),
            effectiveState.ResolvedDependencies.Select(static dependency => dependency.Edge),
            effectiveState.UnresolvedDependencies.Select(static dependency =>
                dependency.Dependency));
        if (!ranking.IsSuccess)
        {
            return EntityRestorationResult.Failure(
                ranking.Diagnostics.Select(static diagnostic => diagnostic.Message));
        }

        await _store.ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                [],
                [],
                [],
                entityIdsToRestore: [entityId],
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    effectiveState)),
            cancellationToken);
        return EntityRestorationResult.Success();
    }
}
