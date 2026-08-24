namespace EntityTracker.Application.Persistence;

using EntityTracker.Application.History;
using EntityTracker.Domain;

/// <summary>
/// Applies one validated tracked-state mutation atomically.
/// </summary>
public interface ITrackedStateStore
{
    Task ApplyAsync(
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default);

    Task EnsureHistoryBaselineAsync(
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default);
}
