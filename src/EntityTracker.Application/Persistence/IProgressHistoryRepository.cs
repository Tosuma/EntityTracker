using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProgressHistoryRepository
{
    Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);

    Task<ProgressSnapshot?> GetLatestProgressSnapshotAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
