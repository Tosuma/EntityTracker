using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface ITrackerRepository
{
    Task<Tracker?> GetAsync(TrackerId trackerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tracker>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tracker>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}
