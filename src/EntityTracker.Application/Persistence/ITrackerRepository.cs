using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface ITrackerRepository
{
    Task<Tracker?> GetAsync(TrackerId trackerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tracker>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tracker>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    Task<bool> IsNameReservedAsync(
        ProjectId projectId,
        string name,
        TrackerId? excludingTrackerId = null,
        CancellationToken cancellationToken = default);
}
