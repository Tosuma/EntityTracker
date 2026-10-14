namespace EntityTracker.Application.Snapshots;

public sealed record ProjectSnapshot(
    int FormatVersion,
    SnapshotProject Project,
    IReadOnlyList<SnapshotTracker> Trackers,
    IReadOnlyList<SnapshotDeveloper>? Developers = null)
{
    /// <summary>
    /// Format 6 stores each entity's dependencies as one list of entity IDs; a name no entity has yet is
    /// a placeholder entity (lifecycle Placeholder). Formats 1–5 stored imported
    /// dependencies (by entity ID or name, with a kind) and manual Add/Suppress corrections; they are
    /// read into the Legacy fields and converted by <see cref="ProjectSnapshotUpgrade"/>.
    /// </summary>
    public const int CurrentFormatVersion = 6;

    public static bool IsSupportedFormat(int formatVersion) =>
        formatVersion is >= 1 and <= CurrentFormatVersion;
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
    string? SyncBaselineJson = null,
    // Formats 1–5 only: the last CSV import, which format 6 no longer keeps.
    SnapshotImportSummary? LegacyImportSummary = null);

public sealed record SnapshotEntity(
    Guid Id, Guid TrackerId, string SourceName, string DevelopmentStatus,
    string Notes, string LifecycleState, string Provenance,
    int? RequestedPriority, string ResponsibleDeveloper, string GroupName,
    DateTimeOffset CreatedAtUtc, DateTimeOffset SchemaUpdatedAtUtc,
    DateTimeOffset ProgressUpdatedAtUtc,
    IReadOnlyList<SnapshotDependency> Dependencies,
    IReadOnlyList<SnapshotResponsibilityPeriod>? ResponsibilityPeriods = null,
    string FilterActive = "",
    string SharedNotes = "",
    // Formats 1–5 only; format 6 keeps these empty and uses Dependencies.
    IReadOnlyList<SnapshotLegacyDependency>? LegacyDependencies = null,
    IReadOnlyList<SnapshotLegacyUnresolvedDependency>? LegacyUnresolvedDependencies = null,
    IReadOnlyList<SnapshotLegacyOverride>? LegacyOverrides = null);

public sealed record SnapshotResponsibilityPeriod(Guid Id, Guid EntityId, Guid DeveloperId,
    DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc);

/// <summary>
/// An entity's dependency on another entity of the same Tracker, by ID. An unresolved reference is a
/// dependency on a placeholder entity.
/// </summary>
public sealed record SnapshotDependency(
    Guid DependencyEntityId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    public EntityTracker.Domain.EntityDependency ToEntityDependency(Guid ownerId) =>
        new(new EntityTracker.Domain.EntityId(ownerId), new EntityTracker.Domain.EntityId(DependencyEntityId));
}

/// <summary>Formats 1–5: an imported dependency on another entity of the Tracker.</summary>
public sealed record SnapshotLegacyDependency(
    Guid DependentEntityId, Guid DependencyEntityId, string Kind,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>Formats 1–5: an imported dependency on a name no entity had.</summary>
public sealed record SnapshotLegacyUnresolvedDependency(
    Guid DependentEntityId, string DependencySourceName, string Kind,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>Formats 1–5: a manual Add or Suppress correction of the imported dependencies.</summary>
public sealed record SnapshotLegacyOverride(
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
