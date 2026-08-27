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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EntityOverviewService _overviewService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly BulkStatusUpdateService _bulkStatusUpdateService;
    private readonly ICsvFilePicker _filePicker;
    private readonly ISchemaSynchronizationConfirmation _confirmationService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _importCsvCommand;
    private readonly AsyncCommand _applySynchronizationCommand;
    private readonly AsyncCommand _cancelSynchronizationCommand;
    private readonly AsyncCommand _applyBulkStatusCommand;
    private readonly AsyncCommand<EntityOverviewRow> _editOverviewEntityCommand;
    private readonly AsyncCommand<SchemaSynchronizationReviewRow> _editReviewEntityCommand;
    private readonly RelayCommand _openSqlQueryCommand;
    private readonly RelayCommand<DevelopmentStatus> _selectOverviewStatusCommand;
    private readonly RelayCommand<SynchronizationProgressImpactRow> _keepSynchronizationStatusCommand;
    private readonly RelayCommand<SynchronizationProgressImpactRow> _markSynchronizationReworkCommand;
    private IReadOnlyList<EntityId> _selectedOverviewEntityIds = [];
    private string? _overviewErrorMessage;
    private string _busyMessage = string.Empty;
    private string? _operationMessage;
    private SchemaImportSummary? _latestImportSummary;
    private bool _isBusy;
    private MainWindowTab _selectedTab = MainWindowTab.Overview;
    private int _notStartedCount;
    private int _inProgressCount;
    private int _reworkNeededCount;
    private int _developmentCompletedCount;
    private int _reconciledCount;
    private DevelopmentStatus _selectedBulkStatus = DevelopmentStatus.InProgress;

    public MainWindowViewModel(
        EntityOverviewService overviewService,
        SchemaSynchronizationService synchronizationService,
        BulkStatusUpdateService bulkStatusUpdateService,
        ManualEntityCreationService manualEntityCreationService,
        EntityDependencyEditorService entityDependencyEditorService,
        EntityLifecycleService entityLifecycleService,
        ICsvFilePicker filePicker,
        ProgressDashboardViewModel progressDashboard,
        IClipboardService clipboard,
        ISchemaSynchronizationConfirmation confirmationService,
        ConnectionsViewModel? connections = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(overviewService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(bulkStatusUpdateService);
        ArgumentNullException.ThrowIfNull(manualEntityCreationService);
        ArgumentNullException.ThrowIfNull(entityDependencyEditorService);
        ArgumentNullException.ThrowIfNull(entityLifecycleService);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(progressDashboard);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(confirmationService);
        _overviewService = overviewService;
        _synchronizationService = synchronizationService;
        _bulkStatusUpdateService = bulkStatusUpdateService;
        _filePicker = filePicker;
        _confirmationService = confirmationService;
        ILoggerFactory effectiveLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = effectiveLoggerFactory.CreateLogger<MainWindowViewModel>();
        ActiveTable = EntityTableViewModel.CreateActive();
        ArchivedTable = EntityTableViewModel.CreateArchived();
        ActiveTable.ProjectionChanging += (_, _) => ClearOverviewSelection();
        ActiveTable.PropertyChanged += OnActiveTablePropertyChanged;
        ArchivedTable.PropertyChanged += OnArchivedTablePropertyChanged;
        Progress = progressDashboard;
        Help = new SqlQueryHelpViewModel(
            clipboard,
            () => SelectedTab = MainWindowTab.SchemaSynchronization,
            effectiveLoggerFactory.CreateLogger<SqlQueryHelpViewModel>());
        Connections = connections;
        Review = new SchemaSynchronizationReviewViewModel();
        ManualCreation = new ManualEntityCreationViewModel(
            manualEntityCreationService,
            OnManualEntityCreatedAsync,
            OpenArchivedFromCreationAsync,
            () => SelectedTab = MainWindowTab.Overview,
            () => !IsBusy,
            effectiveLoggerFactory.CreateLogger<ManualEntityCreationViewModel>());
        ManualCreation.PropertyChanged += OnManualCreationPropertyChanged;
        Editor = new EntityDependencyEditorViewModel(
            entityDependencyEditorService,
            entityLifecycleService,
            synchronizationService,
            OnDependencyEditsPersistedAsync,
            OnEntityArchivedAsync,
            OnEntityRestoredAsync,
            OnReviewDependencyEditsStaged,
            () => !IsBusy && !ManualCreation.IsBusy,
            effectiveLoggerFactory.CreateLogger<EntityDependencyEditorViewModel>());
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
        _applyBulkStatusCommand = new AsyncCommand(
            () => ApplyBulkStatusAsync(),
            CanApplyBulkStatus);
        _editOverviewEntityCommand = new AsyncCommand<EntityOverviewRow>(
            OpenOverviewEntityAsync,
            _ => !IsBusy &&
                 !ManualCreation.IsBusy &&
                 !Editor.IsOpen &&
                 !Review.HasReview);
        _editReviewEntityCommand = new AsyncCommand<SchemaSynchronizationReviewRow>(
            row => EditReviewEntityAsync(row),
            _ => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen && Review.HasReview);
        _openSqlQueryCommand = new RelayCommand(
            () => SelectedTab = MainWindowTab.SqlHelp,
            () => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen);
        _selectOverviewStatusCommand = new RelayCommand<DevelopmentStatus>(
            status => ActiveTable.SetSingleStatusFilter(status));
        _keepSynchronizationStatusCommand = new RelayCommand<SynchronizationProgressImpactRow>(
            row => StageSynchronizationProgressDecision(
                row,
                SynchronizationProgressDecision.KeepCurrentStatus),
            _ => !IsBusy && Review.HasReview && !Editor.IsOpen);
        _markSynchronizationReworkCommand = new RelayCommand<SynchronizationProgressImpactRow>(
            row => StageSynchronizationProgressDecision(
                row,
                SynchronizationProgressDecision.MarkReworkNeeded),
            _ => !IsBusy && Review.HasReview && !Editor.IsOpen);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? OverviewSelectionClearRequested;

    public SchemaSynchronizationReviewViewModel Review { get; }

    public ManualEntityCreationViewModel ManualCreation { get; }

    public EntityDependencyEditorViewModel Editor { get; }

    public ProgressDashboardViewModel Progress { get; }

    public SqlQueryHelpViewModel Help { get; }

    public ConnectionsViewModel? Connections { get; }

    public EntityTableViewModel ActiveTable { get; }

    public EntityTableViewModel ArchivedTable { get; }

    public IReadOnlyList<DevelopmentStatusOption> BulkStatusOptions { get; } =
    [
        new(DevelopmentStatus.NotStarted, "Not started"),
        new(DevelopmentStatus.InProgress, "In progress"),
        new(DevelopmentStatus.ReworkNeeded, "Rework needed"),
        new(DevelopmentStatus.DevelopmentCompleted, "Dev. completed"),
        new(DevelopmentStatus.Reconciled, "Reconciled")
    ];

    public IReadOnlyList<EntityOverviewRow> OverviewItems => ActiveTable.Items;

    public IReadOnlyList<EntityOverviewRow> ArchivedItems => ArchivedTable.Items;

    public string OverviewSearchQuery
    {
        get => ActiveTable.SearchQuery;
        set => ActiveTable.SearchQuery = value;
    }

    public bool IsOverviewSearchOpen => ActiveTable.IsSearchOpen;

    public bool SearchOverviewDependencies
    {
        get => ActiveTable.SearchDependenciesInstead;
        set => ActiveTable.SearchDependenciesInstead = value;
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
                OnPropertyChanged(nameof(ShowArchivedEmptyState));
                OnPropertyChanged(nameof(ShowArchivedSearchEmptyState));
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
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

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    public SchemaImportSummary? LatestImportSummary
    {
        get => _latestImportSummary;
        private set
        {
            if (SetField(ref _latestImportSummary, value))
            {
                OnPropertyChanged(nameof(HasLatestImport));
                OnPropertyChanged(nameof(LatestImportHeadline));
                OnPropertyChanged(nameof(LatestImportDetails));
            }
        }
    }

    public bool HasLatestImport => LatestImportSummary is not null;

    public string LatestImportHeadline => LatestImportSummary is null
        ? "No successful CSV import has been applied yet."
        : $"Latest import: {LatestImportSummary.SourceFileName}";

    public string LatestImportDetails => LatestImportSummary is null
        ? "Choose an import type and select a CSV when you are ready."
        : $"{LatestImportSummary.Mode} · " +
          $"{LatestImportSummary.AppliedAtUtc.ToLocalTime():g} · " +
          $"{LatestImportSummary.NewEntityCount} new, " +
          $"{LatestImportSummary.ChangedEntityCount} changed, " +
          $"{LatestImportSummary.ArchivedEntityCount} archived, " +
          $"{LatestImportSummary.UnchangedEntityCount} unchanged, " +
          $"{LatestImportSummary.UnresolvedEntityCount} unresolved";

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

    public MainWindowTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetField(ref _selectedTab, value))
            {
                ActiveTable.CloseOpenFilter();
                ArchivedTable.CloseOpenFilter();
                ClearOverviewSelection();
                _applyBulkStatusCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DevelopmentStatus SelectedBulkStatus
    {
        get => _selectedBulkStatus;
        set => SetField(ref _selectedBulkStatus, value);
    }

    public int SelectedActiveEntityCount => _selectedOverviewEntityIds.Count;

    public string BulkSelectionSummary => SelectedActiveEntityCount switch
    {
        0 => "No active entities selected",
        1 => "1 active entity selected",
        _ => $"{SelectedActiveEntityCount} active entities selected"
    };

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

    public int ReworkNeededCount
    {
        get => _reworkNeededCount;
        private set
        {
            if (SetField(ref _reworkNeededCount, value))
            {
                OnPropertyChanged(nameof(ImplementedPercentage));
                OnPropertyChanged(nameof(ReworkNeededPercentage));
                OnPropertyChanged(nameof(ReconciledAndReworkPercentage));
            }
        }
    }

    public int DevelopmentCompletedCount
    {
        get => _developmentCompletedCount;
        private set
        {
            if (SetField(ref _developmentCompletedCount, value))
            {
                OnPropertyChanged(nameof(ImplementedPercentage));
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
                OnPropertyChanged(nameof(ImplementedPercentage));
                OnPropertyChanged(nameof(ReconciledPercentage));
                OnPropertyChanged(nameof(ReconciledAndReworkPercentage));
            }
        }
    }

    public int TotalEntityCount => ActiveTable.SourceItems.Count;

    public int ArchivedEntityCount => ArchivedTable.SourceItems.Count;

    public double ImplementedPercentage => TotalEntityCount == 0
        ? 0
        : (ReworkNeededCount + DevelopmentCompletedCount + ReconciledCount) * 100.0 /
          TotalEntityCount;

    public double ReworkNeededPercentage => TotalEntityCount == 0
        ? 0
        : ReworkNeededCount * 100.0 / TotalEntityCount;

    public double ReconciledAndReworkPercentage =>
        ReconciledPercentage + ReworkNeededPercentage;

    public double ReconciledPercentage => TotalEntityCount == 0
        ? 0
        : ReconciledCount * 100.0 / TotalEntityCount;

    public bool HasOverviewItems => ActiveTable.HasSourceItems;

    public bool HasArchivedItems => ArchivedTable.HasSourceItems;

    public bool HasOverviewSearchQuery => !string.IsNullOrWhiteSpace(OverviewSearchQuery);

    public string OverviewSearchResultSummary => ActiveTable.ResultSummary;

    public string ArchivedSearchResultSummary => ArchivedTable.ResultSummary;

    public bool HasOverviewError => !string.IsNullOrWhiteSpace(OverviewErrorMessage);

    public bool ShowOverviewEmptyState => !IsBusy && !HasOverviewItems && !HasOverviewError;

    public bool ShowArchivedEmptyState => !IsBusy && !HasArchivedItems && !HasOverviewError;

    public bool ShowOverviewSearchEmptyState =>
        !IsBusy &&
        HasOverviewItems &&
        ActiveTable.ShowFilteredEmptyState &&
        !HasOverviewError;

    public bool ShowArchivedSearchEmptyState =>
        !IsBusy && ArchivedTable.ShowFilteredEmptyState && !HasOverviewError;

    public bool IsTotalSummarySelected => !ActiveTable.HasFiltersOrSort;

    public bool IsNotStartedSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.NotStarted);

    public bool IsInProgressSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.InProgress);

    public bool IsReworkNeededSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.ReworkNeeded);

    public bool IsDevelopmentCompletedSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.DevelopmentCompleted);

    public bool IsReconciledSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.Reconciled);

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand ImportCsvCommand => _importCsvCommand;

    public ICommand ApplySynchronizationCommand => _applySynchronizationCommand;

    public ICommand CancelSynchronizationCommand => _cancelSynchronizationCommand;

    public ICommand ApplyBulkStatusCommand => _applyBulkStatusCommand;

    public ICommand EditOverviewEntityCommand => _editOverviewEntityCommand;

    public ICommand EditReviewEntityCommand => _editReviewEntityCommand;

    public ICommand OpenOverviewSearchCommand => ActiveTable.OpenSearchCommand;

    public ICommand ClearOverviewSearchCommand => ActiveTable.ClearSearchCommand;

    public ICommand CloseOverviewSearchCommand => ActiveTable.CloseSearchCommand;

    public ICommand OpenSqlQueryCommand => _openSqlQueryCommand;

    public ICommand SelectOverviewStatusCommand => _selectOverviewStatusCommand;

    public ICommand KeepSynchronizationStatusCommand => _keepSynchronizationStatusCommand;

    public ICommand MarkSynchronizationReworkCommand => _markSynchronizationReworkCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Connections is not null)
        {
            await Connections.InitializeAsync(cancellationToken);
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || ManualCreation.IsBusy || Editor.IsOpen)
        {
            return;
        }

        ClearOverviewSelection();
        IsBusy = true;
        BusyMessage = "Loading persisted entities…";
        try
        {
            await LoadOverviewAndProgressAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOverviewFailure("Loading persisted entities was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Persisted entities could not be loaded.");
            SetOverviewFailure($"Persisted entities could not be loaded: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public void UpdateOverviewSelection(IEnumerable<EntityOverviewRow> selectedRows)
    {
        ArgumentNullException.ThrowIfNull(selectedRows);

        EntityId[] selectedIds = selectedRows
            .Where(static row => row.LifecycleState == EntityLifecycleState.Active)
            .Select(static row => row.EntityId)
            .Distinct()
            .ToArray();
        if (_selectedOverviewEntityIds.SequenceEqual(selectedIds))
        {
            return;
        }

        _selectedOverviewEntityIds = selectedIds;
        NotifyOverviewSelectionChanged();
    }

    public async Task ApplyBulkStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBulkStatus())
        {
            return;
        }

        EntityId[] selectedIds = _selectedOverviewEntityIds.ToArray();
        DevelopmentStatus targetStatus = SelectedBulkStatus;
        IsBusy = true;
        BusyMessage = $"Applying {FormatStatus(targetStatus).ToLowerInvariant()} status…";
        OperationMessage = null;
        OverviewErrorMessage = null;
        bool operationCompleted = false;
        try
        {
            BulkStatusUpdateResult result = await _bulkStatusUpdateService.ApplyAsync(
                selectedIds,
                targetStatus,
                cancellationToken);
            operationCompleted = true;
            OperationMessage = FormatBulkStatusResult(result, targetStatus);
            BusyMessage = "Recomputing workflow readiness and progress…";
            await LoadOverviewAndProgressAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearOverviewSelection();
            OverviewErrorMessage = operationCompleted
                ? "The statuses were updated, but refreshing the overview was cancelled. " +
                  "Refresh to load the latest state."
                : "The status update was cancelled; no partial changes were saved.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The selected entity statuses could not be updated.");
            ClearOverviewSelection();
            OverviewErrorMessage = operationCompleted
                ? "The statuses were updated, but the latest overview could not be loaded: " +
                  exception.Message
                : $"The status update was not applied: {exception.Message}";
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
            _logger.LogError(exception, "A CSV file could not be selected.");
            Review.SetFailure($"A CSV file could not be selected: {exception.Message}");
            SelectedTab = MainWindowTab.SchemaSynchronization;
            NotifyCommandsChanged();
            return;
        }

        if (filePath is null)
        {
            return;
        }

        Review.BeginImport(Path.GetFileName(filePath));
        SchemaImportMode mode = Review.Mode;
        SelectedTab = MainWindowTab.SchemaSynchronization;
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
            _logger.LogError(exception, "A CSV synchronization review could not be prepared.");
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
        int archiveCount = plan.ChangeSet.EntityIdsToArchive.Count;
        if (archiveCount > 0 && !_confirmationService.ConfirmArchiveMissingEntities(archiveCount))
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Applying schema synchronization…";
        try
        {
            SchemaImportSummary summary = await _synchronizationService.ApplyAsync(
                plan,
                Review.SelectedFileName,
                cancellationToken);
            LatestImportSummary = summary;
            OperationMessage =
                $"Applied {summary.SourceFileName}: {summary.NewEntityCount} new, " +
                $"{summary.ChangedEntityCount} changed, {summary.ArchivedEntityCount} archived, " +
                $"{summary.UnresolvedEntityCount} unresolved.";
            Review.Clear();
            SelectedTab = MainWindowTab.Overview;
            BusyMessage = "Recomputing dependency ranking…";
            await LoadOverviewAndProgressAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Review.SetOperationFailure(
                "Applying synchronization was cancelled; no partial changes were saved.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Schema synchronization could not be applied.");
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

    private async Task LoadOverviewAndProgressAsync(CancellationToken cancellationToken)
    {
        await LoadOverviewAsync(cancellationToken);
        await Progress.LoadAsync(cancellationToken);
        LatestImportSummary = await _synchronizationService.GetLatestImportAsync(cancellationToken);
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
        ClearOverviewSelection();
        ActiveTable.ReplaceSourceItems(items);
        ArchivedTable.ReplaceSourceItems(archivedItems);
        OnPropertyChanged(nameof(HasOverviewItems));
        OnPropertyChanged(nameof(HasArchivedItems));
        OnPropertyChanged(nameof(ShowOverviewEmptyState));
        OnPropertyChanged(nameof(ShowArchivedEmptyState));
        OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
        OnPropertyChanged(nameof(ShowArchivedSearchEmptyState));
        OnPropertyChanged(nameof(TotalEntityCount));
        OnPropertyChanged(nameof(ArchivedEntityCount));
        OnPropertyChanged(nameof(ImplementedPercentage));
        OnPropertyChanged(nameof(ReworkNeededPercentage));
        OnPropertyChanged(nameof(ReconciledAndReworkPercentage));
        OnPropertyChanged(nameof(ReconciledPercentage));
        OnPropertyChanged(nameof(OverviewSearchResultSummary));
        OnPropertyChanged(nameof(ArchivedSearchResultSummary));
    }

    private void UpdateProgressCounts(IEnumerable<EntityOverviewItem> items)
    {
        EntityOverviewItem[] itemArray = items.ToArray();
        NotStartedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.NotStarted);
        InProgressCount = itemArray.Count(static item => item.Status == DevelopmentStatus.InProgress);
        ReworkNeededCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.ReworkNeeded);
        DevelopmentCompletedCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.DevelopmentCompleted);
        ReconciledCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.Reconciled);
        OnPropertyChanged(nameof(ImplementedPercentage));
        OnPropertyChanged(nameof(ReworkNeededPercentage));
        OnPropertyChanged(nameof(ReconciledAndReworkPercentage));
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
        _applyBulkStatusCommand.NotifyCanExecuteChanged();
        _editOverviewEntityCommand.NotifyCanExecuteChanged();
        _editReviewEntityCommand.NotifyCanExecuteChanged();
        _openSqlQueryCommand.NotifyCanExecuteChanged();
        _keepSynchronizationStatusCommand.NotifyCanExecuteChanged();
        _markSynchronizationReworkCommand.NotifyCanExecuteChanged();
    }

    private bool CanApplyBulkStatus() =>
        SelectedActiveEntityCount > 0 &&
        SelectedTab == MainWindowTab.Overview &&
        !IsBusy &&
        !ManualCreation.IsBusy &&
        !Editor.IsOpen &&
        !Review.HasReview;

    public void ClearOverviewSelection()
    {
        bool hadSelection = _selectedOverviewEntityIds.Count > 0;
        _selectedOverviewEntityIds = [];
        if (hadSelection)
        {
            NotifyOverviewSelectionChanged();
        }

        OverviewSelectionClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyOverviewSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedActiveEntityCount));
        OnPropertyChanged(nameof(BulkSelectionSummary));
        _applyBulkStatusCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBulkStatusResult(
        BulkStatusUpdateResult result,
        DevelopmentStatus targetStatus)
    {
        string changed = result.ChangedCount == 1
            ? "1 entity updated"
            : $"{result.ChangedCount} entities updated";
        string unchanged = result.UnchangedCount == 1
            ? "1 already matched"
            : $"{result.UnchangedCount} already matched";
        return $"{changed} to {FormatStatus(targetStatus)}; {unchanged}.";
    }

    private async Task OnManualEntityCreatedAsync()
    {
        SelectedTab = MainWindowTab.Overview;
        IsBusy = true;
        BusyMessage = "Recomputing dependency ranking…";
        try
        {
            await LoadOverviewAndProgressAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The overview could not be reloaded after entity creation.");
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
            await LoadOverviewAndProgressAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The overview could not be reloaded after dependency edits.");
            SetOverviewFailure(
                $"Dependency changes were saved, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OnEntityArchivedAsync()
    {
        SelectedTab = MainWindowTab.Overview;
        try
        {
            await LoadOverviewAndProgressAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The overview could not be reloaded after entity archival.");
            SetOverviewFailure(
                $"The entity was archived, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OnEntityRestoredAsync()
    {
        ManualCreation.Reset();
        ActiveTable.ClearAllFiltersAndSort();
        ActiveTable.ClearSearchCommand.Execute(null);
        SelectedTab = MainWindowTab.Overview;
        try
        {
            await LoadOverviewAndProgressAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The overview could not be reloaded after entity restoration.");
            SetOverviewFailure(
                $"The entity was restored, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private Task OpenOverviewEntityAsync(EntityOverviewRow row) =>
        row.LifecycleState == EntityLifecycleState.Archived
            ? Editor.BeginArchivedAsync(row.EntityId)
            : Editor.BeginStandaloneAsync(row.EntityId);

    private async Task OpenArchivedFromCreationAsync(EntityId entityId)
    {
        SelectedTab = MainWindowTab.Archived;
        await Editor.BeginArchivedAsync(entityId);
    }

    private void OnReviewDependencyEditsStaged(SchemaSynchronizationPlan plan)
    {
        Review.ReplacePlan(plan);
        SelectedTab = MainWindowTab.SchemaSynchronization;
        NotifyCommandsChanged();
    }

    private void StageSynchronizationProgressDecision(
        SynchronizationProgressImpactRow row,
        SynchronizationProgressDecision decision)
    {
        if (Review.CurrentPlan is null)
        {
            return;
        }

        SchemaSynchronizationPlan revised = _synchronizationService.StageProgressDecision(
            Review.CurrentPlan,
            row.EntityId,
            decision);
        Review.ReplacePlan(revised);
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

    private void OnActiveTablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EntityTableViewModel.Items):
                OnPropertyChanged(nameof(OverviewItems));
                OnPropertyChanged(nameof(OverviewSearchResultSummary));
                OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
                break;
            case nameof(EntityTableViewModel.SearchQuery):
                OnPropertyChanged(nameof(OverviewSearchQuery));
                OnPropertyChanged(nameof(HasOverviewSearchQuery));
                break;
            case nameof(EntityTableViewModel.IsSearchOpen):
                OnPropertyChanged(nameof(IsOverviewSearchOpen));
                break;
            case nameof(EntityTableViewModel.HasFilters):
            case nameof(EntityTableViewModel.HasSort):
            case nameof(EntityTableViewModel.HasFiltersOrSort):
                NotifySummarySelectionChanged();
                break;
        }
    }

    private void OnArchivedTablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntityTableViewModel.Items))
        {
            OnPropertyChanged(nameof(ArchivedItems));
            OnPropertyChanged(nameof(ArchivedSearchResultSummary));
            OnPropertyChanged(nameof(ShowArchivedSearchEmptyState));
        }
    }

    private void NotifySummarySelectionChanged()
    {
        OnPropertyChanged(nameof(IsTotalSummarySelected));
        OnPropertyChanged(nameof(IsNotStartedSummarySelected));
        OnPropertyChanged(nameof(IsInProgressSummarySelected));
        OnPropertyChanged(nameof(IsReworkNeededSummarySelected));
        OnPropertyChanged(nameof(IsDevelopmentCompletedSummarySelected));
        OnPropertyChanged(nameof(IsReconciledSummarySelected));
    }

    private static EntityOverviewRow CreateOverviewRow(EntityOverviewItem item)
    {
        bool isArchived = item.LifecycleState == EntityLifecycleState.Archived;
        return new EntityOverviewRow(
            item.EntityId,
            item.LifecycleState,
            item.Status,
            item.WorkflowState,
            item.DependencyState,
            FormatPriority(item.EffectivePriority),
            FormatRank(item.Rank),
            item.SourceName,
            item.ResponsibleDeveloper,
            item.GroupName,
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
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string FormatPriority(int? priority) =>
        priority?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private static string FormatWorkflowState(EntityWorkflowState state) => state switch
    {
        EntityWorkflowState.Ready => "Ready",
        EntityWorkflowState.Blocked => "Blocked",
        EntityWorkflowState.InProgress => "In progress",
        EntityWorkflowState.ReworkNeeded => "Rework needed",
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
