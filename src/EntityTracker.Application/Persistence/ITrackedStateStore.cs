namespace EntityTracker.Application.Persistence;

/// <summary>
/// Applies one validated tracked-state mutation atomically.
/// </summary>
public interface ITrackedStateStore
{
    Task ApplyAsync(
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default);
}
