using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetAsync(
        EntityId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
