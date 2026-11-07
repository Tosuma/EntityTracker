using EntityTracker.Application.Tracking;

namespace EntityTracker.Application.Snapshots;

public sealed record ProjectSnapshot(
    int FormatVersion,
    SnapshotProject Project,
    IReadOnlyList<SnapshotTracker> Trackers,
    IReadOnlyList<SnapshotDeveloper>? Developers = null,
    // Format 11 and later: the Project's schemas. Null or empty in older formats.
    IReadOnlyList<SnapshotSchema>? Schemas = null)
{
    /// <summary>
    /// Format 6 stores each entity's dependencies as one list of entity IDs; a name no entity has yet is
    /// a placeholder entity (lifecycle Placeholder). Formats 1–5 stored imported
    /// dependencies (by entity ID or name, with a kind) and manual Add/Suppress corrections; they are
    /// read into the Legacy fields and converted by <see cref="ProjectSnapshotUpgrade"/>.
    /// Format 7 writes indented JSON, so changes are readable in Git, and keeps a Tracker's Sync from
    /// source baseline as nested JSON in tracker.json instead of one escaped JSON string.
    /// Format 8 adds the development status NotRelevant; older apps don't know it, so they must not read
    /// a format 8 Project.
    /// Format 9 adds an entity's optional due date (and a Sync from source baseline entity's); older apps
    /// would drop it, so they must not read a format 9 Project.
    /// Format 10 keeps entity IDs in a Sync from source baseline, and the source name a copy's entity was
    /// paired with, so a rename is recognised; older apps refuse the unknown properties.
    /// Format 11 adds the Project's schemas (schemas/&lt;id&gt;/schema.json and their entities); older apps
    /// would drop them, so they must not read a format 11 Project.
    /// Format 12 adds a Tracker's link to the schema it was created from, with its baseline, in
    /// tracker.json; older apps refuse the unknown property.
    /// </summary>
    public const int CurrentFormatVersion = 12;

    /// <summary>The first format that may hold a Tracker's schema link.</summary>
    public const int FirstSchemaLinkFormatVersion = 12;

    /// <summary>The first format that may hold schemas.</summary>
    public const int FirstSchemaFormatVersion = 11;

    /// <summary>The first format that may hold the development status NotRelevant.</summary>
    public const int FirstNotRelevantFormatVersion = 8;

    /// <summary>The first format that may hold a due date.</summary>
    public const int FirstDueDateFormatVersion = 9;

    /// <summary>The first format whose Sync from source baselines may hold entity IDs and paired names.</summary>
    public const int FirstBaselineEntityIdFormatVersion = 10;

    /// <summary>
    /// The first format with today's entities and dependencies (format 6). Older snapshots are legacy:
    /// they are read into the Legacy fields and converted.
    /// </summary>
    public const int FirstCurrentModelFormatVersion = 6;

    public static bool IsSupportedFormat(int formatVersion) =>
        formatVersion is >= 1 and <= CurrentFormatVersion;
}

/// <summary>A Project's schema: its catalog fields and its entities.</summary>
public sealed record SnapshotSchema(
    Guid Id, Guid ProjectId, string Name, string LifecycleState,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? RecycledAtUtc,
    IReadOnlyList<SnapshotSchemaEntity> Entities);

/// <summary>A schema entity: its name, group and the IDs of the schema entities it depends on.</summary>
public sealed record SnapshotSchemaEntity(
    Guid Id, Guid SchemaId, string Name, string GroupName, IReadOnlyList<Guid> Dependencies);

/// <summary>
/// Format 12: a Tracker's link to the schema it was created from, and its baseline: per schema entity, the
/// name, group and dependencies the Tracker last took from the schema, and the Tracker entity linked to it
/// (null once that entity was permanently deleted).
/// </summary>
public sealed record SnapshotSchemaLink(Guid SchemaId, IReadOnlyList<SnapshotSchemaLinkEntity> Entities)
{
    /// <summary>The same link with entities and dependencies in ID order, as it is written.</summary>
    public SnapshotSchemaLink Normalize() => this with
    {
        Entities = Entities.OrderBy(static e => e.SchemaEntityId)
            .Select(static e => e with { Dependencies = e.Dependencies.Order().ToArray() }).ToArray()
    };
}

public sealed record SnapshotSchemaLinkEntity(
    Guid SchemaEntityId, Guid? EntityId, string Name, string GroupName, IReadOnlyList<Guid> Dependencies);

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
    // What the Tracker and its source looked like at the last Sync from source (copies only).
    TrackerSyncBaseline? SyncBaseline = null,
    // Formats 1–5 only: the last CSV import, which format 6 no longer keeps.
    SnapshotImportSummary? LegacyImportSummary = null,
    // Format 12 and later: the schema the Tracker was created from, and its baseline.
    SnapshotSchemaLink? SchemaLink = null);

public sealed record SnapshotEntity(
    Guid Id, Guid TrackerId, string SourceName, string DevelopmentStatus,
    string Notes, string LifecycleState,
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
    IReadOnlyList<SnapshotLegacyOverride>? LegacyOverrides = null,
    // Formats 1–5 only: how the entity entered its Tracker (Imported, ManualOnly, Copied, ...).
    string? LegacyProvenance = null,
    // Format 9 and later.
    DateOnly? DueDate = null);

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
