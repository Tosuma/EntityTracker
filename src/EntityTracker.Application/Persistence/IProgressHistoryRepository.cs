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

    /// <summary>
    /// Gets the Tracker's dependency history, oldest first: what decides, with the status history, which
    /// entities waited on dependencies on a past day. Empty when the Tracker has none yet.
    /// </summary>
    Task<IReadOnlyList<DependencyHistoryEntry>> GetDependencyHistoryAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DependencyHistoryEntry>>([]);

    Task<ProgressSnapshot?> GetLatestProgressSnapshotAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
