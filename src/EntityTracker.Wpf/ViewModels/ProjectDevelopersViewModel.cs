using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectDevelopersViewModel : INotifyPropertyChanged
{
    private readonly ProjectDeveloperService _service;
    private readonly ProjectId _projectId;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _confirmRetireCommand;
    private IReadOnlyList<ProjectDeveloper> _all = [];
    private IReadOnlyList<ProjectDeveloper> _available = [];
    private IReadOnlyList<ProjectDeveloper> _retired = [];
    private DeveloperId? _editingId;
    private string _initials = string.Empty;
    private string _displayName = string.Empty;
    private string _baselineInitials = string.Empty;
    private string _baselineDisplayName = string.Empty;
    private string _searchQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _isRetiredDialogOpen;
    private ProjectDeveloper? _pendingRetirement;
    private string _retirementConfirmation = string.Empty;

    public ProjectDevelopersViewModel(ProjectId projectId, ProjectDeveloperService service)
    {
        _projectId = projectId;
        _service = service;
        EditCommand = new RelayCommand<ProjectDeveloper>(Edit, _ => !IsBusy && !HasUnsavedForm);
        CancelCommand = new RelayCommand(Cancel, () => !IsBusy && (IsEditing || HasUnsavedForm));
        _saveCommand = new AsyncCommand(SaveAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(Initials));
        RetireCommand = new RelayCommand<ProjectDeveloper>(BeginRetirement,
            developer => !IsBusy && !developer.IsRetired);
        _confirmRetireCommand = new AsyncCommand(ConfirmRetirementAsync,
            () => !IsBusy && _pendingRetirement is not null &&
                  RetirementConfirmation == _pendingRetirement.Initials);
        CancelRetirementCommand = new RelayCommand(CancelRetirement,
            () => IsRetirementOpen && !IsBusy);
        OpenRetiredCommand = new RelayCommand(OpenRetired, () => !IsBusy);
        CloseRetiredCommand = new RelayCommand(CloseRetired,
            () => IsRetiredDialogOpen && !IsBusy);
        RestoreCommand = new AsyncCommand<ProjectDeveloper>(
            developer => SetRetiredAsync(developer, false),
            developer => !IsBusy && developer.IsRetired);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ProjectId ProjectId => _projectId;
    public IReadOnlyList<ProjectDeveloper> Available => _available;
    public IReadOnlyList<ProjectDeveloper> Retired => _retired;
    public bool HasAvailable => Available.Count > 0;
    public bool HasRetired => Retired.Count > 0;
    public bool ShowNoRetired => !HasRetired;
    public bool IsRetiredDialogOpen
    {
        get => _isRetiredDialogOpen;
        private set { if (Set(ref _isRetiredDialogOpen, value)) NotifyCommands(); }
    }
    public bool IsEditing => _editingId is not null;
    public bool HasUnsavedForm => Initials != _baselineInitials || DisplayName != _baselineDisplayName;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) NotifyCommands(); } }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsRetirementOpen => _pendingRetirement is not null;
    public string PendingRetirementInitials => _pendingRetirement?.Initials ?? string.Empty;
    public string RetirementConfirmation
    {
        get => _retirementConfirmation;
        set { if (Set(ref _retirementConfirmation, value ?? string.Empty))
                _confirmRetireCommand.NotifyCanExecuteChanged(); }
    }
    public string EditorTitle => _editingId is null ? "Add developer" : "Edit developer";
    public string SaveLabel => IsEditing ? "Save changes" : "Save developer";
    public string ResetLabel => IsEditing ? "Cancel edit" : "Clear";
    public string Initials
    {
        get => _initials;
        set { if (Set(ref _initials, value ?? string.Empty)) FormChanged(); }
    }
    public string DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value ?? string.Empty)) FormChanged(); }
    }
    public string SearchQuery
    {
        get => _searchQuery;
        set { if (Set(ref _searchQuery, value ?? string.Empty)) Filter(); }
    }

    public ICommand EditCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveCommand => _saveCommand;
    public ICommand RetireCommand { get; }
    public ICommand ConfirmRetirementCommand => _confirmRetireCommand;
    public ICommand CancelRetirementCommand { get; }
    public ICommand OpenRetiredCommand { get; }
    public ICommand CloseRetiredCommand { get; }
    public ICommand RestoreCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _all = await _service.ListAsync(_projectId, cancellationToken);
        Filter();
    }

    public void New()
    {
        CloseRetired();
        CancelRetirement();
        _editingId = null;
        _baselineInitials = string.Empty;
        _baselineDisplayName = string.Empty;
        Initials = string.Empty;
        DisplayName = string.Empty;
        ErrorMessage = null;
        FormModeChanged();
        NotifyCommands();
    }

    public void Edit(ProjectDeveloper developer)
    {
        if (HasUnsavedForm || IsBusy) return;
        CloseRetired();
        CancelRetirement();
        _editingId = developer.Id;
        _baselineInitials = developer.Initials;
        _baselineDisplayName = developer.DisplayName;
        Initials = developer.Initials;
        DisplayName = developer.DisplayName;
        ErrorMessage = null;
        FormModeChanged();
        NotifyCommands();
    }

    public void Cancel() => New();

    public async Task SaveAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Initials)) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (_editingId is null)
                await _service.CreateAsync(_projectId, Initials, DisplayName);
            else
                await _service.ChangeDetailsAsync(_projectId, _editingId, Initials, DisplayName);
            New();
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public async Task SetRetiredAsync(ProjectDeveloper developer, bool retired)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _service.SetRetiredAsync(_projectId, developer.Id, retired);
            await RefreshAsync();
        }
        catch (InvalidOperationException exception) { ErrorMessage = exception.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public void BeginRetirement(ProjectDeveloper developer)
    {
        if (IsBusy || developer.IsRetired) return;
        _pendingRetirement = developer;
        RetirementConfirmation = string.Empty;
        OnPropertyChanged(nameof(IsRetirementOpen));
        OnPropertyChanged(nameof(PendingRetirementInitials));
        NotifyCommands();
    }

    public void CancelRetirement()
    {
        if (_pendingRetirement is null) return;
        _pendingRetirement = null;
        RetirementConfirmation = string.Empty;
        OnPropertyChanged(nameof(IsRetirementOpen));
        OnPropertyChanged(nameof(PendingRetirementInitials));
        NotifyCommands();
    }

    public void OpenRetired() => IsRetiredDialogOpen = true;

    public void CloseRetired() => IsRetiredDialogOpen = false;

    public async Task ConfirmRetirementAsync()
    {
        ProjectDeveloper? developer = _pendingRetirement;
        if (developer is null || IsBusy || RetirementConfirmation != developer.Initials) return;
        await SetRetiredAsync(developer, true);
        if (!HasError)
        {
            CancelRetirement();
            if (_editingId == developer.Id) New();
        }
    }

    private void Filter()
    {
        string query = SearchQuery.Trim();
        IEnumerable<ProjectDeveloper> matches = _all.Where(d => query.Length == 0 ||
            d.Initials.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
        _available = matches.Where(d => !d.IsRetired)
            .OrderBy(d => d.Initials, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id.Value).ToArray();
        _retired = matches.Where(d => d.IsRetired)
            .OrderBy(d => d.Initials, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id.Value).ToArray();
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(Retired));
        OnPropertyChanged(nameof(HasAvailable));
        OnPropertyChanged(nameof(HasRetired));
        OnPropertyChanged(nameof(ShowNoRetired));
    }

    private void NotifyCommands()
    {
        _saveCommand.NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        ((RelayCommand<ProjectDeveloper>)EditCommand).NotifyCanExecuteChanged();
        ((RelayCommand<ProjectDeveloper>)RetireCommand).NotifyCanExecuteChanged();
        ((AsyncCommand<ProjectDeveloper>)RestoreCommand).NotifyCanExecuteChanged();
        _confirmRetireCommand.NotifyCanExecuteChanged();
        ((RelayCommand)CancelRetirementCommand).NotifyCanExecuteChanged();
        ((RelayCommand)OpenRetiredCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CloseRetiredCommand).NotifyCanExecuteChanged();
    }

    private void FormChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedForm));
        NotifyCommands();
    }

    private void FormModeChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(ResetLabel));
        OnPropertyChanged(nameof(HasUnsavedForm));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(property);
        if (property == nameof(ErrorMessage)) OnPropertyChanged(nameof(HasError));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
