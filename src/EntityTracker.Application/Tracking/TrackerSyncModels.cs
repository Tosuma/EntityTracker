using System.ComponentModel;
using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed record TrackerSyncDependency(string Name, ImportedDependencyKind Kind);

public sealed record TrackerSyncEntity(
    string Name,
    bool Active,
    int? RequestedPriority,
    string GroupName,
    IReadOnlyList<TrackerSyncDependency> Dependencies);

public sealed record TrackerSyncStructure(IReadOnlyList<TrackerSyncEntity> Entities);

public sealed record TrackerSyncBaseline(
    TrackerSyncStructure Source,
    TrackerSyncStructure Destination);

public sealed record TrackerSyncPreview(
    ProgressSnapshotState CurrentProgress,
    ProgressSnapshotState ResultProgress,
    int UnresolvedDependencyCount);

public enum TrackerSyncChoice { Source, Destination, Both }

public enum TrackerSyncChangeKind { Entity, Dependency, RequestedPriority, Group }

public sealed class TrackerSyncChange : INotifyPropertyChanged
{
    private TrackerSyncChoice? _choice;

    public TrackerSyncChange(
        TrackerSyncChangeKind kind,
        string entityName,
        string? dependencyName,
        string sourceValue,
        string destinationValue,
        bool allowBoth)
    {
        Kind = kind;
        EntityName = entityName;
        DependencyName = dependencyName;
        SourceValue = sourceValue;
        DestinationValue = destinationValue;
        Choices = allowBoth
            ? [TrackerSyncChoice.Source, TrackerSyncChoice.Destination, TrackerSyncChoice.Both]
            : [TrackerSyncChoice.Source, TrackerSyncChoice.Destination];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public TrackerSyncChangeKind Kind { get; }
    public string EntityName { get; }
    public string? DependencyName { get; }
    public string SourceValue { get; }
    public string DestinationValue { get; }
    public IReadOnlyList<TrackerSyncChoice> Choices { get; }
    public string Label => Kind switch
    {
        TrackerSyncChangeKind.Entity => $"{EntityName} — entity",
        TrackerSyncChangeKind.Dependency => $"{EntityName} → {DependencyName} — dependency",
        TrackerSyncChangeKind.RequestedPriority => $"{EntityName} — requested priority",
        _ => $"{EntityName} — group"
    };

    public TrackerSyncChoice? Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            if (value is not null && !Choices.Contains(value.Value))
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
