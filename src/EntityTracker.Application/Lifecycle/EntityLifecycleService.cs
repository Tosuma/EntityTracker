using EntityTracker.Application.Dependencies;
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
    private readonly DependencyRanker _dependencyRanker;

    public EntityLifecycleService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        DependencyRanker dependencyRanker)
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
    }

    public async Task<bool> TryArchiveAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        TrackedEntity? entity = await _entityRepository.GetAsync(entityId, cancellationToken);
        if (entity?.LifecycleState != EntityLifecycleState.Active)
        {
            return false;
        }

        await _store.ApplyAsync(
            new TrackedStateChangeSet([], [], [entityId], [], [], []),
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
            archived.Provenance);
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
                entityIdsToRestore: [entityId]),
            cancellationToken);
        return EntityRestorationResult.Success();
    }
}
