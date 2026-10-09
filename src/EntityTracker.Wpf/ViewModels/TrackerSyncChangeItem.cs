using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Tracking;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>The differences for one entity, shown together in the sync dialog.</summary>
public sealed record TrackerSyncEntityGroup(string EntityName, IReadOnlyList<TrackerSyncChangeItem> Changes);

/// <summary>
/// One difference between a copied Tracker and its source, worded with both Trackers' names: what
/// differs, and the two possible outcomes for the copy.
/// </summary>
public sealed class TrackerSyncChangeItem : INotifyPropertyChanged
{
    public TrackerSyncChangeItem(TrackerSyncChange change, TrackerSyncReview review, string sourceName, string copyName)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        ArgumentNullException.ThrowIfNull(review);
        (Description, UseSourceLabel, KeepCopyLabel) = Describe(change, review, sourceName, copyName);
        change.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TrackerSyncChange.Choice)) return;
            OnPropertyChanged(nameof(UseSource));
            OnPropertyChanged(nameof(KeepCopy));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackerSyncChange Change { get; }

    /// <summary>Gets a unique group for this difference's two radio buttons.</summary>
    public string ChoiceGroup { get; } = Guid.NewGuid().ToString("N");

    public string Description { get; }

    public string UseSourceLabel { get; }

    public string KeepCopyLabel { get; }

    public bool UseSource
    {
        get => Change.Choice == TrackerSyncChoice.Source;
        set { if (value) Change.Choice = TrackerSyncChoice.Source; }
    }

    public bool KeepCopy
    {
        get => Change.Choice == TrackerSyncChoice.Destination;
        set { if (value) Change.Choice = TrackerSyncChoice.Destination; }
    }

    internal static (string Description, string UseSource, string KeepCopy) Describe(
        TrackerSyncChange change, TrackerSyncReview review, string source, string copy)
    {
        string entity = change.EntityName;
        switch (change.Kind)
        {
            case TrackerSyncChangeKind.Entity:
            {
                TrackerSyncEntity? inSource = Find(review.Source, entity);
                TrackerSyncEntity? inCopy = Find(review.Destination, entity);
                if (inSource?.Active == true)
                {
                    string details = Details(inSource);
                    return inCopy is null
                        ? ($"New in {source}{details}. Add it to {copy}?",
                            $"Add {entity} to {copy}", "Leave it out")
                        : ($"Active in {source}{details}, archived in {copy}. Restore it in {copy}?",
                            $"Restore {entity} in {copy}", "Keep it archived");
                }

                return inSource is null
                    ? ($"Not in {source}, but active in {copy}. It was removed in {source}, or added only in {copy}.",
                        $"Archive {entity} in {copy}", $"Keep it in {copy}")
                    : ($"Archived in {source}, still active in {copy}.",
                        $"Archive {entity} in {copy}", $"Keep it in {copy}");
            }
            case TrackerSyncChangeKind.Dependency:
            {
                string target = change.DependencyName!;
                return change.SourceValue == TrackerSyncPlanner.Present
                    ? ($"In {source}, {entity} depends on {target}. In {copy} it does not.",
                        $"Add the dependency on {target}", "Leave it out")
                    : ($"In {copy}, {entity} depends on {target}. In {source} it does not.",
                        $"Remove the dependency on {target}", "Keep it");
            }
            case TrackerSyncChangeKind.RequestedPriority:
            {
                string sourceValue = Value(change.SourceValue), copyValue = Value(change.DestinationValue);
                return ($"Requested priority: {sourceValue} in {source}, {copyValue} in {copy}.",
                    $"Use {sourceValue} (from {source})", $"Keep {copyValue}");
            }
            default:
            {
                string sourceValue = Value(change.SourceValue), copyValue = Value(change.DestinationValue);
                return ($"Group: {sourceValue} in {source}, {copyValue} in {copy}.",
                    $"Use {sourceValue} (from {source})", $"Keep {copyValue}");
            }
        }
    }

    /// <summary>What a whole entity brings along, such as ", with 2 dependencies (customer, country) · group Sales".</summary>
    private static string Details(TrackerSyncEntity entity)
    {
        List<string> parts =
        [
            entity.Dependencies.Count switch
            {
                0 => "with no dependencies",
                1 => $"with 1 dependency ({entity.Dependencies[0].Name})",
                _ => $"with {entity.Dependencies.Count} dependencies ({string.Join(", ", entity.Dependencies.Select(static item => item.Name))})"
            }
        ];
        if (!string.IsNullOrEmpty(entity.GroupName)) parts.Add($"group {entity.GroupName}");
        if (entity.RequestedPriority is { } priority) parts.Add($"priority {priority}");
        return ", " + string.Join(" · ", parts);
    }

    private static TrackerSyncEntity? Find(TrackerSyncStructure structure, string name) =>
        structure.Entities.FirstOrDefault(entity =>
            TrackerSyncPlanner.Key(entity.Name) == TrackerSyncPlanner.Key(name));

    private static string Value(string value) => value == "None" ? "none" : value;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
