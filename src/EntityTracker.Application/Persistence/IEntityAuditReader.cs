using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IEntityAuditReader
{
    Task<IReadOnlyList<EntityAuditTimestamps>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
