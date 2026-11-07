using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

/// <summary>
/// Reads a copy's Sync from source baseline: what it and its source looked like when it last synced. Copies
/// made before schemas keep it as their dormant link, which Convert to schema reads.
/// </summary>
public interface ITrackerSyncStore
{
    Task<TrackerSyncBaseline?> ReadBaselineAsync(
        TrackerId destinationTrackerId, CancellationToken cancellationToken = default);
}
