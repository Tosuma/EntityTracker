using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>The current view applied to Tracker data on Overview, Archived and the graph.</summary>
internal readonly record struct WorkspaceViewState(
    TableViewState Active, TableViewState Archived, string GraphSearch);

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly EntityOverviewService _overviewService;
    private readonly TrackerId _trackerId;
    private readonly ProjectDeveloperService? _developers;
    private readonly IResponsibilityPeriodRepository? _responsibilityPeriods;
    private readonly ITrackedStateStore? _trackedState;
    private readonly LocalProjectIdentityService? _localIdentity;
    private readonly OverviewExportService? _overviewExportService;
    private readonly IOverviewExportFilePicker? _overviewExportFilePicker;
    private readonly OverviewExportSettingsViewModel? _overviewExportSettings;
    private readonly NotificationCenter? _notifications;
    private readonly AsyncCommand<OverviewExportFormat> _exportOverviewCommand;
    private bool _isExporting;
    private readonly AsyncCommand _assignMeCommand;
    private readonly AsyncCommand _openIdentitySettingsCommand;
    private string? _assignmentGuidance;
    private readonly BulkStatusUpdateService _bulkStatusUpdateService;
    private readonly IContextDiscardConfirmation _discardConfirmation;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AsyncCommand _refreshCommand;
    private readonly RelayCommand _openCreateEntityCommand;
    private readonly AsyncCommand _applyBulkStatusCommand;
    private readonly RelayCommand<EntityOverviewRow> _openEntityDetailsCommand;
    private readonly RelayCommand _closeEntityDetailsCommand;
    private readonly RelayCommand _showFullResponsibilityHistoryCommand;
    private readonly AsyncCommand _backFromResponsibilityHistoryCommand;
    private readonly RelayCommand _showArchivedResponsibilityHistoryCommand;
    private readonly AsyncCommand<EntityOverviewRow> _editOverviewEntityCommand;
    private readonly RelayCommand<DevelopmentStatus> _selectOverviewStatusCommand;
    private readonly RelayCommand _selectImplementedSummaryCommand;
    private IReadOnlyList<EntityId> _selectedOverviewEntityIds = [];
    private bool _hasOverviewError;
    private string _busyMessage = string.Empty;
    private bool _isBusy;
    private MainWindowTab _selectedTab = MainWindowTab.Overview;
    private int _notStartedCount;
    private int _inProgressCount;
    private int _reworkNeededCount;
    private int _reworkingCount;
    private int _notRelevantCount;
    private int _blockedCount;
    private int _developmentCompletedCount;
    private int _reconciledCount;
    private DevelopmentStatus _selectedBulkStatus = DevelopmentStatus.InProgress;
    private EntityDetailsViewModel? _selectedEntityDetails;
    private EntityId? _historyReturnToArchivedEntityId;

    public MainWindowViewModel(
        TrackerId trackerId,
        EntityOverviewService overviewService,
        BulkStatusUpdateService bulkStatusUpdateService,
        ManualEntityCreationService manualEntityCreationService,
        EntityDependencyEditorService entityDependencyEditorService,
        EntityLifecycleService entityLifecycleService,
        IContextDiscardConfirmation discardConfirmation,
        ILoggerFactory? loggerFactory = null,
        ProjectDeveloperService? developers = null,
        IResponsibilityPeriodRepository? responsibilityPeriods = null,
        ITrackedStateStore? trackedState = null,
        LocalProjectIdentityService? localIdentity = null,
        OverviewExportService? overviewExportService = null,
        IOverviewExportFilePicker? overviewExportFilePicker = null,
        OverviewExportSettingsViewModel? overviewExportSettings = null,
        NotificationCenter? notifications = null,
        IProgressChartFilePicker? graphFilePicker = null)
    {
        ArgumentNullException.ThrowIfNull(overviewService);
        ArgumentNullException.ThrowIfNull(bulkStatusUpdateService);
        ArgumentNullException.ThrowIfNull(manualEntityCreationService);
        ArgumentNullException.ThrowIfNull(entityDependencyEditorService);
        ArgumentNullException.ThrowIfNull(entityLifecycleService);
        ArgumentNullException.ThrowIfNull(discardConfirmation);
        _trackerId = trackerId;
        _developers = developers;
        _responsibilityPeriods = responsibilityPeriods;
        _trackedState = trackedState;
        _localIdentity = localIdentity;
        _overviewExportService = overviewExportService;
        _overviewExportFilePicker = overviewExportFilePicker;
        _overviewExportSettings = overviewExportSettings;
        _notifications = notifications;
        _overviewService = overviewService;
        _bulkStatusUpdateService = bulkStatusUpdateService;
        _discardConfirmation = discardConfirmation;
        ILoggerFactory effectiveLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = effectiveLoggerFactory.CreateLogger<MainWindowViewModel>();
        ActiveTable = EntityTableViewModel.CreateActive();
        ArchivedTable = EntityTableViewModel.CreateArchived();
        ActiveTable.ProjectionChanging += OnActiveTableProjectionChanging;
        ArchivedTable.ProjectionChanging += OnArchivedTableProjectionChanging;
        ActiveTable.PropertyChanged += OnActiveTablePropertyChanged;
        ArchivedTable.PropertyChanged += OnArchivedTablePropertyChanged;
        DependencyGraph = new DependencyGraphViewModel(OpenEntityDetails, graphFilePicker, notifications,
            effectiveLoggerFactory.CreateLogger<DependencyGraphViewModel>());
        ManualCreation = new ManualEntityCreationViewModel(
            trackerId,
            manualEntityCreationService,
            OnManualEntityCreatedAsync,
            OpenArchivedFromCreationAsync,
            () => { },
            () => !IsBusy,
            effectiveLoggerFactory.CreateLogger<ManualEntityCreationViewModel>(),
            developers is null ? null : new DeveloperPickerViewModel(trackerId, developers),
            () => discardConfirmation.ConfirmDiscard("The new entity has unfinished changes."));
        ManualCreation.PropertyChanged += OnManualCreationPropertyChanged;
        Editor = new EntityDependencyEditorViewModel(
            trackerId,
            entityDependencyEditorService,
            entityLifecycleService,
            OnDependencyEditsPersistedAsync,
            OnEntityArchivedAsync,
            OnEntityRestoredAsync,
            OnEntityPurgedAsync,
            () => !IsBusy && !ManualCreation.IsBusy,
            effectiveLoggerFactory.CreateLogger<EntityDependencyEditorViewModel>(),
            developers is null ? null : new DeveloperPickerViewModel(trackerId, developers),
            responsibilityPeriods,
            developers, localIdentity, () => OpenIdentitySettingsAsync(), notifications);
        Editor.PropertyChanged += OnEditorPropertyChanged;
        _refreshCommand = new AsyncCommand(
            () => RefreshAsync(),
            () => !IsBusy && !ManualCreation.IsBusy && !ManualCreation.IsOpen && !Editor.IsOpen);
        _openCreateEntityCommand = new RelayCommand(
            ManualCreation.Open,
            () => !IsBusy && !ManualCreation.IsOpen && !Editor.IsOpen);
        _exportOverviewCommand = new AsyncCommand<OverviewExportFormat>(ExportOverviewAsync,
            _ => !_isExporting && !IsBusy && !Editor.IsOpen && ActiveTable.SourceItems.Count > 0);
        _applyBulkStatusCommand = new AsyncCommand(
            () => ApplyBulkStatusAsync(),
            CanApplyBulkStatus);
        _openEntityDetailsCommand = new RelayCommand<EntityOverviewRow>(
            OpenEntityDetails,
            _ => !IsBusy && !ManualCreation.IsBusy && !Editor.IsOpen);
        _closeEntityDetailsCommand = new RelayCommand(
            CloseEntityDetails,
            () => IsEntityDetailsOpen);
        _showFullResponsibilityHistoryCommand = new RelayCommand(
            () => SelectedEntityDetails?.ShowFullHistory());
        _backFromResponsibilityHistoryCommand = new AsyncCommand(BackFromResponsibilityHistoryAsync);
        _showArchivedResponsibilityHistoryCommand = new RelayCommand(
            ShowArchivedResponsibilityHistory);
        _assignMeCommand = new AsyncCommand(AssignMeAsync,
            () => SelectedEntityDetails?.CanEdit == true && !IsBusy);
        _openIdentitySettingsCommand = new AsyncCommand(OpenIdentitySettingsAsync);
        _editOverviewEntityCommand = new AsyncCommand<EntityOverviewRow>(
            OpenOverviewEntityAsync,
            _ => !IsBusy &&
                 !ManualCreation.IsBusy &&
                 !Editor.IsOpen);
        // A summary card toggles: clicking it when it is the only status shown clears the status filter, back
        // to Total (the sort stays). When it is one of several (as after "% implemented"), it narrows to itself.
        // The "% implemented" card shows what Implemented counts; clicking it again returns to Total.
        _selectImplementedSummaryCommand = new RelayCommand(() =>
        {
            if (IsImplementedSummarySelected)
            {
                ActiveTable.ClearStatusFilter();
            }
            else
            {
                ActiveTable.SetStatusFilter(ProgressSnapshotState.ImplementedStatuses);
            }
        });
        _selectOverviewStatusCommand = new RelayCommand<DevelopmentStatus>(status =>
        {
            if (ActiveTable.FiltersExactlyStatuses([status]))
            {
                ActiveTable.ClearStatusFilter();
            }
            else
            {
                ActiveTable.SetSingleStatusFilter(status);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? OverviewSelectionClearRequested;

    public event EventHandler? PersistedStateChanged;
    public event Func<Task>? IdentitySettingsRequested;

    public string? AssignmentGuidance
    {
        get => _assignmentGuidance;
        private set { if (SetField(ref _assignmentGuidance, value))
            OnPropertyChanged(nameof(HasAssignmentGuidance)); }
    }
    public bool HasAssignmentGuidance => !string.IsNullOrWhiteSpace(AssignmentGuidance);
    public ICommand AssignMeCommand => _assignMeCommand;
    public ICommand ExportOverviewCommand => _exportOverviewCommand;

    /// <summary>
    /// What an Overview export holds: the shown or all active entities (the Settings choice), and that it
    /// includes internal notes and developer names, unlike the Project report's client version.
    /// </summary>
    public string ExportScopeSummary
    {
        get
        {
            bool all = _overviewExportSettings?.Rows == OverviewExportRows.AllActiveEntities;
            int count = all ? ActiveTable.GetAllItemsInCurrentSortOrder().Count : ActiveTable.Items.Count;
            string rows = all ? $"All {count} active entities" : $"The {count} shown entities";
            return $"{rows}, with internal notes and developer names. Change the rows in Settings › " +
                   "Tracker views; share with a client through the Project report.";
        }
    }

    private async Task ExportOverviewAsync(OverviewExportFormat format)
    {
        if (_overviewExportService is null || _overviewExportFilePicker is null ||
            _notifications is null || _isExporting) return;
        string extension = format == OverviewExportFormat.Excel ? "xlsx" : "csv";
        string suggestedName = $"EntityTracker-overview-{DateTime.Now:yyyy-MM-dd}.{extension}";
        string? path = _overviewExportFilePicker.SelectPath(format, suggestedName);
        if (path is null) return;
        IReadOnlyList<EntityOverviewRow> rows = _overviewExportSettings?.Rows ==
            OverviewExportRows.AllActiveEntities
            ? ActiveTable.GetAllItemsInCurrentSortOrder() : ActiveTable.Items.ToArray();
        OverviewCsvSeparator separator = _overviewExportSettings?.Separator ?? OverviewCsvSeparator.Semicolon;
        _isExporting = true;
        _exportOverviewCommand.NotifyCanExecuteChanged();
        NotificationItem notice = _notifications.BeginProgress("Overview export",
            $"Preparing {rows.Count} entities for {extension.ToUpperInvariant()} export…");
        try
        {
            _notifications.Progress(notice, $"Writing {extension.ToUpperInvariant()} file…");
            await _overviewExportService.ExportAsync(path, format, rows, separator);
            _notifications.Complete(notice, $"Exported {rows.Count} entities to {Path.GetFileName(path)}.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Overview export failed");
            _notifications.Complete(notice, "Overview export failed. Check the destination and try again.",
                NotificationKind.Failure);
        }
        finally
        {
            _isExporting = false;
            _exportOverviewCommand.NotifyCanExecuteChanged();
        }
    }
    public ICommand OpenIdentitySettingsCommand => _openIdentitySettingsCommand;

    private async Task OpenIdentitySettingsAsync()
    {
        if (IdentitySettingsRequested is { } handler) await handler();
    }

    private async Task AssignMeAsync()
    {
        EntityDetailsViewModel? details = SelectedEntityDetails;
        if (details?.CanEdit != true || _localIdentity is null ||
            _trackedState is null || _responsibilityPeriods is null) return;
        IsBusy = true;
        BusyMessage = "Updating responsibility…";
        try
        {
            ProjectDeveloper? developer = await _localIdentity.ResolveForTrackerAsync(_trackerId);
            if (developer is null)
            {
                AssignmentGuidance = "Choose You in this Project in Settings before assigning yourself.";
                return;
            }
            IReadOnlyList<ResponsibilityPeriod> periods =
                await _responsibilityPeriods.GetByEntityAsync(details.EntityId);
            bool assigned = periods.Any(period => period.IsCurrent && period.DeveloperId == developer.Id);
            await _trackedState.ApplyAsync(_trackerId, assigned
                ? new TrackedStateChangeSet([], [], [], responsibilityRemovals:
                    [new ResponsibilityRemoval(details.EntityId, developer.Id)])
                : new TrackedStateChangeSet([], [], [], responsibilityAdditions:
                    [new ResponsibilityAddition(details.EntityId, developer.Id)]));
            await LoadOverviewAndProgressAsync(CancellationToken.None);
            EntityOverviewRow? row = ActiveTable.SourceItems.FirstOrDefault(item =>
                item.EntityId == details.EntityId);
            if (row is not null) OpenEntityDetails(row);
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Self-assignment could not be changed.");
            AssignmentGuidance = "Responsibility could not be saved. Check You in this Project in Settings and retry.";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    public event Action<EntityId>? EntityRevealRequested;

    /// <summary>Gets the current view so it can be applied to another Tracker's data.</summary>
    internal WorkspaceViewState CaptureView() =>
        new(ActiveTable.CaptureView(), ArchivedTable.CaptureView(), DependencyGraph.SearchText);

    /// <summary>Applies the current view when switching Tracker.</summary>
    internal void RestoreView(WorkspaceViewState state)
    {
        ActiveTable.RestoreView(state.Active);
        ArchivedTable.RestoreView(state.Archived);
        DependencyGraph.SearchText = state.GraphSearch;
        DependencyGraph.IsSuggestionsOpen = false;
    }

    public TrackerId TrackerId => _trackerId;

    public bool HasUnsavedWork =>
        ManualCreation.IsDirty || Editor.IsDirty;

    public ManualEntityCreationViewModel ManualCreation { get; }

    public EntityDependencyEditorViewModel Editor { get; }


    public DependencyGraphViewModel DependencyGraph { get; }

    public EntityTableViewModel ActiveTable { get; }

    public EntityTableViewModel ArchivedTable { get; }

    public EntityDetailsViewModel? SelectedEntityDetails
    {
        get => _selectedEntityDetails;
        private set
        {
            if (SetField(ref _selectedEntityDetails, value))
            {
                OnPropertyChanged(nameof(IsEntityDetailsOpen));
                _closeEntityDetailsCommand.NotifyCanExecuteChanged();
                _assignMeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEntityDetailsOpen => SelectedEntityDetails is not null;

    public IReadOnlyList<DevelopmentStatusOption> BulkStatusOptions { get; } =
    [
        new(DevelopmentStatus.NotStarted, "Not started"),
        new(DevelopmentStatus.Blocked, "Blocked"),
        new(DevelopmentStatus.InProgress, "In progress"),
        new(DevelopmentStatus.ReworkNeeded, "Rework needed"),
        new(DevelopmentStatus.Reworking, "Reworking"),
        new(DevelopmentStatus.DevelopmentCompleted, "Completed"),
        new(DevelopmentStatus.Reconciled, "Reconciled"),
        new(DevelopmentStatus.NotRelevant, "Not relevant")
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

    /// <summary>
    /// Gets whether the entities could not be loaded; the reason is in the notification center,
    /// and the empty-state hints stay hidden so an unloaded list does not look empty.
    /// </summary>
    public bool HasOverviewError
    {
        get => _hasOverviewError;
        private set
        {
            if (SetField(ref _hasOverviewError, value))
            {
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
                CloseEntityDetails();
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
            }
        }
    }

    public int ReworkingCount
    {
        get => _reworkingCount;
        private set
        {
            if (SetField(ref _reworkingCount, value))
            {
                OnPropertyChanged(nameof(ImplementedPercentage));
            }
        }
    }

    public int BlockedCount
    {
        get => _blockedCount;
        private set => SetField(ref _blockedCount, value);
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
            }
        }
    }

    /// <summary>Not relevant in this Tracker: listed, but left out of every percentage.</summary>
    public int NotRelevantCount
    {
        get => _notRelevantCount;
        private set
        {
            if (SetField(ref _notRelevantCount, value))
                NotifyPercentagesChanged();
        }
    }

    public int TotalEntityCount => ActiveTable.SourceItems.Count;

    /// <summary>The entities progress is measured against: every active one except those not relevant.</summary>
    public int RelevantEntityCount => TotalEntityCount - NotRelevantCount;

    public int ArchivedEntityCount => ArchivedTable.SourceItems.Count;

    public double ImplementedPercentage => RelevantEntityCount <= 0
        ? 0
        : (ReworkNeededCount + ReworkingCount + DevelopmentCompletedCount + ReconciledCount) * 100.0 /
          RelevantEntityCount;

    public double ReworkNeededPercentage => RelevantEntityCount <= 0
        ? 0
        : ReworkNeededCount * 100.0 / RelevantEntityCount;

    public double ReconciledPercentage => RelevantEntityCount <= 0
        ? 0
        : ReconciledCount * 100.0 / RelevantEntityCount;

    private void NotifyPercentagesChanged()
    {
        OnPropertyChanged(nameof(RelevantEntityCount));
        OnPropertyChanged(nameof(ImplementedPercentage));
        OnPropertyChanged(nameof(ReworkNeededPercentage));
        OnPropertyChanged(nameof(ReconciledPercentage));
    }

    public bool HasOverviewItems => ActiveTable.HasSourceItems;

    public bool HasArchivedItems => ArchivedTable.HasSourceItems;

    public bool HasOverviewSearchQuery => !string.IsNullOrWhiteSpace(OverviewSearchQuery);

    public string OverviewSearchResultSummary => ActiveTable.ResultSummary;

    public string ArchivedSearchResultSummary => ArchivedTable.ResultSummary;


    public bool ShowOverviewEmptyState => !IsBusy && !HasOverviewItems && !HasOverviewError;

    public bool ShowArchivedEmptyState => !IsBusy && !HasArchivedItems && !HasOverviewError;

    public bool ShowOverviewSearchEmptyState =>
        !IsBusy &&
        HasOverviewItems &&
        ActiveTable.ShowFilteredEmptyState &&
        !HasOverviewError;

    public bool ShowArchivedSearchEmptyState =>
        !IsBusy && ArchivedTable.ShowFilteredEmptyState && !HasOverviewError;

    /// <summary>Total is selected whenever nothing is filtered out; a sort alone still shows every row.</summary>
    public bool IsTotalSummarySelected => !ActiveTable.HasFilters;

    public bool IsNotStartedSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.NotStarted);

    public bool IsInProgressSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.InProgress);

    public bool IsReworkNeededSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.ReworkNeeded);

    public bool IsReworkingSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.Reworking);

    public bool IsBlockedSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.Blocked);

    public bool IsDevelopmentCompletedSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.DevelopmentCompleted);

    public bool IsReconciledSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.Reconciled);

    public bool IsNotRelevantSummarySelected =>
        ActiveTable.IncludesStatus(DevelopmentStatus.NotRelevant);

    public bool IsImplementedSummarySelected =>
        ActiveTable.FiltersExactlyStatuses(ProgressSnapshotState.ImplementedStatuses);

    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Opens the New entity overlay for this Tracker (Ctrl+N).</summary>
    public ICommand OpenCreateEntityCommand => _openCreateEntityCommand;

    public ICommand ApplyBulkStatusCommand => _applyBulkStatusCommand;

    public ICommand OpenEntityDetailsCommand => _openEntityDetailsCommand;

    public ICommand CloseEntityDetailsCommand => _closeEntityDetailsCommand;
    public ICommand ShowFullResponsibilityHistoryCommand => _showFullResponsibilityHistoryCommand;
    public ICommand BackFromResponsibilityHistoryCommand => _backFromResponsibilityHistoryCommand;
    public ICommand ShowArchivedResponsibilityHistoryCommand => _showArchivedResponsibilityHistoryCommand;

    public ICommand EditOverviewEntityCommand => _editOverviewEntityCommand;

    public ICommand OpenOverviewSearchCommand => ActiveTable.OpenSearchCommand;

    public ICommand ClearOverviewSearchCommand => ActiveTable.ClearSearchCommand;

    public ICommand CloseOverviewSearchCommand => ActiveTable.CloseSearchCommand;

    public ICommand SelectOverviewStatusCommand => _selectOverviewStatusCommand;

    public ICommand SelectImplementedSummaryCommand => _selectImplementedSummaryCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (ManualCreation.DeveloperPicker is not null)
            await ManualCreation.DeveloperPicker.LoadAsync(cancellationToken: cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public void PrepareForDeactivation()
    {
        ActiveTable.CloseOpenFilter();
        ArchivedTable.CloseOpenFilter();
        CloseEntityDetails();
        ClearOverviewSelection();
    }

    public void DiscardTransientWork()
    {
        ManualCreation.Reset();
        Editor.DiscardAndClose();
        PrepareForDeactivation();
        NotifyCommandsChanged();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || ManualCreation.IsBusy || Editor.IsOpen)
        {
            return;
        }

        CloseEntityDetails();
        ClearOverviewSelection();
        IsBusy = true;
        BusyMessage = "Loading persisted entities…";
        try
        {
            await LoadOverviewAndProgressAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Loading persisted entities was cancelled.");
            SetOverviewFailure("Loading persisted entities was cancelled.", NotificationKind.Information);
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
        bool operationCompleted = false;
        try
        {
            BulkStatusUpdateResult result = await _bulkStatusUpdateService.ApplyAsync(
                _trackerId,
                selectedIds,
                targetStatus,
                cancellationToken);
            operationCompleted = true;
            Notify("Bulk status update", FormatBulkStatusResult(result, targetStatus), NotificationKind.Success);
            BusyMessage = "Recomputing workflow readiness and progress…";
            await LoadOverviewAndProgressAsync(cancellationToken);
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("The bulk status update was cancelled (statuses saved: {Saved}).", operationCompleted);
            ClearOverviewSelection();
            Notify("Bulk status update", operationCompleted
                ? "The statuses were updated, but refreshing the overview was cancelled. " +
                  "Refresh to load the latest state."
                : "The status update was cancelled; no partial changes were saved.",
                NotificationKind.Information);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The selected entity statuses could not be updated.");
            ClearOverviewSelection();
            Notify("Bulk status update", operationCompleted
                ? "The statuses were updated, but the latest overview could not be loaded: " +
                  exception.Message
                : $"The status update was not applied: {exception.Message}",
                NotificationKind.Failure);
        }
        finally
        {
            EndBusyOperation();
        }
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        EntityOverviewResult result = await _overviewService.GetAsync(_trackerId, cancellationToken);
        if (!result.IsSuccess)
        {
            SetOverviewFailure(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            return;
        }

        HasOverviewError = false;
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
    }

    private void SetOverviewFailure(string message, NotificationKind kind = NotificationKind.Failure)
    {
        ReplaceOverviewItems([], []);
        HasOverviewError = true;
        Notify("Overview", message, kind);
        UpdateProgressCounts([]);
    }

    private void Notify(string title, string message, NotificationKind kind) =>
        _notifications?.Show(title, message, kind);

    private void ReplaceOverviewItems(
        IReadOnlyList<EntityOverviewRow> items,
        IReadOnlyList<EntityOverviewRow> archivedItems)
    {
        CloseEntityDetails();
        ClearOverviewSelection();
        ActiveTable.ReplaceSourceItems(items);
        ArchivedTable.ReplaceSourceItems(archivedItems);
        DependencyGraph.Rebuild(items);
        OnPropertyChanged(nameof(HasOverviewItems));
        OnPropertyChanged(nameof(HasArchivedItems));
        OnPropertyChanged(nameof(ShowOverviewEmptyState));
        OnPropertyChanged(nameof(ShowArchivedEmptyState));
        OnPropertyChanged(nameof(ShowOverviewSearchEmptyState));
        OnPropertyChanged(nameof(ShowArchivedSearchEmptyState));
        OnPropertyChanged(nameof(TotalEntityCount));
        OnPropertyChanged(nameof(RelevantEntityCount));
        OnPropertyChanged(nameof(ArchivedEntityCount));
        OnPropertyChanged(nameof(ImplementedPercentage));
        OnPropertyChanged(nameof(ReworkNeededPercentage));
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
        ReworkingCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.Reworking);
        BlockedCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.Blocked);
        DevelopmentCompletedCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.DevelopmentCompleted);
        ReconciledCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.Reconciled);
        NotRelevantCount = itemArray.Count(static item =>
            item.Status == DevelopmentStatus.NotRelevant);
        OnPropertyChanged(nameof(ImplementedPercentage));
        OnPropertyChanged(nameof(ReworkNeededPercentage));
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
        _exportOverviewCommand.NotifyCanExecuteChanged();
        _refreshCommand.NotifyCanExecuteChanged();
        _openCreateEntityCommand.NotifyCanExecuteChanged();
        _applyBulkStatusCommand.NotifyCanExecuteChanged();
        _openEntityDetailsCommand.NotifyCanExecuteChanged();
        _assignMeCommand.NotifyCanExecuteChanged();
        _editOverviewEntityCommand.NotifyCanExecuteChanged();
    }

    private bool CanApplyBulkStatus() =>
        SelectedActiveEntityCount > 0 &&
        SelectedTab == MainWindowTab.Overview &&
        !IsBusy &&
        !ManualCreation.IsBusy &&
        !Editor.IsOpen;

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

    /// <summary>
    /// Reloads after a creation and shows the new entity where the user is: its row on Overview, its node
    /// on the dependency graph. While "Create and add another" keeps the overlay open, nothing moves.
    /// </summary>
    private async Task OnManualEntityCreatedAsync(EntityId createdEntityId)
    {
        IsBusy = true;
        BusyMessage = "Recomputing dependency ranking…";
        try
        {
            await LoadOverviewAndProgressAsync(CancellationToken.None);
            EntityOverviewRow? createdRow = ActiveTable.Items.FirstOrDefault(
                row => row.EntityId == createdEntityId);
            if (createdRow is null)
            {
                ActiveTable.ClearAllFiltersAndSort();
                ActiveTable.ClearSearchCommand.Execute(null);
                createdRow = ActiveTable.Items.FirstOrDefault(
                    row => row.EntityId == createdEntityId);
            }

            if (createdRow is not null && !ManualCreation.IsOpen)
            {
                SelectedEntityDetails = new EntityDetailsViewModel(createdRow);
                if (SelectedTab == MainWindowTab.DependencyGraph)
                    DependencyGraph.ShowEntity(createdEntityId);
                else
                    EntityRevealRequested?.Invoke(createdEntityId);
            }

            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
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
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
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
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
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
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The overview could not be reloaded after entity restoration.");
            SetOverviewFailure(
                $"The entity was restored, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OnEntityPurgedAsync()
    {
        SelectedTab = MainWindowTab.Archived;
        try
        {
            await LoadOverviewAndProgressAsync(CancellationToken.None);
            PersistedStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The overview could not be reloaded after permanently deleting an entity.");
            SetOverviewFailure(
                $"The entity was permanently deleted, but persisted entities could not be reloaded: {exception.Message}");
        }
    }

    private async Task OpenOverviewEntityAsync(EntityOverviewRow row)
    {
        CloseEntityDetails();
        if (row.LifecycleState == EntityLifecycleState.Archived)
        {
            await Editor.BeginArchivedAsync(row.EntityId);
        }
        else
        {
            await Editor.BeginStandaloneAsync(row.EntityId);
        }
    }

    private void OpenEntityDetails(EntityOverviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        AssignmentGuidance = null;
        _historyReturnToArchivedEntityId = null;
        SelectedEntityDetails = new EntityDetailsViewModel(row);
        if (_developers is not null && _responsibilityPeriods is not null)
            _ = LoadResponsibilityDetailsAsync(SelectedEntityDetails);
    }

    public void SetSearchResponsibleNames(bool enabled)
    {
        ActiveTable.SearchResponsibleNames = enabled;
        ArchivedTable.SearchResponsibleNames = enabled;
    }

    private void ShowArchivedResponsibilityHistory()
    {
        EntityId? entityId = Editor.ArchivedDetails?.Entity.Id;
        if (!Editor.IsArchivedMode || entityId is null) return;
        EntityOverviewRow? row = ArchivedTable.SourceItems.FirstOrDefault(item =>
            item.EntityId == entityId);
        if (row is null) return;
        Editor.DiscardAndClose();
        OpenEntityDetails(row);
        _historyReturnToArchivedEntityId = entityId;
        SelectedEntityDetails?.ShowFullHistory();
    }

    private async Task BackFromResponsibilityHistoryAsync()
    {
        EntityId? archivedEntityId = _historyReturnToArchivedEntityId;
        if (archivedEntityId is null)
        {
            SelectedEntityDetails?.ShowSummary();
            return;
        }
        CloseEntityDetails();
        await Editor.BeginArchivedAsync(archivedEntityId);
    }

    private async Task LoadResponsibilityDetailsAsync(EntityDetailsViewModel details)
    {
        try
        {
            IReadOnlyList<ResponsibilityPeriod> periods =
                await _responsibilityPeriods!.GetByEntityAsync(details.EntityId);
            IReadOnlyList<ProjectDeveloper> developers =
                await _developers!.ListForTrackerAsync(_trackerId);
            if (!ReferenceEquals(SelectedEntityDetails, details)) return;
            details.SetResponsibility(periods, developers);
            ProjectDeveloper? self = _localIdentity is null ? null
                : await _localIdentity.ResolveForTrackerAsync(_trackerId);
            if (ReferenceEquals(SelectedEntityDetails, details))
                details.SetSelfAssigned(self is not null && periods.Any(period =>
                    period.IsCurrent && period.DeveloperId == self.Id));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Responsibility history could not be loaded.");
        }
    }

    public bool TryCloseEditor()
    {
        if (!Editor.IsOpen)
        {
            return true;
        }

        if (Editor.IsDirty && !_discardConfirmation.ConfirmDiscard(
                "This entity has unsaved changes."))
        {
            return false;
        }

        Editor.DiscardAndClose();
        return true;
    }

    public bool OpenEntityDetails(EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        EntityOverviewRow? row = OverviewItems.FirstOrDefault(item => item.EntityId == entityId);
        if (row is null)
        {
            return false;
        }

        OpenEntityDetails(row);
        return true;
    }

    public void CloseEntityDetails()
    {
        _historyReturnToArchivedEntityId = null;
        SelectedEntityDetails = null;
    }

    private async Task OpenArchivedFromCreationAsync(EntityId entityId)
    {
        ManualCreation.Reset();
        SelectedTab = MainWindowTab.Archived;
        await Editor.BeginArchivedAsync(entityId);
    }

    private void OnManualCreationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManualEntityCreationViewModel.IsBusy) or
            nameof(ManualEntityCreationViewModel.IsOpen))
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
            case nameof(EntityTableViewModel.SourceItems):
                _exportOverviewCommand.NotifyCanExecuteChanged();
                break;
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
        OnPropertyChanged(nameof(IsImplementedSummarySelected));
        OnPropertyChanged(nameof(IsNotStartedSummarySelected));
        OnPropertyChanged(nameof(IsInProgressSummarySelected));
        OnPropertyChanged(nameof(IsReworkNeededSummarySelected));
        OnPropertyChanged(nameof(IsReworkingSummarySelected));
        OnPropertyChanged(nameof(IsBlockedSummarySelected));
        OnPropertyChanged(nameof(IsDevelopmentCompletedSummarySelected));
        OnPropertyChanged(nameof(IsReconciledSummarySelected));
        OnPropertyChanged(nameof(IsNotRelevantSummarySelected));
    }

    internal static EntityOverviewRow CreateOverviewRow(EntityOverviewItem item)
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
            FormatStatus(item.Status),
            WorkStatusDisplayMapper.Format(WorkStatusDisplayMapper.From(item.WorkflowState)),
            item.DependencyCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            item.DependencyNames,
            item.DependencyResolutionIssueNames,
            FormatGraphIssueTitle(item.DependencyState),
            FormatGraphIssueDescription(item.DependencyState),
            FormatGraphIssueNames(item.DependencyResolutionIssueNames),
            isArchived ? "—" : FormatMissingDependencies(item.MissingDependencyNames),
            item.Notes,
            isArchived ? "View archived entity" : "Edit entity",
            item.RequestedPriority,
            item.Blockers,
            item.AuditTimestamps.CreatedAtUtc,
            item.AuditTimestamps.SchemaUpdatedAtUtc,
            item.AuditTimestamps.ProgressUpdatedAtUtc,
            item.CurrentDevelopers,
            item.FilterActive,
            item.SharedNotes);
    }

    private static string FormatStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.Reworking => "Reworking",
        DevelopmentStatus.NotRelevant => "Not relevant",
        DevelopmentStatus.Blocked => "Blocked",
        DevelopmentStatus.DevelopmentCompleted => "Completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string FormatPriority(int? priority) =>
        priority?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

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

    public void Dispose()
    {
        ActiveTable.ProjectionChanging -= OnActiveTableProjectionChanging;
        ArchivedTable.ProjectionChanging -= OnArchivedTableProjectionChanging;
        ActiveTable.PropertyChanged -= OnActiveTablePropertyChanged;
        ArchivedTable.PropertyChanged -= OnArchivedTablePropertyChanged;
        ManualCreation.PropertyChanged -= OnManualCreationPropertyChanged;
        Editor.PropertyChanged -= OnEditorPropertyChanged;
    }

    private void OnActiveTableProjectionChanging(object? sender, EventArgs e)
    {
        CloseEntityDetails();
        ClearOverviewSelection();
    }

    private void OnArchivedTableProjectionChanging(object? sender, EventArgs e) =>
        CloseEntityDetails();
}
