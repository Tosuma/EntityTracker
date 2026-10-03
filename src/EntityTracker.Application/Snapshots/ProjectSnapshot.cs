namespace EntityTracker.Application.Snapshots;

public sealed record ProjectSnapshot(
    int FormatVersion,
    SnapshotProject Project,
    IReadOnlyList<SnapshotTracker> Trackers,
    IReadOnlyList<SnapshotDeveloper>? Developers = null)
{
    public const int CurrentFormatVersion = 4;
}

public sealed record SnapshotDeveloper(
    Guid Id, Guid ProjectId, string Initials, string DisplayName, bool IsRetired);

public sealed record SnapshotProject(
    Guid Id, string Name, string LifecycleState,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RecycledAtUtc);

public sealed record SnapshotTracker(
    Guid Id, Guid ProjectId, string Name, string LifecycleState,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RecycledAtUtc, Guid? CopiedFromTrackerId,
    IReadOnlyList<SnapshotEntity> Entities,
    IReadOnlyList<SnapshotStatusEvent> StatusHistory,
    IReadOnlyList<SnapshotProgress> ProgressHistory,
    SnapshotImportSummary? ImportSummary,
    string? SyncBaselineJson = null);

public sealed record SnapshotEntity(
    Guid Id, Guid TrackerId, string SourceName, string DevelopmentStatus,
    string Notes, string LifecycleState, string Provenance,
    int? RequestedPriority, string ResponsibleDeveloper, string GroupName,
    DateTimeOffset CreatedAtUtc, DateTimeOffset SchemaUpdatedAtUtc,
    DateTimeOffset ProgressUpdatedAtUtc,
    IReadOnlyList<SnapshotDependency> Dependencies,
    IReadOnlyList<SnapshotUnresolvedDependency> UnresolvedDependencies,
    IReadOnlyList<SnapshotOverride> ManualOverrides,
    IReadOnlyList<SnapshotResponsibilityPeriod>? ResponsibilityPeriods = null,
    string FilterActive = "");

public sealed record SnapshotResponsibilityPeriod(Guid Id, Guid EntityId, Guid DeveloperId,
    DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc);

public sealed record SnapshotDependency(
    Guid DependentEntityId, Guid DependencyEntityId, string Kind,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record SnapshotUnresolvedDependency(
    Guid DependentEntityId, string DependencySourceName, string Kind,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record SnapshotOverride(
    Guid DependentEntityId, string DependencySourceName, string Action,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record SnapshotStatusEvent(
    Guid EventId, Guid EntityId, Guid? PreviousEventId,
    string? PreviousStatus, string NewStatus, string Kind,
    DateTimeOffset OccurredAtUtc, int Order);

public sealed record SnapshotProgress(
    Guid SnapshotId, DateTimeOffset RecordedAtUtc,
    int ReadyCount, int BlockedCount, int InProgressCount,
    int ReworkNeededCount, int DevelopmentCompletedCount, int ReconciledCount,
    int Order,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    int ManuallyBlockedCount = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    int ReworkingCount = 0);

public sealed record SnapshotImportSummary(
    DateTimeOffset AppliedAtUtc, string SourceFileName, string Mode,
    int NewEntityCount, int ChangedEntityCount, int ArchivedEntityCount,
    int UnchangedEntityCount, int UnresolvedEntityCount);
