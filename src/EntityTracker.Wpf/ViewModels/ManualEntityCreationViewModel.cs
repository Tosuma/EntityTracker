using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ManualEntityCreationViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

    private readonly ManualEntityCreationService _service;
    private readonly Func<Task> _onCreated;
    private readonly Func<EntityId, Task> _onRestoreArchived;
    private readonly Action _onCancelled;
    private readonly Func<bool> _canOperate;
    private readonly AsyncCommand _createCommand;
    private readonly AsyncCommand _restoreArchivedCommand;
    private readonly RelayCommand<ManualDependencySuggestion> _addExistingCommand;
    private readonly RelayCommand<ManualDependencyRow> _removeDependencyCommand;
    private readonly RelayCommand _addUnresolvedCommand;
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private string _entityName = string.Empty;
    private string _dependencyQuery = string.Empty;
    private IReadOnlyList<ManualDependencySuggestion> _suggestions = [];
    private IReadOnlyList<string> _errors = [];
    private IReadOnlyList<string> _warnings = [];
    private string? _searchMessage;
    private string? _operationMessage;
    private bool _canAddAsUnresolved;
    private bool _isBusy;
    private ArchivedEntityMatch? _archivedEntityMatch;

    public ManualEntityCreationViewModel(
        ManualEntityCreationService service,
        Func<Task> onCreated,
        Func<EntityId, Task> onRestoreArchived,
        Action onCancelled,
        Func<bool>? canOperate = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(onCreated);
        ArgumentNullException.ThrowIfNull(onRestoreArchived);
        ArgumentNullException.ThrowIfNull(onCancelled);

        _service = service;
        _onCreated = onCreated;
        _onRestoreArchived = onRestoreArchived;
        _onCancelled = onCancelled;
        _canOperate = canOperate ?? (() => true);
        SelectedDependencies = [];
        _addExistingCommand = new RelayCommand<ManualDependencySuggestion>(
            AddExisting,
            _ => !IsBusy && _canOperate());
        _removeDependencyCommand = new RelayCommand<ManualDependencyRow>(
            RemoveDependency,
            _ => !IsBusy && _canOperate());
        _addUnresolvedCommand = new RelayCommand(
            AddUnresolved,
            () => !IsBusy && _canOperate() && CanAddAsUnresolved);
        _createCommand = new AsyncCommand(
            () => CreateAsync(),
            () => !IsBusy && _canOperate() && !string.IsNullOrWhiteSpace(EntityName));
        _restoreArchivedCommand = new AsyncCommand(
            RestoreArchivedAsync,
            () => !IsBusy && _canOperate() && ArchivedEntityMatch is not null);
        _cancelCommand = new RelayCommand(Cancel, () => !IsBusy && _canOperate());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EntityName
    {
        get => _entityName;
        set
        {
            if (SetField(ref _entityName, value ?? string.Empty))
            {
                ArchivedEntityMatch = null;
                _createCommand.NotifyCanExecuteChanged();
                ScheduleSearch();
            }
        }
    }

    public string DependencyQuery
    {
        get => _dependencyQuery;
        set
        {
            if (SetField(ref _dependencyQuery, value ?? string.Empty))
            {
                ScheduleSearch();
            }
        }
    }

    public IReadOnlyList<ManualDependencySuggestion> Suggestions
    {
        get => _suggestions;
        private set
        {
            if (SetField(ref _suggestions, value))
            {
                OnPropertyChanged(nameof(HasSuggestions));
            }
        }
    }

    public ObservableCollection<ManualDependencyRow> SelectedDependencies { get; }

    public IReadOnlyList<string> Errors
    {
        get => _errors;
        private set
        {
            if (SetField(ref _errors, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        private set
        {
            if (SetField(ref _warnings, value))
            {
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public string? SearchMessage
    {
        get => _searchMessage;
        private set
        {
            if (SetField(ref _searchMessage, value))
            {
                OnPropertyChanged(nameof(HasSearchMessage));
            }
        }
    }

    public string? OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetField(ref _operationMessage, value))
            {
                OnPropertyChanged(nameof(HasOperationMessage));
            }
        }
    }

    public bool CanAddAsUnresolved
    {
        get => _canAddAsUnresolved;
        private set
        {
            if (SetField(ref _canAddAsUnresolved, value))
            {
                _addUnresolvedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ArchivedEntityMatch? ArchivedEntityMatch
    {
        get => _archivedEntityMatch;
        private set
        {
            if (SetField(ref _archivedEntityMatch, value))
            {
                OnPropertyChanged(nameof(HasArchivedEntityMatch));
                OnPropertyChanged(nameof(ArchivedEntityMessage));
                _restoreArchivedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasArchivedEntityMatch => ArchivedEntityMatch is not null;

    public string ArchivedEntityMessage => ArchivedEntityMatch is null
        ? string.Empty
        : $"'{ArchivedEntityMatch.SourceName}' already exists and is archived. " +
          "Restore it to keep its identity, progress, notes, and dependencies.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _createCommand.NotifyCanExecuteChanged();
                _addUnresolvedCommand.NotifyCanExecuteChanged();
                _cancelCommand.NotifyCanExecuteChanged();
                _addExistingCommand.NotifyCanExecuteChanged();
                _removeDependencyCommand.NotifyCanExecuteChanged();
                _restoreArchivedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasSearchMessage => !string.IsNullOrWhiteSpace(SearchMessage);

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    public ICommand AddExistingCommand => _addExistingCommand;

    public ICommand RemoveDependencyCommand => _removeDependencyCommand;

    public ICommand AddUnresolvedCommand => _addUnresolvedCommand;

    public ICommand CreateCommand => _createCommand;

    public ICommand RestoreArchivedCommand => _restoreArchivedCommand;

    public ICommand CancelCommand => _cancelCommand;

    public async Task SearchDependenciesAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !_canOperate())
        {
            return;
        }

        CancelPendingSearch();
        int version = ++_searchVersion;
        await SearchDependenciesCoreAsync(
            DependencyQuery,
            EntityName,
            version,
            cancellationToken);
    }

    private async Task SearchDependenciesCoreAsync(
        string dependencyQuery,
        string entityName,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            ManualDependencySearchResult result = await _service.SearchDependenciesAsync(
                dependencyQuery,
                entityName,
                cancellationToken);
            if (version != _searchVersion)
            {
                return;
            }

            Suggestions = result.Suggestions;
            CanAddAsUnresolved = result.CanAddAsUnresolved &&
                                 (result.EnteredKey is null ||
                                  !ContainsDependency(result.EnteredKey));
            SearchMessage = result.BlockingMessage ??
                            (CanAddAsUnresolved
                                ? $"No existing entity exactly matches '{result.EnteredName}'."
                                : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version != _searchVersion)
            {
                return;
            }

            Suggestions = [];
            CanAddAsUnresolved = false;
            SearchMessage = $"Dependencies could not be searched: {exception.Message}";
        }
    }

    public async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !_canOperate())
        {
            return;
        }

        CancelPendingSearch();
        IsBusy = true;
        Errors = [];
        Warnings = [];
        ArchivedEntityMatch = null;
        OperationMessage = "Creating entity…";

        ManualEntityCreationResult result;
        try
        {
            result = await _service.CreateAsync(
                new ManualEntityCreationRequest(
                    EntityName,
                    SelectedDependencies.Select(static row => row.Selection)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Errors = ["Entity creation was cancelled; no partial changes were saved."];
            OperationMessage = null;
            IsBusy = false;
            return;
        }
        catch (Exception exception)
        {
            Errors = [$"Entity could not be created: {exception.Message}"];
            OperationMessage = null;
            IsBusy = false;
            return;
        }

        Errors = result.Diagnostics
            .Where(static diagnostic =>
                diagnostic.Severity == ManualEntityCreationDiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Message)
            .ToArray();
        Warnings = result.Diagnostics
            .Where(static diagnostic =>
                diagnostic.Severity == ManualEntityCreationDiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.Message)
            .ToArray();
        ArchivedEntityMatch = result.ArchivedEntityMatch;

        if (!result.IsSuccess)
        {
            OperationMessage = null;
            IsBusy = false;
            return;
        }

        ResetCore();
        IsBusy = false;
        try
        {
            await _onCreated();
        }
        catch (Exception exception)
        {
            Errors =
            [
                $"The entity was created, but the overview could not be refreshed: {exception.Message}"
            ];
        }
    }

    public void Reset()
    {
        if (!IsBusy)
        {
            ResetCore();
        }
    }

    private async Task RestoreArchivedAsync()
    {
        ArchivedEntityMatch? match = ArchivedEntityMatch;
        if (match is null || IsBusy || !_canOperate())
        {
            return;
        }

        IsBusy = true;
        OperationMessage = "Opening archived entity…";
        try
        {
            IsBusy = false;
            await _onRestoreArchived(match.EntityId);
            OperationMessage = null;
        }
        catch (Exception exception)
        {
            Errors = [$"The archived entity could not be opened: {exception.Message}"];
            OperationMessage = null;
        }
        finally
        {
            if (IsBusy)
            {
                IsBusy = false;
            }
        }
    }

    public void NotifyHostCanExecuteChanged()
    {
        if (!_canOperate())
        {
            CancelPendingSearch();
        }

        _createCommand.NotifyCanExecuteChanged();
        _addUnresolvedCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
        _addExistingCommand.NotifyCanExecuteChanged();
        _removeDependencyCommand.NotifyCanExecuteChanged();
        _restoreArchivedCommand.NotifyCanExecuteChanged();
    }

    private void AddExisting(ManualDependencySuggestion suggestion)
    {
        ManualDependencySelection selection = ManualDependencySelection.Existing(
            suggestion.EntityId,
            suggestion.SourceName);
        AddDependency(selection, false);
    }

    private void AddUnresolved()
    {
        string sourceName = DependencyQuery.Trim();
        if (!CanAddAsUnresolved || sourceName.Length == 0)
        {
            return;
        }

        AddDependency(ManualDependencySelection.Unresolved(sourceName), true);
    }

    private void AddDependency(ManualDependencySelection selection, bool isUnresolved)
    {
        if (SelectedDependencies.Any(row => row.Selection.SourceKey == selection.SourceKey))
        {
            SearchMessage = $"'{selection.SourceName}' has already been added.";
            return;
        }

        SelectedDependencies.Add(new ManualDependencyRow(
            selection,
            selection.SourceName,
            isUnresolved ? "⚠ Missing" : "Resolved",
            isUnresolved));
        ClearDependencySearch();
        UpdateDraftWarnings();
    }

    private void RemoveDependency(ManualDependencyRow row)
    {
        SelectedDependencies.Remove(row);
        UpdateDraftWarnings();
        ScheduleSearch();
    }

    private void Cancel()
    {
        ResetCore();
        _onCancelled();
    }

    private void ScheduleSearch()
    {
        CancelPendingSearch();
        CancellationTokenSource cancellation = new();
        _searchCancellation = cancellation;
        int version = ++_searchVersion;
        _ = SearchAfterDelayAsync(
            cancellation,
            version,
            DependencyQuery,
            EntityName);
    }

    private async Task SearchAfterDelayAsync(
        CancellationTokenSource cancellation,
        int version,
        string dependencyQuery,
        string entityName)
    {
        try
        {
            await Task.Delay(SearchDelay, cancellation.Token);
            await SearchDependenciesCoreAsync(
                dependencyQuery,
                entityName,
                version,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingSearch()
    {
        _searchVersion++;
        _searchCancellation?.Cancel();
        _searchCancellation = null;
    }

    private void ClearDependencySearch()
    {
        CancelPendingSearch();
        _dependencyQuery = string.Empty;
        OnPropertyChanged(nameof(DependencyQuery));
        Suggestions = [];
        SearchMessage = null;
        CanAddAsUnresolved = false;
    }

    private void UpdateDraftWarnings()
    {
        Warnings = SelectedDependencies
            .Where(static row => row.IsUnresolved)
            .Select(static row =>
                $"'{row.SourceName}' does not exist and will be added as unresolved.")
            .ToArray();
    }

    private bool ContainsDependency(EntitySourceKey sourceKey) =>
        SelectedDependencies.Any(row => row.Selection.SourceKey == sourceKey);

    private void ResetCore()
    {
        CancelPendingSearch();
        _entityName = string.Empty;
        OnPropertyChanged(nameof(EntityName));
        _dependencyQuery = string.Empty;
        OnPropertyChanged(nameof(DependencyQuery));
        SelectedDependencies.Clear();
        Suggestions = [];
        Errors = [];
        Warnings = [];
        ArchivedEntityMatch = null;
        SearchMessage = null;
        OperationMessage = null;
        CanAddAsUnresolved = false;
        _createCommand.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
