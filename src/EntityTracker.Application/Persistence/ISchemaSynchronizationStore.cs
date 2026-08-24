using EntityTracker.Application.Synchronization;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Atomically applies an accepted schema synchronization and records its summary.
/// </summary>
public interface ISchemaSynchronizationStore
{
    Task<SchemaImportSummary> ApplyAsync(
        TrackedStateChangeSet changeSet,
        SchemaImportCompletion completion,
        CancellationToken cancellationToken = default);

    Task<SchemaImportSummary?> GetLatestImportAsync(
        CancellationToken cancellationToken = default);
}
