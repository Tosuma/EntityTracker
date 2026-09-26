using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

public sealed class ProgressHistoryInitializer
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly IProgressHistoryRepository _historyRepository;
    private readonly ITrackedStateStore _store;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public ProgressHistoryInitializer(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        IProgressHistoryRepository historyRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ProgressSnapshotCalculator snapshotCalculator)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(historyRepository);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(snapshotCalculator);
        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _historyRepository = historyRepository;
        _store = store;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _snapshotCalculator = snapshotCalculator;
    }

    public async Task EnsureInitializedAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        if (await _historyRepository.GetLatestProgressSnapshotAsync(trackerId, cancellationToken) is not null)
            return;
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        IReadOnlyList<TrackedEntity> entities = await entitiesTask;
        TrackerStateValidator.EnsureOwned(
            trackerId,
            entities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        EffectiveDependencyState effective = _effectiveDependencyResolver.Resolve(
            entities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        await _store.EnsureHistoryBaselineAsync(
            trackerId,
            entities,
            _snapshotCalculator.Calculate(entities, effective),
            cancellationToken);
    }
}
