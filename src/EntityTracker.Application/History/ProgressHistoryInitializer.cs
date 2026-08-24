using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

public sealed class ProgressHistoryInitializer
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly ITrackedStateStore _store;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public ProgressHistoryInitializer(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        ITrackedStateStore store,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ProgressSnapshotCalculator snapshotCalculator)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(snapshotCalculator);
        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _store = store;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _snapshotCalculator = snapshotCalculator;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        IReadOnlyList<TrackedEntity> entities = await entitiesTask;
        EffectiveDependencyState effective = _effectiveDependencyResolver.Resolve(
            entities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        await _store.EnsureHistoryBaselineAsync(
            entities,
            _snapshotCalculator.Calculate(entities, effective),
            cancellationToken);
    }
}
