using EntityTracker.Application.History;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public sealed record TrackerCreationState(
    Tracker Tracker,
    TrackedStateChangeSet ChangeSet,
    ProgressSnapshotState InitialSnapshot,
    SchemaImportCompletion? ImportCompletion = null);
