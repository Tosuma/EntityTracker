using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Atomically applies an accepted schema synchronization and records its summary.
/// </summary>
public interface ISchemaSynchronizationStore
{
    Task<SchemaImportSummary> ApplyAsync(
        TrackerId trackerId,
        TrackedStateChangeSet changeSet,
        SchemaImportCompletion completion,
        CancellationToken cancellationToken = default);

    Task<SchemaImportSummary?> GetLatestImportAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
