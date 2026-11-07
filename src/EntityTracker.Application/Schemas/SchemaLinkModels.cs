using System.ComponentModel;

using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>
/// One schema entity as a linked Tracker last took it from its schema: its name, group and dependencies
/// then, and the Tracker entity linked to it. <see cref="EntityId"/> is null for an addition the Tracker
/// declined (Keep tracker), and when the linked entity was permanently deleted.
/// </summary>
public sealed record SchemaBaselineEntity(
    SchemaEntityId SchemaEntityId,
    EntityId? EntityId,
    string Name,
    string GroupName,
    IReadOnlyList<SchemaEntityId> Dependencies);

/// <summary>
/// A Tracker's link to the schema it was created from, with its baseline: the schema as it was when the
/// Tracker was created or last pushed to. A push compares the schema, the baseline and the Tracker, so a
/// change only one side made is not asked about.
/// </summary>
public sealed record TrackerSchemaLink(
    TrackerId TrackerId,
    SchemaId SchemaId,
    IReadOnlyList<SchemaBaselineEntity> Entities);

public enum SchemaPushChoice { Schema, Tracker }

public enum SchemaPushChangeKind
{
    /// <summary>The schema added an entity the Tracker doesn't have.</summary>
    Added,

    /// <summary>The schema added an entity the Tracker already has under that name, not linked yet.</summary>
    Linked,

    /// <summary>The schema renamed an entity.</summary>
    Name,

    /// <summary>The schema moved an entity to another group.</summary>
    Group,

    /// <summary>The schema added a dependency.</summary>
    DependencyAdded,

    /// <summary>The schema removed a dependency.</summary>
    DependencyRemoved,

    /// <summary>The schema removed an entity.</summary>
    Removed
}

/// <summary>
/// One change the schema made that a Tracker doesn't have yet. Each is decided per Tracker: Use schema
/// applies it, Keep tracker declines it until the schema changes the same thing again, and leaving it
/// undecided keeps it for a later push. <see cref="IsConflict"/> says the Tracker changed the same thing.
/// </summary>
public sealed class SchemaPushChange : INotifyPropertyChanged
{
    private SchemaPushChoice? _choice;

    public SchemaPushChange(
        TrackerId trackerId,
        string trackerName,
        SchemaEntityId schemaEntityId,
        SchemaEntityId? dependencyId,
        SchemaPushChangeKind kind,
        string entityName,
        bool isConflict,
        string description,
        string useSchemaLabel,
        string keepTrackerLabel)
    {
        TrackerId = trackerId;
        TrackerName = trackerName;
        SchemaEntityId = schemaEntityId;
        DependencyId = dependencyId;
        Kind = kind;
        EntityName = entityName;
        IsConflict = isConflict;
        Description = description;
        UseSchemaLabel = useSchemaLabel;
        KeepTrackerLabel = keepTrackerLabel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackerId TrackerId { get; }
    public string TrackerName { get; }
    public SchemaEntityId SchemaEntityId { get; }
    public SchemaEntityId? DependencyId { get; }
    public SchemaPushChangeKind Kind { get; }
    public string EntityName { get; }
    public bool IsConflict { get; }
    public string Description { get; }
    public string UseSchemaLabel { get; }
    public string KeepTrackerLabel { get; }

    /// <summary>Identifies the change within its Tracker's plan, so a choice can be carried over.</summary>
    internal (SchemaEntityId, SchemaPushChangeKind, SchemaEntityId?) Key => (SchemaEntityId, Kind, DependencyId);

    /// <summary>Use schema, Keep tracker, or null: left for a later push.</summary>
    public SchemaPushChoice? Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            _choice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choice)));
        }
    }
}

/// <summary>
/// What a push offers one linked Tracker. <see cref="HasBaselineUpdate"/> says its baseline moves on even
/// without a choice: the Tracker already has some of the schema's changes.
/// </summary>
public sealed record TrackerPushPlan(
    Tracker Tracker,
    string Fingerprint,
    IReadOnlyList<SchemaPushChange> Changes,
    bool HasBaselineUpdate)
{
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>A push of a schema to every active Tracker linked to it, as reviewed before it is applied.</summary>
public sealed class SchemaPushReview(
    SchemaContent schema,
    string schemaFingerprint,
    IReadOnlyList<TrackerPushPlan> trackers)
{
    public SchemaContent Schema { get; } = schema;
    public string SchemaFingerprint { get; } = schemaFingerprint;
    public IReadOnlyList<TrackerPushPlan> Trackers { get; } = trackers;
    public IReadOnlyList<SchemaPushChange> Changes { get; } =
        trackers.SelectMany(static tracker => tracker.Changes).ToArray();
    public bool HasChanges => Changes.Count > 0;
    public int DecidedCount => Changes.Count(static change => change.Choice is not null);

    /// <summary>Whether pushing does anything: a change is decided, or a baseline moves on by itself.</summary>
    public bool CanApply => DecidedCount > 0 || Trackers.Any(static tracker => tracker.HasBaselineUpdate);
}

/// <summary>
/// The schema's changes since it was last pushed, and the active Trackers linked to it. A Tracker created
/// after a push already has the schema's current state, so only Trackers behind it count.
/// </summary>
public sealed record SchemaPendingChanges(
    IReadOnlyList<string> Changes,
    int LinkedTrackerCount,
    int TrackersBehindCount)
{
    public bool HasChanges => Changes.Count > 0;
}
