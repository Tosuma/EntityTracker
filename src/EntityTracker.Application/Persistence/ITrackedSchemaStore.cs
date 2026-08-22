namespace EntityTracker.Application.Persistence;

/// <summary>
/// Applies one validated tracked-schema mutation atomically.
/// </summary>
public interface ITrackedSchemaStore
{
    Task ApplyAsync(
        TrackedSchemaChangeSet changeSet,
        CancellationToken cancellationToken = default);
}
