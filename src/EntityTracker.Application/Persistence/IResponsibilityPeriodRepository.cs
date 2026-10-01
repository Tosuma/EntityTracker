using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IResponsibilityPeriodRepository
{
    Task<IReadOnlyList<ResponsibilityPeriod>> GetByEntityAsync(EntityId entityId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResponsibilityPeriod>> GetByTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default);
}

public sealed record ResponsibilitySelection(EntityId EntityId, IReadOnlyList<DeveloperId> DeveloperIds);
