using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class EntityDependencyEditorViewModel : INotifyPropertyChanged
{
    private readonly EntityDependencyEditorService _editorService;
    private readonly EntityArchivalService _archivalService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly Func<Task> _onPersisted;
    private readonly Func<Task> _onArchived;
    private readonly Action<SchemaSynchronizationPlan> _onReviewStaged;
    private readonly Func<bool> _canOperate;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _confirmArchiveCommand;
    private readonly RelayCommand<ManualDependencySuggestion> _addExistingCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _suppressCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _removeManualCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _restoreCommand;
    private readonly RelayCommand _addUnresolvedCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _requestArchiveCommand;
    private readonly RelayCommand _cancelArchiveCommand;
    private IReadOnlyList<EntityDependencyEditRow> _dependencies = [];
    private IReadOnlyList<ManualDependencySuggestion> _suggestions = [];
    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _errors = [];
    private string _dependencyQuery = string.Empty;
    private string? _searchMessage;
    private string? _archiveErrorMessage;
    private bool _canAddAsUnresolved;
    private bool _isBusy;
    private bool _isOpen;
    private bool _isReviewMode;
    private bool _isArchiveConfirmationOpen;
    private int _searchVersion;
    private EntityDependencyEditPlan? _currentEditPlan;
    private SchemaSynchronizationPlan? _reviewPlan;

    public EntityDependencyEditorViewModel(
        EntityDependencyEditorService editorService,
        EntityArchivalService archivalService,
        SchemaSynchronizationService synchronizationService,
        Func<Task> onPersisted,
        Func<Task> onArchived,
        Action<SchemaSynchronizationPlan> onReviewStaged,
        Func<bool>? canOperate = null)
    {
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(archivalService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(onPersisted);
        ArgumentNullException.ThrowIfNull(onArchived);
        ArgumentNullException.ThrowIfNull(onReviewStaged);

        _editorService = editorService;
        _archivalService = archivalService;
        _synchronizationService = synchronizationService;
        _onPersisted = onPersisted;
        _onArchived = onArchived;
        _onReviewStaged = onReviewStaged;
        _canOperate = canOperate ?? (() => true);
        _addExistingCommand = new RelayCommand<ManualDependencySuggestion>(
            suggestion => _ = AddManualDependencyAsync(suggestion.SourceName),
            _ => CanEdit);
        _suppressCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = SetOverrideAsync(
                row.SourceName,
                ManualDependencyOverrideAction.Suppress),
            row => CanEdit && row.CanSuppress);
        _removeManualCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = RemoveOverrideAsync(row.SourceName),
            row => CanEdit && row.CanRemoveManual);
        _restoreCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = RemoveOverrideAsync(row.SourceName),
            row => CanEdit && row.CanRestore);
        _addUnresolvedCommand = new RelayCommand(
            () => _ = AddManualDependencyAsync(DependencyQuery.Trim()),
            () => CanEdit && CanAddAsUnresolved);
        _saveCommand = new AsyncCommand(
            SaveAsync,
            () => CanEdit && CurrentEditPlan?.IsValid == true);
        _cancelCommand = new RelayCommand(
            CancelOrClose,
            () => IsOpen && !IsBusy && _canOperate());
        _requestArchiveCommand = new RelayCommand(
            RequestArchive,
            () => CanArchive);
        _confirmArchiveCommand = new AsyncCommand(
            ConfirmArchiveAsync,
            () => IsArchiveConfirmationOpen && !IsBusy && _canOperate());
        _cancelArchiveCommand = new RelayCommand(
            CancelArchive,
            () => IsArchiveConfirmationOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<EntityDependencyEditRow> Dependencies
    {
        get => _dependencies;
        private set
        {
            if (SetField(ref _dependencies, value))
            {
                OnPropertyChanged(nameof(HasDependencies));
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

    public string DependencyQuery
    {
        get => _dependencyQuery;
        set
        {
            if (SetField(ref _dependencyQuery, value ?? string.Empty))
            {
                _ = SearchAsync(++_searchVersion);
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

    public string? ArchiveErrorMessage
    {
        get => _archiveErrorMessage;
        private set
        {
            if (SetField(ref _archiveErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasArchiveError));
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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyCommandsChanged();
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetField(ref _isOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsReviewMode
    {
        get => _isReviewMode;
        private set
        {
            if (SetField(ref _isReviewMode, value))
            {
                OnPropertyChanged(nameof(ContextTitle));
                OnPropertyChanged(nameof(ContextDescription));
                OnPropertyChanged(nameof(SaveLabel));
                OnPropertyChanged(nameof(CanArchive));
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsArchiveConfirmationOpen
    {
        get => _isArchiveConfirmationOpen;
        private set
        {
            if (SetField(ref _isArchiveConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                NotifyCommandsChanged();
            }
        }
    }

    public EntityDependencyEditPlan? CurrentEditPlan
    {
        get => _currentEditPlan;
        private set
        {
            if (SetField(ref _currentEditPlan, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntity));
                OnPropertyChanged(nameof(SelectedEntityName));
                OnPropertyChanged(nameof(EntityDetails));
                OnPropertyChanged(nameof(EntityNotes));
                OnPropertyChanged(nameof(ArchiveConfirmationMessage));
                OnPropertyChanged(nameof(CanArchive));
                NotifyCommandsChanged();
            }
        }
    }

    public bool CanEdit =>
        IsOpen && !IsBusy && !IsArchiveConfirmationOpen &&
        _canOperate() && CurrentEditPlan is not null;

    public bool CanArchive => CanEdit && !IsReviewMode;

    public bool HasSelectedEntity => CurrentEditPlan is not null;

    public bool HasDependencies => Dependencies.Count > 0;

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasSearchMessage => !string.IsNullOrWhiteSpace(SearchMessage);

    public bool HasArchiveError => !string.IsNullOrWhiteSpace(ArchiveErrorMessage);

    public string SelectedEntityName => CurrentEditPlan?.Entity.SourceName ?? "Loading entity…";

    public string EntityDetails => CurrentEditPlan is null
        ? "Loading entity details…"
        : $"Origin: {FormatProvenance(CurrentEditPlan.Entity.Provenance)}  •  " +
          $"Status: {FormatStatus(CurrentEditPlan.Entity.Status)}";

    public string EntityNotes => CurrentEditPlan?.Entity.Notes.Length > 0
        ? CurrentEditPlan.Entity.Notes
        : "No notes.";

    public string ContextTitle => IsReviewMode
        ? "Correct dependencies before applying the import"
        : "Edit entity";

    public string ContextDescription => IsReviewMode
        ? "Changes are staged with this synchronization and are saved only when the review is applied."
        : "Imported facts remain visible. Manual additions and suppressions survive future imports.";

    public string SaveLabel => IsReviewMode ? "Stage Changes" : "Save Changes";

    public string ArchiveConfirmationMessage => CurrentEditPlan is null
        ? string.Empty
        : $"Archive '{CurrentEditPlan.Entity.SourceName}'? It will disappear from active views and dependency searches. " +
          "Identity, progress, notes, imported relationships, and manual overrides will be preserved. " +
          "Entities that depend on it may become unresolved. Archived entities are not currently available " +
          "in the app, so this action cannot be undone here. Unsaved dependency edits will be discarded.";

    public ICommand AddExistingCommand => _addExistingCommand;

    public ICommand AddUnresolvedCommand => _addUnresolvedCommand;

    public ICommand SuppressCommand => _suppressCommand;

    public ICommand RemoveManualCommand => _removeManualCommand;

    public ICommand RestoreCommand => _restoreCommand;

    public ICommand SaveCommand => _saveCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand RequestArchiveCommand => _requestArchiveCommand;

    public ICommand ConfirmArchiveCommand => _confirmArchiveCommand;

    public ICommand CancelArchiveCommand => _cancelArchiveCommand;

    public async Task BeginStandaloneAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return;
        }

        ResetSessionState();
        IsReviewMode = false;
        IsOpen = true;
        IsBusy = true;
        try
        {
            LoadPlan(await _editorService.LoadAsync(entityId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Errors = ["Loading entity dependencies was cancelled."];
        }
        catch (Exception exception)
        {
            Errors = [$"Entity dependencies could not be loaded: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task BeginReviewAsync(
        SchemaSynchronizationPlan plan,
        EntityId ownerId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownerId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return Task.CompletedTask;
        }

        ResetSessionState();
        _reviewPlan = plan;
        IsReviewMode = true;
        IsOpen = true;
        try
        {
            ManualDependencyOverride[] desired = plan.CandidateManualOverrides
                .Where(item => item.DependentEntityId == ownerId)
                .ToArray();
            LoadPlan(_synchronizationService.PreviewDependencyEdit(
                plan,
                ownerId,
                desired));
        }
        catch (Exception exception)
        {
            Errors = [$"Entity dependencies could not be loaded: {exception.Message}"];
        }

        return Task.CompletedTask;
    }

    public void NotifyHostCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanArchive));
        NotifyCommandsChanged();
    }

    private async Task SearchAsync(int searchVersion)
    {
        if (!CanEdit || CurrentEditPlan is null)
        {
            ClearSearch();
            return;
        }

        try
        {
            ManualDependencySearchResult result = IsReviewMode
                ? _editorService.SearchDependencies(
                    CurrentEditPlan.Entity.Id,
                    DependencyQuery,
                    _reviewPlan!.CandidateEntities)
                : await _editorService.SearchDependenciesAsync(
                    CurrentEditPlan.Entity.Id,
                    DependencyQuery);
            if (searchVersion != _searchVersion)
            {
                return;
            }

            Suggestions = result.Suggestions;
            CanAddAsUnresolved = result.CanAddAsUnresolved &&
                                 !ContainsDependency(result.EnteredKey);
            SearchMessage = result.BlockingMessage ??
                            (CanAddAsUnresolved
                                ? $"No active entity exactly matches '{result.EnteredName}'. Add it deliberately as unresolved."
                                : null);
        }
        catch (Exception exception)
        {
            if (searchVersion != _searchVersion)
            {
                return;
            }

            Suggestions = [];
            CanAddAsUnresolved = false;
            SearchMessage = $"Dependencies could not be searched: {exception.Message}";
        }
    }

    private Task AddManualDependencyAsync(string sourceName)
    {
        if (sourceName.Length == 0)
        {
            return Task.CompletedTask;
        }

        return SetOverrideAsync(sourceName, ManualDependencyOverrideAction.Add);
    }

    private Task SetOverrideAsync(
        string sourceName,
        ManualDependencyOverrideAction action)
    {
        if (CurrentEditPlan is null)
        {
            return Task.CompletedTask;
        }

        EntitySourceKey key = EntitySourceKey.From(sourceName);
        ManualDependencyOverride[] desired = CurrentEditPlan.DesiredOverrides
            .Where(item => EntitySourceKey.From(item.DependencySourceName) != key)
            .Append(new ManualDependencyOverride(CurrentEditPlan.Entity.Id, sourceName, action))
            .ToArray();
        return PreviewAsync(desired);
    }

    private Task RemoveOverrideAsync(string sourceName)
    {
        if (CurrentEditPlan is null)
        {
            return Task.CompletedTask;
        }

        EntitySourceKey key = EntitySourceKey.From(sourceName);
        ManualDependencyOverride[] desired = CurrentEditPlan.DesiredOverrides
            .Where(item => EntitySourceKey.From(item.DependencySourceName) != key)
            .ToArray();
        return PreviewAsync(desired);
    }

    private async Task PreviewAsync(IReadOnlyList<ManualDependencyOverride> desired)
    {
        if (CurrentEditPlan is null)
        {
            return;
        }

        try
        {
            EntityDependencyEditPlan plan = IsReviewMode
                ? _synchronizationService.PreviewDependencyEdit(
                    _reviewPlan!,
                    CurrentEditPlan.Entity.Id,
                    desired)
                : await _editorService.PreviewAsync(CurrentEditPlan.Entity.Id, desired);
            LoadPlan(plan);
            ClearSearch();
        }
        catch (Exception exception)
        {
            Errors = [$"Dependency changes could not be evaluated: {exception.Message}"];
        }
    }

    private async Task SaveAsync()
    {
        if (CurrentEditPlan?.IsValid != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsReviewMode)
            {
                SchemaSynchronizationPlan revised =
                    _synchronizationService.StageDependencyEdit(_reviewPlan!, CurrentEditPlan);
                _onReviewStaged(revised);
            }
            else
            {
                await _editorService.SaveAsync(CurrentEditPlan);
                await _onPersisted();
            }

            CloseSession();
        }
        catch (Exception exception)
        {
            Errors = [$"Dependency changes could not be saved: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestArchive()
    {
        ArchiveErrorMessage = null;
        IsArchiveConfirmationOpen = true;
    }

    private void CancelArchive()
    {
        ArchiveErrorMessage = null;
        IsArchiveConfirmationOpen = false;
    }

    private void CancelOrClose()
    {
        if (IsArchiveConfirmationOpen)
        {
            CancelArchive();
            return;
        }

        CloseSession();
    }

    private async Task ConfirmArchiveAsync()
    {
        if (CurrentEditPlan is null || IsReviewMode)
        {
            return;
        }

        IsBusy = true;
        ArchiveErrorMessage = null;
        try
        {
            bool archived = await _archivalService.TryArchiveAsync(CurrentEditPlan.Entity.Id);
            if (!archived)
            {
                ArchiveErrorMessage =
                    "This entity no longer exists as an active entity. Close the editor and refresh before trying again.";
                return;
            }

            await _onArchived();
            CloseSession();
        }
        catch (Exception exception)
        {
            ArchiveErrorMessage = $"The entity could not be archived: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadPlan(EntityDependencyEditPlan plan)
    {
        CurrentEditPlan = plan;
        Dependencies = plan.Dependencies.Select(static item => new EntityDependencyEditRow(
            item.DependencySourceName,
            FormatOrigin(item),
            FormatResolution(item),
            item.Origin)).ToArray();
        Warnings = plan.Warnings;
        Errors = plan.Errors;
        NotifyCommandsChanged();
    }

    private void CloseSession()
    {
        IsArchiveConfirmationOpen = false;
        IsOpen = false;
        ResetSessionState();
    }

    private void ResetSessionState()
    {
        CurrentEditPlan = null;
        Dependencies = [];
        Warnings = [];
        Errors = [];
        ArchiveErrorMessage = null;
        _reviewPlan = null;
        IsReviewMode = false;
        ClearSearch();
    }

    private void ClearSearch()
    {
        _searchVersion++;
        _dependencyQuery = string.Empty;
        OnPropertyChanged(nameof(DependencyQuery));
        Suggestions = [];
        CanAddAsUnresolved = false;
        SearchMessage = null;
    }

    private bool ContainsDependency(EntitySourceKey? key) =>
        key is not null && CurrentEditPlan?.Dependencies.Any(item =>
            item.DependencySourceKey == key &&
            item.Origin is not DependencyEditOrigin.SuppressedImported and
                not DependencyEditOrigin.DormantSuppression) == true;

    private void NotifyCommandsChanged()
    {
        _saveCommand.NotifyCanExecuteChanged();
        _addExistingCommand.NotifyCanExecuteChanged();
        _addUnresolvedCommand.NotifyCanExecuteChanged();
        _suppressCommand.NotifyCanExecuteChanged();
        _removeManualCommand.NotifyCanExecuteChanged();
        _restoreCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
        _requestArchiveCommand.NotifyCanExecuteChanged();
        _confirmArchiveCommand.NotifyCanExecuteChanged();
        _cancelArchiveCommand.NotifyCanExecuteChanged();
    }

    private static string FormatOrigin(EntityDependencyEditItem item) => item.Origin switch
    {
        DependencyEditOrigin.Imported => $"Imported ({item.ImportedKind})",
        DependencyEditOrigin.Manual => "Manual addition",
        DependencyEditOrigin.ImportedAndManual =>
            $"Manual addition + imported ({item.ImportedKind})",
        DependencyEditOrigin.SuppressedImported =>
            $"Suppressed imported ({item.ImportedKind})",
        DependencyEditOrigin.DormantSuppression => "Suppression retained; absent from current CSV",
        _ => throw new ArgumentOutOfRangeException(nameof(item))
    };

    private static string FormatResolution(EntityDependencyEditItem item)
    {
        if (item.Origin is DependencyEditOrigin.SuppressedImported or
            DependencyEditOrigin.DormantSuppression)
        {
            return "Not in effective graph";
        }

        return item.IsResolved ? "Resolved" : "⚠ Unresolved";
    }

    private static string FormatProvenance(EntityProvenance provenance) => provenance switch
    {
        EntityProvenance.Imported => "CSV",
        EntityProvenance.ManualOnly => "Manual only",
        EntityProvenance.ManualAndImported => "Manual + CSV",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, null)
    };

    private static string FormatStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.Completed => "Completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

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
