using System.ComponentModel;

using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>
/// One schema entity as a linked Tracker last took it from its schema: its name, group and dependencies
/// then, and the Tracker entity linked to it. <see cref="EntityId"/> is null when the linked entity was
/// permanently deleted.
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

public enum SchemaPushConflictKind
{
    /// <summary>The schema renamed an entity the Tracker renamed differently.</summary>
    Name,

    /// <summary>The schema regrouped an entity the Tracker regrouped differently.</summary>
    Group,

    /// <summary>A dependency differs on an entity linked by name, which has no baseline yet.</summary>
    Dependency,

    /// <summary>The schema removed an entity the Tracker changed.</summary>
    Removed
}

/// <summary>
/// A difference a push can't settle alone: the schema changed something the Tracker changed too. Its
/// <see cref="Choice"/> says whether the schema's value or the Tracker's is kept.
/// </summary>
public sealed class SchemaPushConflict : INotifyPropertyChanged
{
    private SchemaPushChoice? _choice;

    public SchemaPushConflict(
        TrackerId trackerId,
        string trackerName,
        SchemaEntityId schemaEntityId,
        SchemaEntityId? dependencyId,
        SchemaPushConflictKind kind,
        string entityName,
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
        Description = description;
        UseSchemaLabel = useSchemaLabel;
        KeepTrackerLabel = keepTrackerLabel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackerId TrackerId { get; }
    public string TrackerName { get; }
    public SchemaEntityId SchemaEntityId { get; }
    public SchemaEntityId? DependencyId { get; }
    public SchemaPushConflictKind Kind { get; }
    public string EntityName { get; }
    public string Description { get; }
    public string UseSchemaLabel { get; }
    public string KeepTrackerLabel { get; }

    /// <summary>Identifies the conflict within its Tracker's plan, so a choice can be carried over.</summary>
    internal (SchemaEntityId, SchemaPushConflictKind, SchemaEntityId?) Key => (SchemaEntityId, Kind, DependencyId);

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

/// <summary>What a push does to one linked Tracker: the changes it makes without asking, and its conflicts.</summary>
public sealed record TrackerPushPlan(
    Tracker Tracker,
    string Fingerprint,
    IReadOnlyList<string> AutomaticChanges,
    IReadOnlyList<SchemaPushConflict> Conflicts)
{
    public bool HasChanges => AutomaticChanges.Count > 0 || Conflicts.Count > 0;
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
    public IReadOnlyList<SchemaPushConflict> Conflicts { get; } =
        trackers.SelectMany(static tracker => tracker.Conflicts).ToArray();
    public bool HasChanges => Trackers.Any(static tracker => tracker.HasChanges);
    public bool CanApply => Conflicts.All(static conflict => conflict.Choice is not null);
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
