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
    private readonly EntityLifecycleService _lifecycleService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly Func<Task> _onPersisted;
    private readonly Func<Task> _onArchived;
    private readonly Func<Task> _onRestored;
    private readonly Action<SchemaSynchronizationPlan> _onReviewStaged;
    private readonly Func<bool> _canOperate;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _confirmArchiveCommand;
    private readonly AsyncCommand _restoreEntityCommand;
    private readonly RelayCommand<ManualDependencySuggestion> _addExistingCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _suppressCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _removeManualCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _restoreDependencyCommand;
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
    private EntityEditorMode _mode;
    private bool _isArchiveConfirmationOpen;
    private int _searchVersion;
    private EntityDependencyEditPlan? _currentEditPlan;
    private ArchivedEntityDetails? _archivedDetails;
    private SchemaSynchronizationPlan? _reviewPlan;
    private DevelopmentStatus _selectedStatus;
    private string _editedNotes = string.Empty;

    public EntityDependencyEditorViewModel(
        EntityDependencyEditorService editorService,
        EntityLifecycleService lifecycleService,
        SchemaSynchronizationService synchronizationService,
        Func<Task> onPersisted,
        Func<Task> onArchived,
        Func<Task> onRestored,
        Action<SchemaSynchronizationPlan> onReviewStaged,
        Func<bool>? canOperate = null)
    {
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(onPersisted);
        ArgumentNullException.ThrowIfNull(onArchived);
        ArgumentNullException.ThrowIfNull(onRestored);
        ArgumentNullException.ThrowIfNull(onReviewStaged);

        _editorService = editorService;
        _lifecycleService = lifecycleService;
        _synchronizationService = synchronizationService;
        _onPersisted = onPersisted;
        _onArchived = onArchived;
        _onRestored = onRestored;
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
        _restoreDependencyCommand = new RelayCommand<EntityDependencyEditRow>(
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
        _restoreEntityCommand = new AsyncCommand(
            RestoreEntityAsync,
            () => CanRestoreEntity);
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
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanRestoreEntity));
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
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanRestoreEntity));
                NotifyCommandsChanged();
            }
        }
    }

    public EntityEditorMode Mode
    {
        get => _mode;
        private set
        {
            if (SetField(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsReviewMode));
                OnPropertyChanged(nameof(IsArchivedMode));
                OnPropertyChanged(nameof(ContextTitle));
                OnPropertyChanged(nameof(ContextDescription));
                OnPropertyChanged(nameof(SaveLabel));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanRestoreEntity));
                OnPropertyChanged(nameof(ShowSave));
                OnPropertyChanged(nameof(ShowDependencyEditor));
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsReviewMode => Mode == EntityEditorMode.SynchronizationReview;

    public bool IsArchivedMode => Mode == EntityEditorMode.ArchivedDetails;

    public bool IsArchiveConfirmationOpen
    {
        get => _isArchiveConfirmationOpen;
        private set
        {
            if (SetField(ref _isArchiveConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
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
                OnPropertyChanged(nameof(ArchiveConfirmationMessage));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
                NotifyCommandsChanged();
            }
        }
    }

    public ArchivedEntityDetails? ArchivedDetails
    {
        get => _archivedDetails;
        private set
        {
            if (SetField(ref _archivedDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntity));
                OnPropertyChanged(nameof(SelectedEntityName));
                OnPropertyChanged(nameof(EntityDetails));
                OnPropertyChanged(nameof(CanRestoreEntity));
                NotifyCommandsChanged();
            }
        }
    }

    public DevelopmentStatus SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (CanEditProgress)
            {
                SetField(ref _selectedStatus, value);
            }
        }
    }

    public string EditedNotes
    {
        get => _editedNotes;
        set
        {
            if (CanEditProgress)
            {
                SetField(ref _editedNotes, value ?? string.Empty);
            }
        }
    }

    public IReadOnlyList<DevelopmentStatusOption> StatusOptions { get; } =
    [
        new(DevelopmentStatus.NotStarted, "Not started"),
        new(DevelopmentStatus.InProgress, "In progress"),
        new(DevelopmentStatus.DevelopmentCompleted, "Dev. completed"),
        new(DevelopmentStatus.Reconciled, "Reconciled")
    ];

    public bool CanEdit =>
        IsOpen && !IsBusy && !IsArchiveConfirmationOpen &&
        _canOperate() && CurrentEditPlan is not null && !IsArchivedMode;

    public bool CanEditProgress => CanEdit && Mode == EntityEditorMode.Standalone;

    public bool CanArchive => CanEditProgress;

    public bool CanRestoreEntity =>
        IsOpen && !IsBusy && _canOperate() && IsArchivedMode && ArchivedDetails is not null;

    public bool ShowSave => !IsArchivedMode;

    public bool ShowDependencyEditor => !IsArchivedMode;

    public bool HasSelectedEntity => SelectedEntity is not null;

    public bool HasDependencies => Dependencies.Count > 0;

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasSearchMessage => !string.IsNullOrWhiteSpace(SearchMessage);

    public bool HasArchiveError => !string.IsNullOrWhiteSpace(ArchiveErrorMessage);

    public string SelectedEntityName => SelectedEntity?.SourceName ?? "Loading entity…";

    public string EntityDetails => SelectedEntity is null
        ? "Loading entity details…"
        : $"Origin: {FormatProvenance(SelectedEntity.Provenance)}";

    public string ContextTitle => Mode switch
    {
        EntityEditorMode.SynchronizationReview =>
            "Correct dependencies before applying the import",
        EntityEditorMode.ArchivedDetails => "Archived entity details",
        _ => "Edit entity"
    };

    public string ContextDescription => Mode switch
    {
        EntityEditorMode.SynchronizationReview =>
            "Changes are staged with this synchronization and are saved only when the review is applied.",
        EntityEditorMode.ArchivedDetails =>
            "Archived entities are read-only until explicitly restored.",
        _ => "Update progress and notes. Imported facts remain visible, while manual dependency corrections survive future imports."
    };

    public string SaveLabel => IsReviewMode ? "Stage Changes" : "Save Changes";

    public string ArchiveConfirmationMessage => CurrentEditPlan is null
        ? string.Empty
        : $"Archive '{CurrentEditPlan.Entity.SourceName}'? It will disappear from active views and dependency searches. " +
          "Identity, progress, notes, imported relationships, and manual overrides will be preserved. " +
          "Entities that depend on it may become unresolved. You can restore it later from the Archived view. " +
          "Unsaved edits will be discarded.";

    public ICommand AddExistingCommand => _addExistingCommand;

    public ICommand AddUnresolvedCommand => _addUnresolvedCommand;

    public ICommand SuppressCommand => _suppressCommand;

    public ICommand RemoveManualCommand => _removeManualCommand;

    public ICommand RestoreCommand => _restoreDependencyCommand;

    public ICommand RestoreEntityCommand => _restoreEntityCommand;

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
        Mode = EntityEditorMode.Standalone;
        IsOpen = true;
        IsBusy = true;
        try
        {
            LoadPlan(await _editorService.LoadAsync(entityId, cancellationToken), true);
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
        Mode = EntityEditorMode.SynchronizationReview;
        IsOpen = true;
        try
        {
            ManualDependencyOverride[] desired = plan.CandidateManualOverrides
                .Where(item => item.DependentEntityId == ownerId)
                .ToArray();
            LoadPlan(
                _synchronizationService.PreviewDependencyEdit(plan, ownerId, desired),
                true);
        }
        catch (Exception exception)
        {
            Errors = [$"Entity dependencies could not be loaded: {exception.Message}"];
        }

        return Task.CompletedTask;
    }

    public async Task BeginArchivedAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return;
        }

        ResetSessionState();
        Mode = EntityEditorMode.ArchivedDetails;
        IsOpen = true;
        IsBusy = true;
        try
        {
            ArchivedEntityDetails details =
                await _editorService.LoadArchivedDetailsAsync(entityId, cancellationToken);
            ArchivedDetails = details;
            _selectedStatus = details.Entity.Status;
            OnPropertyChanged(nameof(SelectedStatus));
            _editedNotes = details.Entity.Notes;
            OnPropertyChanged(nameof(EditedNotes));
            Dependencies = details.Dependencies.Select(static item =>
                new EntityDependencyEditRow(
                    item.DependencySourceName,
                    FormatOrigin(item),
                    FormatResolution(item),
                    item.Origin)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Errors = ["Loading archived entity details was cancelled."];
        }
        catch (Exception exception)
        {
            Errors = [$"Archived entity details could not be loaded: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NotifyHostCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanArchive));
        OnPropertyChanged(nameof(CanEditProgress));
        OnPropertyChanged(nameof(CanRestoreEntity));
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
            LoadPlan(plan, false);
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
                await _editorService.SaveAsync(
                    CurrentEditPlan,
                    SelectedStatus,
                    EditedNotes);
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
            bool archived = await _lifecycleService.TryArchiveAsync(CurrentEditPlan.Entity.Id);
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

    private async Task RestoreEntityAsync()
    {
        if (ArchivedDetails is null || !IsArchivedMode)
        {
            return;
        }

        IsBusy = true;
        Errors = [];
        try
        {
            EntityRestorationResult result =
                await _lifecycleService.RestoreAsync(ArchivedDetails.Entity.Id);
            if (!result.IsSuccess)
            {
                Errors = result.Errors;
                return;
            }

            await _onRestored();
            CloseSession();
        }
        catch (Exception exception)
        {
            Errors = [$"The entity could not be restored: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadPlan(EntityDependencyEditPlan plan, bool initializeProgress)
    {
        CurrentEditPlan = plan;
        if (initializeProgress)
        {
            _selectedStatus = plan.Entity.Status;
            OnPropertyChanged(nameof(SelectedStatus));
            _editedNotes = plan.Entity.Notes;
            OnPropertyChanged(nameof(EditedNotes));
        }

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
        ArchivedDetails = null;
        Dependencies = [];
        Warnings = [];
        Errors = [];
        ArchiveErrorMessage = null;
        _reviewPlan = null;
        Mode = EntityEditorMode.Standalone;
        _selectedStatus = DevelopmentStatus.NotStarted;
        OnPropertyChanged(nameof(SelectedStatus));
        _editedNotes = string.Empty;
        OnPropertyChanged(nameof(EditedNotes));
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
        _restoreDependencyCommand.NotifyCanExecuteChanged();
        _restoreEntityCommand.NotifyCanExecuteChanged();
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

    private TrackedEntity? SelectedEntity => CurrentEditPlan?.Entity ?? ArchivedDetails?.Entity;

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
