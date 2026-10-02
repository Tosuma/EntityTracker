using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed class DeveloperPickerViewModel : INotifyPropertyChanged
{
    private readonly TrackerId _trackerId;
    private readonly ProjectDeveloperService _service;
    private string _query = string.Empty;

    public DeveloperPickerViewModel(TrackerId trackerId, ProjectDeveloperService service)
    {
        _trackerId = trackerId;
        _service = service;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectionChanged;
    public ObservableCollection<DeveloperChoice> Choices { get; } = [];
    public ObservableCollection<DeveloperChoice> FilteredChoices { get; } = [];
    public bool IsQueryEmpty => string.IsNullOrEmpty(Query);
    public bool HasAvailableDevelopers => Choices.Count > 0;
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(IsQueryEmpty));
            Filter();
        }
    }
    public IReadOnlyList<DeveloperId> SelectedIds => Choices.Where(c => c.IsSelected)
        .Select(c => c.Developer.Id).ToArray();

    public async Task LoadAsync(IEnumerable<DeveloperId>? selected = null,
        CancellationToken cancellationToken = default)
    {
        HashSet<DeveloperId> ids = (selected ?? SelectedIds).ToHashSet();
        IReadOnlyList<ProjectDeveloper> available = (await _service.ListForTrackerAsync(_trackerId, cancellationToken))
            .Where(d => !d.IsRetired).ToArray();
        Choices.Clear();
        foreach (ProjectDeveloper developer in available)
        {
            DeveloperChoice choice = new(developer, ids.Contains(developer.Id));
            choice.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DeveloperChoice.IsSelected))
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            Choices.Add(choice);
        }
        OnPropertyChanged(nameof(HasAvailableDevelopers));
        Filter();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        foreach (DeveloperChoice choice in Choices) choice.IsSelected = false;
        Query = string.Empty;
    }

    public bool Select(DeveloperId id)
    {
        DeveloperChoice? choice = Choices.FirstOrDefault(item => item.Developer.Id == id);
        if (choice is null) return false;
        choice.IsSelected = true;
        return true;
    }

    private void Filter()
    {
        FilteredChoices.Clear();
        foreach (DeveloperChoice choice in Choices.Where(c =>
                     c.Developer.Initials.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     c.Developer.DisplayName.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)))
            FilteredChoices.Add(choice);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DeveloperChoice(ProjectDeveloper developer, bool isSelected) : INotifyPropertyChanged
{
    private bool _isSelected = isSelected;
    public ProjectDeveloper Developer { get; } = developer;
    public string Label => string.IsNullOrEmpty(Developer.DisplayName)
        ? Developer.Initials : $"{Developer.Initials} — {Developer.DisplayName}";
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
