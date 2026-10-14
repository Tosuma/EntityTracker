using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

public sealed class ProgressHistoryInitializer
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly ITrackedStateStore _store;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public ProgressHistoryInitializer(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ProgressSnapshotCalculator snapshotCalculator)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(snapshotCalculator);
        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _store = store;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _snapshotCalculator = snapshotCalculator;
    }

    public async Task EnsureInitializedAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        IReadOnlyList<TrackedEntity> entities = await entitiesTask;
        TrackerStateValidator.EnsureOwned(
            trackerId,
            entities,
            await dependenciesTask);
        EffectiveDependencyState effective = _effectiveDependencyResolver.Resolve(
            entities,
            await dependenciesTask);
        await _store.EnsureHistoryBaselineAsync(
            trackerId,
            entities,
            _snapshotCalculator.Calculate(entities, effective),
            cancellationToken);
    }
}
