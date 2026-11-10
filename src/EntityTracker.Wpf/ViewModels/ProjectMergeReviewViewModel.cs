using System.Collections.ObjectModel;
using System.ComponentModel;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectMergeReviewViewModel : INotifyPropertyChanged
{
    public ProjectMergeReviewViewModel(IReadOnlyList<ProjectMergeConflict> conflicts)
    {
        Rows = new ObservableCollection<ProjectMergeChoiceRow>(conflicts.Select(c =>
        {
            ProjectMergeChoiceRow row = new(c);
            row.PropertyChanged += (_, _) =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllResolved)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            };
            return row;
        }));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProjectMergeChoiceRow> Rows { get; }
    public bool AllResolved => Rows.All(r => r.Choice is not null);

    /// <summary>How many conflicts are decided; unlike a push, the merge needs every one.</summary>
    public string Summary
    {
        get
        {
            int decided = Rows.Count(static row => row.Choice is not null);
            string noun = Rows.Count == 1 ? "conflict" : "conflicts";
            return decided == Rows.Count
                ? $"All {Rows.Count} {noun} decided."
                : $"{decided} of {Rows.Count} {noun} decided. Choose a version for each to apply.";
        }
    }

    public void ChooseAll(MergeSide side)
    {
        foreach (ProjectMergeChoiceRow row in Rows) row.Choice = side;
    }

    public IReadOnlyDictionary<string, MergeSide> Decisions() =>
        !AllResolved ? throw new InvalidOperationException("Resolve every conflict first.") :
        Rows.ToDictionary(r => r.Conflict.Path, r => r.Choice!.Value);
}

public sealed class ProjectMergeChoiceRow(ProjectMergeConflict conflict) : INotifyPropertyChanged
{
    private MergeSide? _choice;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProjectMergeConflict Conflict { get; } = conflict;
    public string Path => Conflict.Path;
    public string Title => Conflict.Display?.Title ?? Conflict.Path;
    public string Kind => Conflict.Kind.ToString();

    /// <summary>The difference in a sentence, as push review describes a change.</summary>
    public string Description => Conflict.Kind switch
    {
        ProjectConflictKind.Addition => $"This computer and the repository each added {Subject} differently.",
        ProjectConflictKind.Deletion => $"One side deleted {Subject} and the other changed it.",
        _ => $"Both this computer and the repository changed {Subject}."
    };

    /// <summary>Whether there was an earlier value; an addition had none.</summary>
    public bool HasBase => (Conflict.Display?.BaseValue ?? Conflict.BaseValue) is { Length: > 0 } value &&
        value != "Not present";

    private string Subject => Conflict.Display?.Title ?? Conflict.Path;
    public string BaseValue => Conflict.Display?.BaseValue ?? Conflict.BaseValue ?? "Not present";
    public string LocalValue => Conflict.Display?.LocalValue ?? Conflict.LocalValue ?? "Not present";
    public string RemoteValue => Conflict.Display?.RemoteValue ?? Conflict.RemoteValue ?? "Not present";
    public string BaseAutomationName => "Base value for " + Title;
    public string LocalAutomationName => "Local value for " + Title;
    public string RemoteAutomationName => "Remote value for " + Title;
    public string KeepLocalAutomationName => "Keep local value for " + Title;
    public string KeepRemoteAutomationName => "Keep remote value for " + Title;
    public MergeSide? Choice
    {
        get => _choice;
        set
        {
            if (_choice == value) return;
            _choice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choice)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRemote)));
        }
    }
    public bool IsLocal
    {
        get => Choice == MergeSide.Local;
        set { if (value) Choice = MergeSide.Local; }
    }
    public bool IsRemote
    {
        get => Choice == MergeSide.Remote;
        set { if (value) Choice = MergeSide.Remote; }
    }
}
