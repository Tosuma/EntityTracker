namespace EntityTracker.Application.Persistence;

using EntityTracker.Application.History;
using EntityTracker.Domain;

/// <summary>
/// Applies one validated tracked-state mutation atomically.
/// </summary>
public interface ITrackedStateStore
{
    Task ApplyAsync(
        TrackerId trackerId,
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default);

    Task EnsureHistoryBaselineAsync(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default);
}
