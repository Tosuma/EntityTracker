using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

/// <summary>
/// Applies one development status to a validated collection of active entities.
/// </summary>
public sealed class BulkStatusUpdateService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ITrackedStateStore _store;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public BulkStatusUpdateService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ITrackedStateStore store,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _store = store;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<BulkStatusUpdateResult> ApplyAsync(
        IReadOnlyCollection<EntityId> entityIds,
        DevelopmentStatus targetStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        if (entityIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one entity must be selected.",
                nameof(entityIds));
        }

        if (!Enum.IsDefined(targetStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetStatus),
                targetStatus,
                "The development status is not defined.");
        }

        if (entityIds.Any(static id => id is null))
        {
            throw new ArgumentException(
                "The selected entity IDs cannot contain null values.",
                nameof(entityIds));
        }

        EntityId[] selectedIds = entityIds.Distinct().ToArray();
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
        Dictionary<EntityId, TrackedEntity> entitiesById = entities.ToDictionary(
            static entity => entity.Id);
        foreach (EntityId selectedId in selectedIds)
        {
            if (!entitiesById.TryGetValue(selectedId, out TrackedEntity? entity))
            {
                throw new InvalidOperationException(
                    "A selected entity no longer exists. Refresh the overview and try again.");
            }

            if (entity.LifecycleState != EntityLifecycleState.Active)
            {
                throw new InvalidOperationException(
                    $"'{entity.SourceName}' is no longer active. Refresh the overview and try again.");
            }
        }

        HashSet<EntityId> selectedIdSet = selectedIds.ToHashSet();
        TrackedEntity[] progressUpdates = entities
            .Where(entity => selectedIdSet.Contains(entity.Id) && entity.Status != targetStatus)
            .Select(entity => new TrackedEntity(
                entity.Id,
                entity.SourceName,
                targetStatus,
                entity.Notes,
                entity.LifecycleState,
                entity.Provenance,
                entity.RequestedPriority))
            .ToArray();
        int unchangedCount = selectedIds.Length - progressUpdates.Length;
        if (progressUpdates.Length == 0)
        {
            return new BulkStatusUpdateResult(0, unchangedCount);
        }

        Dictionary<EntityId, TrackedEntity> updatesById = progressUpdates.ToDictionary(
            static entity => entity.Id);
        TrackedEntity[] candidateEntities = entities
            .Select(entity => updatesById.GetValueOrDefault(entity.Id) ?? entity)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);

        await _store.ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                [],
                [],
                [],
                entitiesWithProgressToUpdate: progressUpdates,
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    effectiveState)),
            cancellationToken);

        return new BulkStatusUpdateResult(progressUpdates.Length, unchangedCount);
    }
}
