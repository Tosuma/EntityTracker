using System.ComponentModel;
using System.Text.Json.Serialization;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

/// <summary>A dependency by name. Baselines saved before dependency kinds were removed carry a
/// "Kind" property too; it is ignored when they are read.</summary>
public sealed record TrackerSyncDependency(string Name);

public sealed record TrackerSyncEntity(
    string Name,
    bool Active,
    int? RequestedPriority,
    string GroupName,
    IReadOnlyList<TrackerSyncDependency> Dependencies,
    // Missing (null) in baselines saved before Filter active was synced.
    string? FilterActive = null,
    // Left out when there is none, so baselines without due dates read as before.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DueDate = null);

public sealed record TrackerSyncStructure(IReadOnlyList<TrackerSyncEntity> Entities);

public sealed record TrackerSyncBaseline(
    TrackerSyncStructure Source,
    TrackerSyncStructure Destination)
{
    /// <summary>
    /// The same baseline with entities, and each entity's dependencies, in name-key order. Baselines
    /// read back from the database come in its own name order, so they are normalized before they are
    /// written to a snapshot or compared in a merge.
    /// </summary>
    public TrackerSyncBaseline Normalize() => new(Normalize(Source), Normalize(Destination));

    private static TrackerSyncStructure Normalize(TrackerSyncStructure structure) => new(structure.Entities
        .OrderBy(static entity => TrackerSyncPlanner.Key(entity.Name), StringComparer.Ordinal)
        .ThenBy(static entity => entity.Name, StringComparer.Ordinal)
        .Select(static entity => entity with
        {
            Dependencies = entity.Dependencies
                .OrderBy(static dependency => TrackerSyncPlanner.Key(dependency.Name), StringComparer.Ordinal)
                .ThenBy(static dependency => dependency.Name, StringComparer.Ordinal)
                .ToArray()
        })
        .ToArray());
}

public sealed record TrackerSyncPreview(
    ProgressSnapshotState CurrentProgress,
    ProgressSnapshotState ResultProgress,
    int UnresolvedDependencyCount);

public enum TrackerSyncChoice { Source, Destination }

public sealed record TrackerSyncChoiceOption(TrackerSyncChoice Value, string Label);

public enum TrackerSyncChangeKind { Entity, Dependency, RequestedPriority, Group, FilterActive, DueDate }

public sealed class TrackerSyncChange : INotifyPropertyChanged
{
    private TrackerSyncChoice? _choice;

    public TrackerSyncChange(
        TrackerSyncChangeKind kind,
        string entityName,
        string? dependencyName,
        string sourceValue,
        string destinationValue)
    {
        Kind = kind;
        EntityName = entityName;
        DependencyName = dependencyName;
        SourceValue = sourceValue;
        DestinationValue = destinationValue;
        Choices = kind == TrackerSyncChangeKind.Dependency &&
                  (sourceValue == "Absent" || destinationValue == "Absent")
            ? [new(TrackerSyncChoice.Source, sourceValue == "Absent" ? "Remove dependency" : "Keep dependency"),
               new(TrackerSyncChoice.Destination, destinationValue == "Absent" ? "Remove dependency" : "Keep dependency")]
            : [new(TrackerSyncChoice.Source, $"Use source: {sourceValue}"),
               new(TrackerSyncChoice.Destination, $"Keep this tracker: {destinationValue}")];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public TrackerSyncChangeKind Kind { get; }
    public string EntityName { get; }
    public string? DependencyName { get; }
    public string SourceValue { get; }
    public string DestinationValue { get; }
    public IReadOnlyList<TrackerSyncChoiceOption> Choices { get; }
    public string Label => Kind switch
    {
        TrackerSyncChangeKind.Entity => $"{EntityName} — entity",
        TrackerSyncChangeKind.Dependency => $"{EntityName} → {DependencyName} — dependency",
        TrackerSyncChangeKind.RequestedPriority => $"{EntityName} — requested priority",
        TrackerSyncChangeKind.FilterActive => $"{EntityName} — Filter active",
        TrackerSyncChangeKind.DueDate => $"{EntityName} — due date",
        _ => $"{EntityName} — group"
    };

    public TrackerSyncChoice? Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            if (value is not null && !Choices.Any(option => option.Value == value.Value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _choice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choice)));
        }
    }
}

public sealed class TrackerSyncReview(
    TrackerId sourceTrackerId,
    TrackerId destinationTrackerId,
    TrackerSyncStructure source,
    TrackerSyncStructure destination,
    TrackerSyncBaseline? baseline,
    string sourceFingerprint,
    string destinationFingerprint,
    IReadOnlyList<TrackerSyncChange> changes)
{
    public TrackerId SourceTrackerId { get; } = sourceTrackerId;
    public TrackerId DestinationTrackerId { get; } = destinationTrackerId;
    public TrackerSyncStructure Source { get; } = source;
    public TrackerSyncStructure Destination { get; } = destination;
    public TrackerSyncBaseline? Baseline { get; } = baseline;
    public string SourceFingerprint { get; } = sourceFingerprint;
    public string DestinationFingerprint { get; } = destinationFingerprint;
    public IReadOnlyList<TrackerSyncChange> Changes { get; } = changes;
    public bool CanApply => Changes.All(static change => change.Choice is not null);
}
