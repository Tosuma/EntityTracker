using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan OverviewSearchDelay = TimeSpan.FromMilliseconds(200);

    private readonly EntityOverviewService _overviewService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly ICsvFilePicker _filePicker;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _importCsvCommand;
    private readonly AsyncCommand _applySynchronizationCommand;
    private readonly AsyncCommand _cancelSynchronizationCommand;
    private readonly AsyncCommand<EntityOverviewRow> _editOverviewEntityCommand;
    private readonly AsyncCommand<SchemaSynchronizationReviewRow> _editReviewEntityCommand;
    private readonly RelayCommand _openOverviewSearchCommand;
    private readonly RelayCommand _clearOverviewSearchCommand;
    private readonly RelayCommand _closeOverviewSearchCommand;
    private IReadOnlyList<EntityOverviewRow> _allOverviewItems = [];
    private IReadOnlyList<EntityOverviewRow> _archivedOverviewItems = [];
    private IReadOnlyList<EntityOverviewRow> _currentViewItems = [];
    private IReadOnlyList<EntityOverviewRow> _overviewItems = [];
    private string? _overviewErrorMessage;
    private string _overviewSearchQuery = string.Empty;
    private string _busyMessage = string.Empty;
    private bool _isBusy;
    private bool _isOverviewSearchOpen;
    private bool _searchOverviewDependencies;
    private OverviewManagerFilter _selectedOverviewFilter;
    private int _selectedTabIndex;
    private int _notStartedCount;
    private int _inProgressCount;
    private int _developmentCompletedCount;
    private int _reconciledCount;
    private CancellationTokenSource? _overviewSearchCancellation;
    private int _overviewSearchVersion;

    public MainWindowViewModel(
        EntityOverviewService overviewService,
        SchemaSynchronizationService synchronizationService,
        ManualEntityCreationService manualEntityCreationService,
        EntityDependencyEditorService entityDependencyEditorService,
        EntityLifecycleService entityLifecycleService,
        ICsvFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(overviewService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(manualEntityCreationService);
        ArgumentNullException.ThrowIfNull(entityDependencyEditorService);
        ArgumentNullException.ThrowIfNull(entityLifecycleService);
        ArgumentNullException.ThrowIfNull(filePicker);
        _overviewService = overviewService;
        _synchronizationService = synchronizationService;
        _filePicker = filePicker;
        Review = new SchemaSynchronizationReviewViewModel();
        ManualCreation = new ManualEntityCreationViewModel(
            manualEntityCreationService,
            OnManualEntityCreatedAsync,
            OpenArchivedFromCreationAsync,
            () => SelectedTabIndex = 0,
            () => !IsBusy);
        ManualCreation.PropertyChanged += OnManualCreationPropertyChanged;
        Editor = new EntityDependencyEditorViewModel(
            entityDependencyEditorService,
            entityLifecycleService,
            synchronizationService,
            OnDependencyEditsPersistedAsync,
            OnEntityArchivedAsync,
            OnEntityRestoredAsync,
            OnReviewDependencyEditsStaged,
            () => !IsBusy && !ManualCreation.IsBusy);
        Editor.PropertyChanged += OnEditorPropertyChanged;
        _refreshCommand = new AsyncCommand(
            () => RefreshAsync(),
            () => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen);
        _importCsvCommand = new AsyncCommand(
            () => ImportCsvAsync(),
            () => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen);
        _applySynchronizationCommand = new AsyncCommand(
            () => ApplySynchronizationAsync(),
            () => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen && Review.CanApply);
        _cancelSynchronizationCommand = new AsyncCommand(
            () => CancelSynchronizationAsync(),
            () => !IsBusy &&
                  !ManualCreation.IsBusy &&
                  !Editor.IsOpen &&
                  (Review.HasReview || Review.HasDiagnostics));
        _editOverviewEntityCommand = new AsyncCommand<EntityOverviewRow>(
            OpenOverviewEntityAsync,
            _ => !IsBusy &&
                 !ManualCreation.IsBusy &&
                 !Editor.IsOpen &&
                 !Review.HasReview);
        _editReviewEntityCommand = new AsyncCommand<SchemaSynchronizationReviewRow>(
            row => EditReviewEntityAsync(row),
            _ => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen && Review.HasReview);
        _openOverviewSearchCommand = new RelayCommand(
            OpenOverviewSearch,
            () => SelectedTabIndex == 0 &&
                  !IsBusy &&
                  !ManualCreation.IsBusy &&
                  !Editor.IsOpen);
        _clearOverviewSearchCommand = new RelayCommand(
            ClearOverviewSearch,
            () => IsOverviewSearchOpen && OverviewSearchQuery.Length > 0);
        _closeOverviewSearchCommand = new RelayCommand(
            CloseOverviewSearch,
            () => IsOverviewSearchOpen);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SchemaSynchronizationReviewViewModel Review { get; }

    public ManualEntityCreationViewModel ManualCreation { get; }

    public EntityDependencyEditorViewModel Editor { get; }

    public IReadOnlyList<OverviewManagerFilterOption> OverviewFilters { get; } =
    [
        new(OverviewManagerFilter.AllActive, "All active"),
        new(OverviewManagerFilter.Ready, "Ready"),
        new(OverviewManagerFilter.Blocked, "Blocked"),
        new(OverviewManagerFilter.InProgress, "In progress"),
        new(OverviewManagerFilter.DevelopmentCompleted, "Dev. completed"),
        new(OverviewManagerFilter.Reconciled, "Reconciled"),
        new(OverviewManagerFilter.Archived, "Archived")
    ];

    public OverviewManagerFilter SelectedOverviewFilter
    {
        get => _selectedOverviewFilter;
        set
        {
            if (SetField(ref _selectedOverviewFilter, value))
            {
                if (!CanSearchOverviewDependencies && SearchOverviewDependencies)
                {
                    _searchOverviewDependencies = false;
                    OnPropertyChanged(nameof(SearchOverviewDependencies));
                }

                OnPropertyChanged(nameof(CanSearchOverviewDependencies));
                OnPropertyChanged(nameof(SelectedOverviewFilterName));
                OnPropertyChanged(nameof(OverviewEmptyTitle));
                OnPropertyChanged(nameof(OverviewEmptyDescription));
                CancelPendingOverviewSearch();
                ApplyOverviewFilterAndSearch();
            }
        }
    }

    public bool CanSearchOverviewDependencies =>
        SelectedOverviewFilter != OverviewManagerFilter.Archived;

    public string SelectedOverviewFilterName => OverviewFilters
        .Single(option => option.Value == SelectedOverviewFilter)
        .DisplayName;

    public string OverviewEmptyTitle => SelectedOverviewFilter switch
    {
        OverviewManagerFilter.AllActive => "No active entities",
        OverviewManagerFilter.Archived => "No archived entities",
        _ => $"No {SelectedOverviewFilterName.ToLowerInvariant()} entities"
    };

    public string OverviewEmptyDescription => SelectedOverviewFilter switch
    {
        OverviewManagerFilter.AllActive =>
            "Add an entity manually or open Schema Synchronization to import a CSV.",
        OverviewManagerFilter.Archived =>
            "Archived entities will appear here and can be inspected or restored.",
        _ => "Choose another manager view or update entity progress."
    };

    public IReadOnlyList<EntityOverviewRow> OverviewItems
    {
        get => _overviewItems;
        private set
        {
            if (SetField(ref _overviewItems, value))
            {
                OnPropertyChanged(nameof(OverviewSearchResultSummary));
                OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
            }
        }
    }

    public string OverviewSearchQuery
    {
        get => _overviewSearchQuery;
        set
        {
            if (SetField(ref _overviewSearchQuery, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasOverviewSearchQuery));
                _clearOverviewSearchCommand.NotifyCanExecuteChanged();
                ScheduleOverviewSearch();
            }
        }
    }

    public bool IsOverviewSearchOpen
    {
        get => _isOverviewSearchOpen;
        private set
        {
            if (SetField(ref _isOverviewSearchOpen, value))
            {
                OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
                _clearOverviewSearchCommand.NotifyCanExecuteChanged();
                _closeOverviewSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool SearchOverviewDependencies
    {
        get => _searchOverviewDependencies;
        set
        {
            if (CanSearchOverviewDependencies && SetField(ref _searchOverviewDependencies, value))
            {
                CancelPendingOverviewSearch();
                ApplyOverviewFilterAndSearch();
            }
        }
    }

    public string? OverviewErrorMessage
    {
        get => _overviewErrorMessage;
        private set
        {
            if (SetField(ref _overviewErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasOverviewError));
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
                OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyCommandsChanged();
                ManualCreation.NotifyHostCanExecuteChanged();
                Editor.NotifyHostCanExecuteChanged();
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
                OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetField(ref _selectedTabIndex, value))
            {
                _openOverviewSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int NotStartedCount
    {
        get => _notStartedCount;
        private set => SetField(ref _notStartedCount, value);
    }

    public int InProgressCount
    {
        get => _inProgressCount;
        private set => SetField(ref _inProgressCount, value);
    }

    public int DevelopmentCompletedCount
    {
        get => _developmentCompletedCount;
        private set
        {
            if (SetField(ref _developmentCompletedCount, value))
            {
                OnPropertyChanged(nameof(SuccessfulCompletionPercentage));
            }
        }
    }

    public int ReconciledCount
    {
        get => _reconciledCount;
        private set
        {
            if (SetField(ref _reconciledCount, value))
            {
                OnPropertyChanged(nameof(SuccessfulCompletionPercentage));
                OnPropertyChanged(nameof(ReconciledPercentage));
            }
        }
    }

    public int TotalEntityCount => _allOverviewItems.Count;

    public int ArchivedEntityCount => _archivedOverviewItems.Count;

    public double SuccessfulCompletionPercentage => TotalEntityCount == 0
        ? 0
        : (DevelopmentCompletedCount + ReconciledCount) * 100.0 / TotalEntityCount;

    public double ReconciledPercentage => TotalEntityCount == 0
        ? 0
        : ReconciledCount * 100.0 / TotalEntityCount;

    public bool HasOverviewItems => _currentViewItems.Count > 0;

    public bool HasOverviewSearchQuery => !string.IsNullOrWhiteSpace(OverviewSearchQuery);

    public string OverviewSearchResultSummary =>
        $"Showing {OverviewItems.Count} of {_currentViewItems.Count} in {SelectedOverviewFilterName}";

    public bool HasOverviewError => !string.IsNullOrWhiteSpace(OverviewErrorMessage);

    public bool ShowOverviewEmptyState => !IsBusy && !HasOverviewItems && !HasOverviewError;

    public bool ShowOverviewSearchEmptyState =>
        !IsBusy &&
        IsOverviewSearchOpen &&
        HasOverviewSearchQuery &&
        HasOverviewItems &&
        OverviewItems.Count == 0 &&
        !HasOverviewError;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand ImportCsvCommand => _importCsvCommand;

    public ICommand ApplySynchronizationCommand => _applySynchronizationCommand;

    public ICommand CancelSynchronizationCommand => _cancelSynchronizationCommand;

    public ICommand EditOverviewEntityCommand => _editOverviewEntityCommand;

    public ICommand EditReviewEntityCommand => _editReviewEntityCommand;

    public ICommand OpenOverviewSearchCommand => _openOverviewSearchCommand;

    public ICommand ClearOverviewSearchCommand => _clearOverviewSearchCommand;

    public ICommand CloseOverviewSearchCommand => _closeOverviewSearchCommand;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || ManualCreation.IsBusy || Editor.IsOpen)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Loading persisted entities…";
        try
        {
            await LoadOverviewAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOverviewFailure("Loading persisted entities was cancelled.");
        }
        catch (Exception exception)
        {
            SetOverviewFailure($"Persisted entities could not be loaded: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public async Task ImportCsvAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || ManualCreation.IsBusy || Editor.IsOpen)
        {
            return;
        }

        string? filePath;
        try
        {
            filePath = _filePicker.SelectCsvFile();
        }
        catch (Exception exception)
        {
            Review.SetFailure($"A CSV file could not be selected: {exception.Message}");
            SelectedTabIndex = 1;
            NotifyCommandsChanged();
            return;
        }

        if (filePath is null)
        {
            return;
        }

        Review.BeginImport(Path.GetFileName(filePath));
        SchemaImportMode mode = Review.Mode;
        SelectedTabIndex = 1;
        IsBusy = true;
        BusyMessage = $"Comparing {Review.SelectedFileName} with persisted state…";
        try
        {
            SchemaSynchronizationResult result = await _synchronizationService.PlanAsync(
                filePath,
                mode,
                cancellationToken);
            Review.Load(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Review.SetFailure("CSV synchronization review was cancelled.");
        }
        catch (Exception exception)
        {
            Review.SetFailure($"The CSV could not be compared: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public async Task ApplySynchronizationAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || ManualCreation.IsBusy || Editor.IsOpen || !Review.CanApply ||
            Review.CurrentPlan is null)
        {
            return;
        }

        SchemaSynchronizationPlan plan = Review.CurrentPlan;
        IsBusy = true;
        BusyMessage = "Applying schema synchronization…";
        try
        {
            await _synchronizationService.ApplyAsync(plan, cancellationToken);
            Review.Clear();
            SelectedTabIndex = 0;
            BusyMessage = "Recomputing dependency ranking…";
            await LoadOverviewAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Review.SetOperationFailure(
                "Applying synchronization was cancelled; no partial changes were saved.");
        }
        catch (Exception exception)
        {
            Review.SetOperationFailure($"Synchronization could not be applied: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public Task CancelSynchronizationAsync()
    {
        if (!IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen)
        {
            Review.Clear();
            NotifyCommandsChanged();
        }

        return Task.CompletedTask;
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        EntityOverviewResult result = await _overviewService.GetAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            SetOverviewFailure(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            return;
        }

        OverviewErrorMessage = null;
        EntityOverviewRow[] rows = result.Items.Select(CreateOverviewRow).ToArray();
        EntityOverviewRow[] archivedRows = result.ArchivedItems
            .Select(CreateOverviewRow)
            .ToArray();
        ReplaceOverviewItems(rows, archivedRows);
        UpdateProgressCounts(result.Items);
    }

    private void SetOverviewFailure(string message)
    {
        ReplaceOverviewItems([], []);
        OverviewErrorMessage = message;
        UpdateProgressCounts([]);
    }

    private void ReplaceOverviewItems(
        IReadOnlyList<EntityOverviewRow> items,
        IReadOnlyList<EntityOverviewRow> archivedItems)
    {
        _allOverviewItems = items;
        _archivedOverviewItems = archivedItems;
        ApplyOverviewFilterAndSearch();
        OnPropertyChanged(nameof(HasOverviewItems));
        OnPropertyChanged(nameof(ShowOverviewEmptyState));
        OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
        OnPropertyChanged(nameof(TotalEntityCount));
        OnPropertyChanged(nameof(ArchivedEntityCount));
        OnPropertyChanged(nameof(SuccessfulCompletionPercentage));
        OnPropertyChanged(nameof(ReconciledPercentage));
        OnPropertyChanged(nameof(OverviewSearchResultSummary));
    }

    private void OpenOverviewSearch()
    {
        IsOverviewSearchOpen = true;
    }

    private void ClearOverviewSearch()
    {
        CancelPendingOverviewSearch();
        if (_overviewSearchQuery.Length > 0)
        {
            _overviewSearchQuery = string.Empty;
            OnPropertyChanged(nameof(OverviewSearchQuery));
            OnPropertyChanged(nameof(HasOverviewSearchQuery));
        }

        ApplyOverviewFilterAndSearch();
        _clearOverviewSearchCommand.NotifyCanExecuteChanged();
    }

    private void CloseOverviewSearch()
    {
        ClearOverviewSearch();
        IsOverviewSearchOpen = false;
    }

    private void ScheduleOverviewSearch()
    {
        CancelPendingOverviewSearch();
        if (!HasOverviewSearchQuery)
        {
            ApplyOverviewFilterAndSearch();
            return;
        }

        CancellationTokenSource cancellation = new();
        _overviewSearchCancellation = cancellation;
        int version = ++_overviewSearchVersion;
        _ = ApplyOverviewSearchAfterDelayAsync(cancellation, version);
    }

    private async Task ApplyOverviewSearchAfterDelayAsync(
        CancellationTokenSource cancellation,
        int version)
    {
        try
        {
            await Task.Delay(OverviewSearchDelay, cancellation.Token);
            if (version == _overviewSearchVersion)
            {
                ApplyOverviewFilterAndSearch();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_overviewSearchCancellation, cancellation))
            {
                _overviewSearchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingOverviewSearch()
    {
        _overviewSearchVersion++;
        _overviewSearchCancellation?.Cancel();
        _overviewSearchCancellation = null;
    }

    private void ApplyOverviewFilterAndSearch()
    {
        _currentViewItems = SelectedOverviewFilter switch
        {
            OverviewManagerFilter.AllActive => _allOverviewItems,
            OverviewManagerFilter.Archived => _archivedOverviewItems,
            OverviewManagerFilter.Ready => FilterByWorkflow(EntityWorkflowState.Ready),
            OverviewManagerFilter.Blocked => FilterByWorkflow(EntityWorkflowState.Blocked),
            OverviewManagerFilter.InProgress => FilterByWorkflow(EntityWorkflowState.InProgress),
            OverviewManagerFilter.DevelopmentCompleted =>
                FilterByWorkflow(EntityWorkflowState.DevelopmentCompleted),
            OverviewManagerFilter.Reconciled =>
                FilterByWorkflow(EntityWorkflowState.Reconciled),
            _ => throw new ArgumentOutOfRangeException(
                nameof(SelectedOverviewFilter),
                SelectedOverviewFilter,
                null)
        };

        string query = OverviewSearchQuery.Trim();
        if (query.Length == 0)
        {
            OverviewItems = _currentViewItems;
            NotifyOverviewViewChanged();
            return;
        }

        OverviewItems = _currentViewItems
            .Where(row => SearchOverviewDependencies
                ? row.DependencyNames.Any(name =>
                    name.Contains(query, StringComparison.OrdinalIgnoreCase))
                : row.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        NotifyOverviewViewChanged();
    }

    private IReadOnlyList<EntityOverviewRow> FilterByWorkflow(EntityWorkflowState workflowState) =>
        _allOverviewItems.Where(row => row.WorkflowState == workflowState).ToArray();

    private void NotifyOverviewViewChanged()
    {
        OnPropertyChanged(nameof(HasOverviewItems));
        OnPropertyChanged(nameof(ShowOverviewEmptyState));
        OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
        OnPropertyChanged(nameof(OverviewSearchResultSummary));
    }

    private void UpdateProgressCounts(IEnumerable<EntityOverviewItem> items)
    {
        EntityOverviewItem[] itemArray = items.ToArray();
        NotStartedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.NotStarted);
        InProgressCount = itemArray.Count(static item => item.Status == DevelopmentStatus.InProgress);
        DevelopmentCompletedCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.DevelopmentCompleted);
        ReconciledCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.Reconciled);
        OnPropertyChanged(nameof(SuccessfulCompletionPercentage));
        OnPropertyChanged(nameof(ReconciledPercentage));
    }

    private void EndBusyOperation()
    {
        IsBusy = false;
        BusyMessage = string.Empty;
        NotifyCommandsChanged();
    }

    private void NotifyCommandsChanged()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _importCsvCommand.NotifyCanExecuteChanged();
        _applySynchronizationCommand.NotifyCanExecuteChanged();
        _cancelSynchronizationCommand.NotifyCanExecuteChanged();
        _editOverviewEntityCommand.NotifyCanExecuteChanged();
        _editReviewEntityCommand.NotifyCanExecuteChanged();
        _openOverviewSearchCommand.NotifyCanExecuteChanged();
    }

    private async Task OnManualEntityCreatedAsync()
    {
        SelectedTabIndex = 0;
        IsBusy = true;
        BusyMessage = "Recomputing dependency ranking…";
        try
        {
            await LoadOverviewAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetOverviewFailure(
                $"The entity was created, but persisted entities could not be reloaded: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    private async Task OnDependencyEditsPersistedAsync()
    {
        try
        {
            await LoadOverviewAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetOverviewFailure(
                $"Dependency changes were saved, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OnEntityArchivedAsync()
    {
        try
        {
            await LoadOverviewAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetOverviewFailure(
                $"The entity was archived, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OnEntityRestoredAsync()
    {
        ManualCreation.Reset();
        SelectedTabIndex = 0;
        SelectedOverviewFilter = OverviewManagerFilter.AllActive;
        try
        {
            await LoadOverviewAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            SetOverviewFailure(
                $"The entity was restored, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private Task OpenOverviewEntityAsync(EntityOverviewRow row) =>
        row.LifecycleState == EntityLifecycleState.Archived
            ? Editor.BeginArchivedAsync(row.EntityId)
            : Editor.BeginStandaloneAsync(row.EntityId);

    private Task OpenArchivedFromCreationAsync(EntityId entityId) =>
        Editor.BeginArchivedAsync(entityId);

    private void OnReviewDependencyEditsStaged(SchemaSynchronizationPlan plan)
    {
        Review.ReplacePlan(plan);
        SelectedTabIndex = 1;
        NotifyCommandsChanged();
    }

    private async Task EditReviewEntityAsync(SchemaSynchronizationReviewRow row)
    {
        if (Review.CurrentPlan is null)
        {
            return;
        }

        await Editor.BeginReviewAsync(Review.CurrentPlan, row.EntityId);
    }

    private void OnManualCreationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManualEntityCreationViewModel.IsBusy))
        {
            NotifyCommandsChanged();
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityDependencyEditorViewModel.IsBusy) or
            nameof(EntityDependencyEditorViewModel.IsOpen))
        {
            NotifyCommandsChanged();
        }
    }

    private static EntityOverviewRow CreateOverviewRow(EntityOverviewItem item)
    {
        bool isArchived = item.LifecycleState == EntityLifecycleState.Archived;
        return new EntityOverviewRow(
            item.EntityId,
            item.LifecycleState,
            item.WorkflowState,
            item.DependencyState,
            FormatRank(item.Rank),
            item.SourceName,
            FormatProvenance(item.Provenance),
            FormatStatus(item.Status),
            FormatWorkflowState(item.WorkflowState),
            isArchived
                ? "—"
                : item.DependencyCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            item.DependencyNames,
            item.DependencyResolutionIssueNames,
            FormatGraphIssueTitle(item.DependencyState),
            FormatGraphIssueDescription(item.DependencyState),
            FormatGraphIssueNames(item.DependencyResolutionIssueNames),
            isArchived ? "—" : FormatMissingDependencies(item.MissingDependencyNames),
            item.Notes,
            isArchived ? "View and restore" : "Edit entity");
    }

    private static string FormatStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string FormatWorkflowState(EntityWorkflowState state) => state switch
    {
        EntityWorkflowState.Ready => "Ready",
        EntityWorkflowState.Blocked => "Blocked",
        EntityWorkflowState.InProgress => "In progress",
        EntityWorkflowState.DevelopmentCompleted => "Dev. completed",
        EntityWorkflowState.Reconciled => "Reconciled",
        EntityWorkflowState.Archived => "Archived",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string FormatProvenance(EntityProvenance provenance) => provenance switch
    {
        EntityProvenance.Imported => "CSV",
        EntityProvenance.ManualOnly => "Manual only",
        EntityProvenance.ManualAndImported => "Manual + CSV",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, null)
    };

    private static string FormatRank(int? rank) =>
        rank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private static string FormatGraphIssueTitle(DependencyResolutionState? state) => state switch
    {
        DependencyResolutionState.Unresolved => "Unresolved dependency",
        DependencyResolutionState.Blocked => "Upstream unresolved",
        DependencyResolutionState.Resolved or null => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string FormatGraphIssueDescription(DependencyResolutionState? state) =>
        state switch
        {
            DependencyResolutionState.Unresolved =>
                "This entity has at least one dependency name that does not match an active " +
                "entity, so it cannot receive a dependency-safe rank.",
            DependencyResolutionState.Blocked =>
                "This entity depends on another entity whose dependency chain contains an " +
                "unresolved reference, so it cannot receive a dependency-safe rank.",
            DependencyResolutionState.Resolved or null => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

    private static string FormatGraphIssueNames(IReadOnlyList<string> names) =>
        names.Count == 0
            ? string.Empty
            : $"Unresolved names affecting this entity: {string.Join(", ", names)}";

    private static string FormatMissingDependencies(IReadOnlyList<string> names) =>
        names.Count == 0 ? "—" : string.Join(", ", names);

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
