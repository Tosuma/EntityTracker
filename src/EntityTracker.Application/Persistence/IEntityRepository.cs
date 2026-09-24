using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetAsync(
        TrackerId trackerId,
        EntityId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
