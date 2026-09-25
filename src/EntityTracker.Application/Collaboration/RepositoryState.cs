using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Collaboration;

public sealed record ProjectRepositoryState(
    Project Project,
    IReadOnlyList<TrackerRepositoryState> Trackers,
    IReadOnlyList<RepositoryOperation> Operations,
    IReadOnlyList<RepositoryTombstone> Tombstones);

public sealed record TrackerRepositoryState(
    Tracker Tracker,
    IReadOnlyList<EntityRepositoryState> Entities);

public sealed record EntityRepositoryState(
    TrackedEntity Entity,
    EntityAuditTimestamps AuditTimestamps,
    IReadOnlyList<ImportedDependencyDeclaration> ImportedDependencies,
    IReadOnlyList<ManualDependencyOverride> ManualOverrides);

public sealed record ImportedDependencyDeclaration(
    string SourceName,
    ImportedDependencyKind Kind);

public sealed record RepositoryOperation(
    OperationId Id,
    RepositoryOperationKind Kind,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<ProjectId> ProjectIds,
    IReadOnlyList<TrackerId> TrackerIds,
    IReadOnlyList<EntityId> EntityIds,
    IReadOnlyList<EntityStatusHistoryEntry> StatusTransitions,
    RepositoryImportSummary? ImportSummary = null,
    IReadOnlyList<RepositoryProgressSnapshot>? ProgressSnapshots = null)
{
    public IReadOnlyList<RepositoryProgressSnapshot> RecordedProgressSnapshots { get; } =
        ProgressSnapshots?.ToArray() ?? [];
}

public sealed record RepositoryProgressSnapshot(
    TrackerId TrackerId,
    DateTimeOffset RecordedAtUtc,
    ProgressSnapshotState State);

public sealed record RepositoryImportSummary(
    TrackerId TrackerId,
    string SourceFileName,
    SchemaImportMode Mode,
    int NewEntityCount,
    int ChangedEntityCount,
    int ArchivedEntityCount,
    int UnchangedEntityCount,
    int UnresolvedEntityCount);

public sealed record RepositoryTombstone(
    RepositoryTombstoneKind Kind,
    Guid DeletedId,
    ProjectId ProjectId,
    OperationId DeletionOperationId,
    DateTimeOffset DeletedAtUtc);

public enum RepositoryTombstoneKind
{
    Project,
    Tracker,
    Entity
}

public enum RepositoryOperationKind
{
    RepositoryLinked,
    HistoryImported,
    ProjectCreated,
    ProjectRenamed,
    ProjectRecycled,
    ProjectRestored,
    ProjectPurged,
    TrackerCreated,
    TrackerCopied,
    TrackerRenamed,
    TrackerRecycled,
    TrackerRestored,
    TrackerPurged,
    SchemaImported,
    EntityCreated,
    EntityEdited,
    EntityArchived,
    EntityRestored,
    EntityPurged,
    DependencyEdited,
    StatusUpdated,
    BulkStatusUpdated
}
