using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed record TrackerSyncCommit(
    TrackerId SourceTrackerId,
    TrackerId DestinationTrackerId,
    string ExpectedSourceFingerprint,
    string ExpectedDestinationFingerprint,
    TrackedStateChangeSet ChangeSet,
    TrackerSyncBaseline Baseline);

public interface ITrackerSyncStore
{
    Task<TrackerSyncBaseline?> ReadBaselineAsync(
        TrackerId destinationTrackerId, CancellationToken cancellationToken = default);

    Task<string> FingerprintAsync(
        TrackerId trackerId, CancellationToken cancellationToken = default);

    Task ApplyAsync(TrackerSyncCommit commit, CancellationToken cancellationToken = default);
}
