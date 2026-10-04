using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class LocalProjectIdentitySettingsViewModel : INotifyPropertyChanged
{
    private readonly LocalProjectIdentityService _identity;
    private readonly AsyncCommand _clearCommand;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ProjectId? _projectId;
    private DeveloperChoice? _selectedDeveloper;
    private bool _isBusy;
    private int _pendingOperations;
    private readonly NotificationCenter? _notifications;
    private string? _errorMessage;
    private int _refreshVersion;

    public LocalProjectIdentitySettingsViewModel(LocalProjectIdentityService identity,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        _identity = identity;
        _clearCommand = new AsyncCommand(ClearAsync, () => HasProject && !_isBusy && SelectedDeveloper is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DeveloperChoice> AvailableDevelopers { get; } = [];
    public bool HasProject => _projectId is not null;
    public bool CanChoose => HasProject && !_isBusy;
    public bool HasAvailableDevelopers => AvailableDevelopers.Count > 0;
    public string ProjectName { get; private set; } = "No Project selected";
    public string SelectionDescription => SelectedDeveloper?.Label ?? "No Developer selected";
    public bool IsBusy => _isBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public ICommand ClearCommand => _clearCommand;
    public DeveloperChoice? SelectedDeveloper
    {
        get => _selectedDeveloper;
        set
        {
            if (ReferenceEquals(_selectedDeveloper, value)) return;
            _selectedDeveloper = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionDescription));
            _clearCommand.NotifyCanExecuteChanged();
            if (CanChoose) _ = SaveSelectionAsync(value?.Developer.Id);
        }
    }

    public async Task SetProjectAsync(ProjectId? projectId, string? projectName,
        CancellationToken cancellationToken = default)
    {
        _projectId = projectId;
        ProjectName = projectName ?? "No Project selected";
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(CanChoose));
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        int version = ++_refreshVersion;
        SetBusy(true);
        _errorMessage = null;
        NotifyState();
        bool entered = false;
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            entered = true;
            if (version != _refreshVersion) return;
            ProjectId? projectId = _projectId;
            ProjectDeveloper? selected = projectId is null ? null :
                await _identity.ResolveAsync(projectId, cancellationToken);
            IReadOnlyList<ProjectDeveloper> developers = projectId is null ? [] :
                await _identity.AvailableAsync(projectId, cancellationToken);
            if (version != _refreshVersion) return;
            AvailableDevelopers.Clear();
            foreach (ProjectDeveloper developer in developers)
                AvailableDevelopers.Add(new DeveloperChoice(developer, false));
            SelectedDeveloper = AvailableDevelopers.FirstOrDefault(item => item.Developer.Id == selected?.Id);
            OnPropertyChanged(nameof(HasAvailableDevelopers));
        }
        catch (Exception)
        {
            ReportError("The local Developer choice could not be loaded.");
        }
        finally { if (entered) _operationGate.Release(); SetBusy(false); }
    }

    private async Task SaveSelectionAsync(DeveloperId? developerId)
    {
        ProjectId? projectId = _projectId;
        if (projectId is null) return;
        SetBusy(true);
        bool entered = false;
        try { await _operationGate.WaitAsync(); entered = true;
            await _identity.SetAsync(projectId, developerId); _errorMessage = null; }
        catch (Exception) { ReportError("The local Developer choice could not be saved."); }
        finally { if (entered) _operationGate.Release(); SetBusy(false); }
    }

    private async Task ClearAsync()
    {
        _selectedDeveloper = null;
        OnPropertyChanged(nameof(SelectedDeveloper));
        OnPropertyChanged(nameof(SelectionDescription));
        await SaveSelectionAsync(null);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanChoose));
        OnPropertyChanged(nameof(HasError));
        _clearCommand.NotifyCanExecuteChanged();
    }

    private void SetBusy(bool started)
    {
        _pendingOperations += started ? 1 : -1;
        _isBusy = _pendingOperations > 0;
        NotifyState();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void ReportError(string message)
    {
        _errorMessage = message;
        _notifications?.Show("Settings", message, NotificationKind.Failure);
    }
}
