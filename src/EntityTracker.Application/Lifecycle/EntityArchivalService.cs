using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Lifecycle;

/// <summary>
/// Soft-archives active entities while preserving their identity and related persisted data.
/// </summary>
public sealed class EntityArchivalService
{
    private readonly IEntityRepository _entityRepository;
    private readonly ITrackedSchemaStore _store;

    public EntityArchivalService(
        IEntityRepository entityRepository,
        ITrackedSchemaStore store)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _store = store;
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
            new TrackedSchemaChangeSet([], [], [entityId], [], [], []),
            cancellationToken);
        return true;
    }
}
