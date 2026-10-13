using EntityTracker.Application.History;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public sealed record TrackerCreationState(
    Tracker Tracker,
    TrackedStateChangeSet ChangeSet,
    ProgressSnapshotState InitialSnapshot,
    TrackerSyncBaseline? SyncBaseline = null);
