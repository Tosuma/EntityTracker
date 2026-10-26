using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Lifecycle;

/// <summary>
/// Soft-archives active entities while preserving their identity and related persisted data.
/// </summary>
public sealed class EntityLifecycleService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly ITrackedStateStore _store;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public EntityLifecycleService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        IDependencyRankingService dependencyRanker,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(dependencyRanker);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _store = store;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _dependencyRanker = dependencyRanker;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<bool> TryArchiveAsync(
        TrackerId trackerId,
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entityId);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        TrackedEntity[] entities = (await entitiesTask).ToArray();
        TrackerStateValidator.EnsureOwned(
            trackerId,
            entities,
            await dependenciesTask);
        TrackedEntity? entity = entities.SingleOrDefault(item => item.Id == entityId);
        if (entity?.LifecycleState != EntityLifecycleState.Active)
        {
            return false;
        }

        TrackedEntity archived = new(
            entity.Id,
            trackerId,
            entity.SourceName,
            entity.Status,
            entity.Notes,
            EntityLifecycleState.Archived,
            entity.RequestedPriority,
            entity.ResponsibleDeveloper,
            entity.GroupName,
            entity.FilterActive,
            entity.SharedNotes,
            entity.DueDate);
        TrackedEntity[] candidateEntities = entities
            .Select(item => item.Id == entityId ? archived : item)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            await dependenciesTask);
        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                [], [], [entityId],
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    effectiveState)),
            cancellationToken);
        return true;
    }

    public async Task<EntityRestorationResult> RestoreAsync(
        TrackerId trackerId,
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entityId);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        TrackedEntity[] currentEntities = (await entitiesTask).ToArray();
        TrackerStateValidator.EnsureOwned(
            trackerId,
            currentEntities,
            await dependenciesTask);
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
            trackerId,
            archived.SourceName,
            archived.Status,
            archived.Notes,
            EntityLifecycleState.Active,
            archived.RequestedPriority,
            archived.ResponsibleDeveloper,
            archived.GroupName,
            archived.FilterActive,
            archived.SharedNotes,
            archived.DueDate);
        TrackedEntity[] candidateEntities = currentEntities
            .Select(entity => entity.Id == entityId ? restored : entity)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            await dependenciesTask);
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
            trackerId,
            new TrackedStateChangeSet(
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

    public async Task<bool> PurgeArchivedAsync(
        TrackerId trackerId,
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entityId);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        TrackedEntity[] entities = (await entitiesTask).ToArray();
        EntityDependency[] dependencies = (await dependenciesTask).ToArray();
        TrackerStateValidator.EnsureOwned(trackerId, entities, dependencies);

        TrackedEntity? archived = entities.SingleOrDefault(entity => entity.Id == entityId);
        if (archived?.LifecycleState != EntityLifecycleState.Archived)
        {
            return false;
        }

        // The entity's own dependencies go with it. If other entities depend on it, it stays as a
        // placeholder with the same ID and name, so their dependencies become unresolved references.
        EntityDependency[] remaining = dependencies
            .Where(dependency => dependency.DependentEntityId != entityId)
            .ToArray();
        bool referenced = remaining.Any(dependency => dependency.DependencyEntityId == entityId);
        TrackedEntity[] remainingEntities = entities
            .Where(entity => entity.Id != entityId)
            .Concat(referenced
                ? [new TrackedEntity(archived.Id, trackerId, archived.SourceName,
                    lifecycleState: EntityLifecycleState.Placeholder)]
                : [])
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            remainingEntities,
            remaining);
        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                [],
                [],
                [],
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    remainingEntities,
                    effectiveState),
                entityIdsToPurge: [entityId]),
            cancellationToken);
        return true;
    }
}
