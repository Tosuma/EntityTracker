using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class DeveloperPickerViewModel : INotifyPropertyChanged
{
    private readonly TrackerId _trackerId;
    private readonly ProjectDeveloperService _service;
    private string _query = string.Empty;
    private string _newInitials = string.Empty;
    private string _newDisplayName = string.Empty;
    private string? _error;
    private bool _busy;

    public DeveloperPickerViewModel(TrackerId trackerId, ProjectDeveloperService service)
    {
        _trackerId = trackerId;
        _service = service;
        CreateCommand = new AsyncCommand(CreateAsync, () => !_busy && !string.IsNullOrWhiteSpace(NewInitials));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectionChanged;
    public ObservableCollection<DeveloperChoice> Choices { get; } = [];
    public ObservableCollection<DeveloperChoice> FilteredChoices { get; } = [];
    public ICommand CreateCommand { get; }
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? string.Empty)) Filter(); }
    }
    public string NewInitials
    {
        get => _newInitials;
        set
        {
            if (Set(ref _newInitials, value ?? string.Empty))
                ((AsyncCommand)CreateCommand).NotifyCanExecuteChanged();
        }
    }
    public string NewDisplayName
    {
        get => _newDisplayName;
        set => Set(ref _newDisplayName, value ?? string.Empty);
    }
    public string? Error { get => _error; private set => Set(ref _error, value); }
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
        Filter();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        foreach (DeveloperChoice choice in Choices) choice.IsSelected = false;
        Query = string.Empty;
    }

    public async Task CreateAsync()
    {
        _busy = true;
        Error = null;
        try
        {
            ProjectDeveloper developer = await _service.CreateForTrackerAsync(
                _trackerId, NewInitials, NewDisplayName);
            DeveloperId[] selected = [.. SelectedIds, developer.Id];
            await LoadAsync(selected);
            NewInitials = string.Empty;
            NewDisplayName = string.Empty;
            Query = string.Empty;
        }
        catch (Exception exception) { Error = exception.Message; }
        finally { _busy = false; ((AsyncCommand)CreateCommand).NotifyCanExecuteChanged(); }
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
