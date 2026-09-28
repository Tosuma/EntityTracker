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
            row.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(AllResolved)));
            return row;
        }));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProjectMergeChoiceRow> Rows { get; }
    public bool AllResolved => Rows.All(r => r.Choice is not null);

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
    public string Kind => Conflict.Kind.ToString();
    public string BaseValue => Conflict.BaseValue ?? "(absent)";
    public string LocalValue => Conflict.LocalValue ?? "(absent)";
    public string RemoteValue => Conflict.RemoteValue ?? "(absent)";
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
