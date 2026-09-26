using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public enum ShellNotificationSeverity
{
    Information,
    Success,
    Warning,
    Error
}

public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly ITrackerRepository _trackerRepository;
    private readonly DashboardViewModelFactory _dashboardFactory;
    private readonly ProgressHistoryInitializer _historyInitializer;
    private readonly EntityTrackerSettingsStore _settingsStore;
    private readonly TrackerWorkspaceViewModelFactory _workspaceFactory;
    private readonly IContextDiscardConfirmation _discardConfirmation;
    private readonly IProjectRepositoryManager? _repositoryManager;
    private readonly IProjectSynchronizationService? _synchronizationService;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly Dictionary<TrackerId, MainWindowViewModel> _workspaces = [];
    private readonly Dictionary<ProjectId, ProjectDashboardViewModel> _projectDashboards = [];
    private readonly ProjectId? _startupProjectId;
    private readonly TrackerId? _startupTrackerId;
    private readonly AsyncCommand<ShellDestination> _navigateCommand;
    private ShellDestination _selectedDestination = ShellDestination.Portfolio;
    private Project? _selectedProject;
    private Tracker? _selectedTracker;
    private MainWindowViewModel? _currentWorkspace;
    private PortfolioDashboard? _portfolio;
    private ProjectDashboard? _projectDashboard;
    private ProjectDashboardViewModel? _projectReporting;
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private string? _notificationMessage;
    private ShellNotificationSeverity _notificationSeverity = ShellNotificationSeverity.Information;
    private bool _showDefaultNamePrompt;
    private ProjectRepositoryStatus? _activeRepositoryStatus;
    private CancellationTokenSource? _synchronizationCancellation;

    public ShellViewModel(
        IProjectRepository projectRepository,
        ITrackerRepository trackerRepository,
        DashboardViewModelFactory dashboardFactory,
        ProgressHistoryInitializer historyInitializer,
        EntityTrackerSettingsStore settingsStore,
        TrackerWorkspaceViewModelFactory workspaceFactory,
        IContextDiscardConfirmation discardConfirmation,
        CatalogManagementViewModel catalogManagement,
        AppearanceViewModel appearance,
        IClipboardService clipboard,
        EntityTrackerSettings initialSettings,
        IProjectRepositoryManager? repositoryManager = null,
        IProjectSynchronizationService? synchronizationService = null,
        ILogger<ShellViewModel>? logger = null)
    {
        _projectRepository = projectRepository;
        _trackerRepository = trackerRepository;
        _dashboardFactory = dashboardFactory;
        _historyInitializer = historyInitializer;
        _settingsStore = settingsStore;
        _workspaceFactory = workspaceFactory;
        _discardConfirmation = discardConfirmation;
        _repositoryManager = repositoryManager;
        _synchronizationService = synchronizationService;
        Catalog = catalogManagement;
        Appearance = appearance;
        Help = new SqlQueryHelpViewModel(
            clipboard,
            () => _ = NavigateAsync(ShellDestination.SchemaSynchronization));
        _startupProjectId = initialSettings.LastProjectId;
        _startupTrackerId = initialSettings.LastTrackerId;
        _logger = logger ?? NullLogger<ShellViewModel>.Instance;
        Projects = [];
        Trackers = [];
        PortfolioReporting = dashboardFactory.CreatePortfolio();
        NavigationItems =
        [
            new(ShellDestination.Portfolio, string.Empty, "Portfolio", false, false),
            new(ShellDestination.ProjectDashboard, string.Empty, "Project dashboard", true, false),
            new(ShellDestination.Overview, "Tracker", "Overview", true, true),
            new(ShellDestination.Archived, "Tracker", "Archived", true, true),
            new(ShellDestination.Reports, "Tracker", "Reports", true, true),
            new(ShellDestination.SchemaSynchronization, "Manage", "Schema synchronization", true, true),
            new(ShellDestination.AddEntity, "Manage", "Add entity", true, true),
            new(ShellDestination.HelpSql, "Utilities", "Help & SQL", false, false),
            new(ShellDestination.Settings, "Utilities", "Settings", false, false)
        ];
        _navigateCommand = new AsyncCommand<ShellDestination>(
            NavigateFromCommandAsync,
            destination => CanNavigate(NavigationItems.Single(item => item.Destination == destination)));
        Catalog.Changed += OnCatalogChanged;
        Catalog.SelectionRequested += OnCatalogSelectionRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Project> Projects { get; }

    public ObservableCollection<Tracker> Trackers { get; }

    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; }

    public CatalogManagementViewModel Catalog { get; }

    public AppearanceViewModel Appearance { get; }

    public SqlQueryHelpViewModel Help { get; }

    public PortfolioDashboardViewModel PortfolioReporting { get; }

    public ProjectDashboardViewModel? ProjectReporting
    {
        get => _projectReporting;
        private set => SetField(ref _projectReporting, value);
    }

    public PortfolioDashboard? Portfolio
    {
        get => _portfolio;
        private set
        {
            if (SetField(ref _portfolio, value))
            {
                OnPropertyChanged(nameof(HasPortfolioProjects));
            }
        }
    }

    public ProjectDashboard? ProjectDashboard
    {
        get => _projectDashboard;
        private set
        {
            if (SetField(ref _projectDashboard, value))
            {
                OnPropertyChanged(nameof(HasProjectTrackers));
            }
        }
    }

    public Project? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (SetField(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(ProjectContextName));
                OnPropertyChanged(nameof(ContextSummary));
                OnPropertyChanged(nameof(DefaultNamePromptMessage));
                OnPropertyChanged(nameof(DefaultNamePromptActionLabel));
                OnPropertyChanged(nameof(CanLinkRepository));
                OnPropertyChanged(nameof(CanLocateRepository));
                OnPropertyChanged(nameof(CanRebuildRepositoryCache));
                OnPropertyChanged(nameof(CanReadActiveProjectCache));
            }
        }
    }

    public Tracker? SelectedTracker
    {
        get => _selectedTracker;
        private set
        {
            if (SetField(ref _selectedTracker, value))
            {
                OnPropertyChanged(nameof(HasTracker));
                OnPropertyChanged(nameof(TrackerContextName));
                OnPropertyChanged(nameof(ContextSummary));
                OnPropertyChanged(nameof(DefaultNamePromptMessage));
                OnPropertyChanged(nameof(DefaultNamePromptActionLabel));
            }
        }
    }

    public MainWindowViewModel? CurrentWorkspace
    {
        get => _currentWorkspace;
        private set
        {
            if (SetField(ref _currentWorkspace, value))
            {
                OnPropertyChanged(nameof(HasWorkspace));
                OnPropertyChanged(nameof(IsTrackerWorkspace));
            }
        }
    }

    public ShellDestination SelectedDestination
    {
        get => _selectedDestination;
        private set
        {
            if (SetField(ref _selectedDestination, value))
            {
                OnPropertyChanged(nameof(IsPortfolio));
                OnPropertyChanged(nameof(IsProjectDashboard));
                OnPropertyChanged(nameof(IsOverview));
                OnPropertyChanged(nameof(IsArchived));
                OnPropertyChanged(nameof(IsReports));
                OnPropertyChanged(nameof(IsSchemaSynchronization));
                OnPropertyChanged(nameof(IsAddEntity));
                OnPropertyChanged(nameof(IsHelpSql));
                OnPropertyChanged(nameof(IsSettings));
                OnPropertyChanged(nameof(IsTrackerWorkspace));
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
                _navigateCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanSyncRepository));
                OnPropertyChanged(nameof(CanCancelSynchronization));
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public string? NotificationMessage
    {
        get => _notificationMessage;
        private set
        {
            if (SetField(ref _notificationMessage, value))
            {
                OnPropertyChanged(nameof(HasNotification));
            }
        }
    }

    public bool ShowDefaultNamePrompt
    {
        get => _showDefaultNamePrompt;
        private set => SetField(ref _showDefaultNamePrompt, value);
    }

    public ProjectRepositoryStatus? ActiveRepositoryStatus
    {
        get => _activeRepositoryStatus;
        private set
        {
            if (SetField(ref _activeRepositoryStatus, value))
            {
                OnPropertyChanged(nameof(RepositoryStatusText));
                OnPropertyChanged(nameof(RepositoryStatusDetails));
                OnPropertyChanged(nameof(HasRepositoryStatusDetails));
                OnPropertyChanged(nameof(CanLinkRepository));
                OnPropertyChanged(nameof(CanLocateRepository));
                OnPropertyChanged(nameof(CanRebuildRepositoryCache));
                OnPropertyChanged(nameof(CanUseActiveRepository));
                OnPropertyChanged(nameof(CanReadActiveProjectCache));
                OnPropertyChanged(nameof(ShowSyncRepository));
                OnPropertyChanged(nameof(CanSyncRepository));
                OnPropertyChanged(nameof(SyncDisabledReason));
                OnPropertyChanged(nameof(HasSyncDisabledReason));
                _navigateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ShellNotificationSeverity NotificationSeverity
    {
        get => _notificationSeverity;
        private set => SetField(ref _notificationSeverity, value);
    }

    public string DefaultNamePromptMessage =>
        SelectedTracker?.Name == "Default tracker"
            ? "Give the migrated default Tracker a name your team will recognize."
            : SelectedProject?.Name == "Default project"
                ? "Give the migrated default Project a name your team will recognize."
                : string.Empty;

    public string DefaultNamePromptActionLabel =>
        SelectedTracker?.Name == "Default tracker" ? "Rename tracker" : "Rename project";

    public bool HasProject => SelectedProject is not null;
    public bool HasTracker => SelectedTracker is not null;
    public bool HasWorkspace => CurrentWorkspace is not null;
    public bool IsTrackerWorkspace =>
        CurrentWorkspace is not null && IsTrackerDestination(SelectedDestination);
    public bool HasPortfolioProjects => Portfolio?.Projects.Count > 0;
    public bool HasProjectTrackers => ProjectDashboard?.Trackers.Count > 0;
    public bool HasNotification => !string.IsNullOrWhiteSpace(NotificationMessage);
    public string ProjectContextName => SelectedProject?.Name ?? "No project selected";
    public string TrackerContextName => SelectedTracker?.Name ?? "No tracker selected";
    public string ContextSummary => HasProject
        ? HasTracker ? $"{ProjectContextName} / {TrackerContextName}" : ProjectContextName
        : "Portfolio";
    public string RepositoryStatusText => ActiveRepositoryStatus?.Kind switch
    {
        ProjectRepositoryStatusKind.GitRegistered => "Git-backed · using SQLite cache",
        ProjectRepositoryStatusKind.GitClean => ActiveRepositoryStatus.SyncState switch
        {
            ProjectSyncState.NoUpstream => "Git-backed · local only",
            ProjectSyncState.UpToDate => "Git-backed · up to date",
            ProjectSyncState.Ahead => "Git-backed · local changes to push",
            ProjectSyncState.Behind => "Git-backed · upstream changes available",
            ProjectSyncState.MergeRequired => "Git-backed · merge required",
            _ => "Git-backed · sync required"
        },
        ProjectRepositoryStatusKind.Unavailable => "Git-backed · repository unavailable",
        ProjectRepositoryStatusKind.StaleCache => "Git-backed · cache rebuild required",
        ProjectRepositoryStatusKind.Blocked => "Git-backed · blocked",
        _ => "SQLite only"
    };
    public string RepositoryStatusDetails
    {
        get
        {
            if (ActiveRepositoryStatus is not { } status) return string.Empty;
            string location = status.RepositoryPath is null
                ? string.Empty
                : status.ManagedBranch is null
                    ? status.RepositoryPath
                    : $"{status.RepositoryPath} · branch {status.ManagedBranch}";
            string graph = status.AheadCount is null && status.BehindCount is null
                ? string.Empty
                : $"Ahead {status.AheadCount ?? 0} · behind {status.BehindCount ?? 0}";
            string upstream = status.Upstream is null ? string.Empty : $"Upstream {status.Upstream}";
            string fetched = FormatSyncTime("Last successful fetch", status.LastSuccessfulFetchAtUtc);
            string pushed = FormatSyncTime("Last successful push", status.LastSuccessfulPushAtUtc);
            return string.Join(Environment.NewLine,
                new[] { location, upstream, graph, fetched, pushed, status.Diagnostic }
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));
        }
    }
    public bool HasRepositoryStatusDetails => !string.IsNullOrWhiteSpace(RepositoryStatusDetails);
    public bool CanLinkRepository => HasProject &&
        ActiveRepositoryStatus?.Kind == ProjectRepositoryStatusKind.SQLiteOnly;
    public bool CanLocateRepository => HasProject && ActiveRepositoryStatus?.Kind is
        ProjectRepositoryStatusKind.Unavailable or ProjectRepositoryStatusKind.Blocked;
    public bool CanRebuildRepositoryCache => HasProject &&
        ActiveRepositoryStatus?.Kind == ProjectRepositoryStatusKind.StaleCache;
    public bool CanUseActiveRepository => ActiveRepositoryStatus?.CanUseProject != false;
    public bool CanReadActiveProjectCache => HasProject;
    public bool ShowSyncRepository => ActiveRepositoryStatus?.IsGitBacked == true;
    public bool CanSyncRepository => !IsBusy && _synchronizationService is not null &&
        ActiveRepositoryStatus is
        {
            Kind: ProjectRepositoryStatusKind.GitRegistered or ProjectRepositoryStatusKind.GitClean,
            SyncState: not ProjectSyncState.NoUpstream and not ProjectSyncState.MergeRequired
        };
    public bool CanCancelSynchronization => _synchronizationCancellation is not null;
    public string SyncDisabledReason => ActiveRepositoryStatus switch
    {
        { Kind: ProjectRepositoryStatusKind.StaleCache } => "Rebuild the SQLite cache before synchronizing.",
        { Kind: ProjectRepositoryStatusKind.Unavailable } => "Locate the repository before synchronizing.",
        { Kind: ProjectRepositoryStatusKind.Blocked } => "Resolve the repository validation problem before synchronizing.",
        { SyncState: ProjectSyncState.NoUpstream } => "No upstream is configured. Local commits continue to work without a remote.",
        { SyncState: ProjectSyncState.MergeRequired } => "The histories diverged. Semantic merge will be available in RS-04.",
        _ => string.Empty
    };
    public bool HasSyncDisabledReason => !string.IsNullOrWhiteSpace(SyncDisabledReason);

    public bool IsPortfolio => SelectedDestination == ShellDestination.Portfolio;
    public bool IsProjectDashboard => SelectedDestination == ShellDestination.ProjectDashboard;
    public bool IsOverview => SelectedDestination == ShellDestination.Overview;
    public bool IsArchived => SelectedDestination == ShellDestination.Archived;
    public bool IsReports => SelectedDestination == ShellDestination.Reports;
    public bool IsSchemaSynchronization => SelectedDestination == ShellDestination.SchemaSynchronization;
    public bool IsAddEntity => SelectedDestination == ShellDestination.AddEntity;
    public bool IsHelpSql => SelectedDestination == ShellDestination.HelpSql;
    public bool IsSettings => SelectedDestination == ShellDestination.Settings;

    public ICommand NavigateCommand => _navigateCommand;

    private async Task NavigateFromCommandAsync(ShellDestination destination) =>
        await NavigateAsync(destination);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ReloadCatalogAsync(cancellationToken);
        Project? startupProject = _startupProjectId is null
            ? null
            : Projects.FirstOrDefault(project => project.Id == _startupProjectId);
        Tracker? startupTracker = _startupTrackerId is null
            ? null
            : (await _trackerRepository.GetAsync(_startupTrackerId, cancellationToken));
        if (startupProject is not null && startupTracker is not null &&
            startupTracker.ProjectId == startupProject.Id &&
            startupTracker.LifecycleState == CatalogLifecycleState.Active)
        {
            await ApplyContextAsync(startupProject, startupTracker, ShellDestination.Overview, false, cancellationToken);
        }
        else if (startupProject is not null && _startupTrackerId is null)
        {
            await ApplyContextAsync(startupProject, null, ShellDestination.ProjectDashboard, false, cancellationToken);
        }
        else
        {
            await ApplyContextAsync(null, null, ShellDestination.Portfolio, true, cancellationToken);
        }
    }

    public async Task<bool> SelectProjectAsync(
        Project? project,
        CancellationToken cancellationToken = default)
    {
        if (project?.Id == SelectedProject?.Id)
        {
            return true;
        }

        if (!ConfirmLeavingDirtyWorkspace())
        {
            return false;
        }

        return await ApplyContextAsync(
            project,
            null,
            project is null ? ShellDestination.Portfolio : ShellDestination.ProjectDashboard,
            true,
            cancellationToken);
    }

    public async Task<bool> SelectTrackerAsync(
        Tracker? tracker,
        CancellationToken cancellationToken = default)
    {
        if (tracker?.Id == SelectedTracker?.Id)
        {
            return true;
        }

        if (tracker is not null && SelectedProject?.Id != tracker.ProjectId)
        {
            return false;
        }

        if (!ConfirmLeavingDirtyWorkspace())
        {
            return false;
        }

        ShellDestination destination = tracker is null
            ? ShellDestination.ProjectDashboard
            : IsTrackerDestination(SelectedDestination)
                ? SelectedDestination
                : ShellDestination.Overview;
        return await ApplyContextAsync(SelectedProject, tracker, destination, true, cancellationToken);
    }

    public async Task<bool> NavigateAsync(
        ShellDestination destination,
        CancellationToken cancellationToken = default)
    {
        ShellNavigationItem item = NavigationItems.Single(item => item.Destination == destination);
        if (!CanNavigate(item) || destination == SelectedDestination)
        {
            return destination == SelectedDestination;
        }

        if (WouldLeaveDirtyFlow(destination) && !ConfirmLeavingDirtyWorkspace())
        {
            return false;
        }

        CurrentWorkspace?.PrepareForDeactivation();
        SetDestination(destination);
        if (destination is ShellDestination.Portfolio or ShellDestination.ProjectDashboard)
        {
            await RefreshDashboardsAsync(cancellationToken);
        }

        return true;
    }

    public async Task OpenProjectAsync(ProjectId projectId)
    {
        Project? project = Projects.FirstOrDefault(item => item.Id == projectId);
        if (project is not null && await SelectProjectAsync(project))
        {
            await NavigateAsync(ShellDestination.ProjectDashboard);
        }
    }

    public async Task OpenRepositoryAsync(string repositoryPath)
    {
        if (_repositoryManager is null) return;
        await RunRepositoryActionAsync("Opening repository…", async cancellationToken =>
        {
            ProjectId projectId = await _repositoryManager.OpenAsync(repositoryPath, cancellationToken);
            await ReloadCatalogAsync(cancellationToken);
            Project project = Projects.Single(item => item.Id == projectId);
            if (await ApplyContextAsync(project, null, ShellDestination.ProjectDashboard, true, cancellationToken))
                SetNotification($"Opened Git-backed Project “{project.Name}”.", ShellNotificationSeverity.Success);
        });
    }

    public async Task LinkSelectedProjectAsync(string repositoryPath)
    {
        if (_repositoryManager is null || SelectedProject is not { } project) return;
        await RunRepositoryActionAsync("Linking repository…", async cancellationToken =>
        {
            await _repositoryManager.LinkAsync(project.Id, repositoryPath, cancellationToken);
            await RefreshRepositoryStatusAsync(cancellationToken);
            await RefreshDashboardsAsync(cancellationToken);
            SetNotification(
                $"“{project.Name}” is now Git-backed. Changes create local commits.",
                ShellNotificationSeverity.Success);
        });
    }

    public async Task LocateSelectedRepositoryAsync(string repositoryPath)
    {
        if (_repositoryManager is null || SelectedProject is not { } project) return;
        await RunRepositoryActionAsync("Locating repository…", async cancellationToken =>
        {
            await _repositoryManager.LocateAsync(project.Id, repositoryPath, cancellationToken);
            await RefreshRepositoryStatusAsync(cancellationToken);
            SetNotification("Repository location updated.", ShellNotificationSeverity.Success);
        });
    }

    public async Task RebuildSelectedRepositoryCacheAsync()
    {
        if (_repositoryManager is null || SelectedProject is not { } project) return;
        await RunRepositoryActionAsync("Rebuilding local cache…", async cancellationToken =>
        {
            await _repositoryManager.RebuildCacheAsync(project.Id, cancellationToken);
            await ReloadCatalogAsync(cancellationToken);
            Project refreshed = Projects.Single(item => item.Id == project.Id);
            if (await ApplyContextAsync(refreshed, null, ShellDestination.ProjectDashboard, true, cancellationToken))
                SetNotification(
                    "SQLite cache rebuilt from repository HEAD.",
                    ShellNotificationSeverity.Success);
        });
    }

    public async Task SyncSelectedProjectAsync()
    {
        if (!CanSyncRepository || _synchronizationService is null || SelectedProject is not { } project)
            return;

        _synchronizationCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(CanCancelSynchronization));
        IsBusy = true;
        BusyMessage = "Synchronizing Project…";
        try
        {
            ProjectSyncResult result = await _synchronizationService.SyncAsync(
                project.Id, _synchronizationCancellation.Token);
            _logger.LogInformation(
                "Project synchronization completed with outcome {Outcome} and failure kind {FailureKind}.",
                result.Outcome,
                result.FailureKind);
            ActiveRepositoryStatus = result.Status;
            SetNotification(result.Message, result.Outcome switch
            {
                ProjectSyncOutcome.UpToDate or
                ProjectSyncOutcome.Pushed or
                ProjectSyncOutcome.FastForwarded => ShellNotificationSeverity.Success,
                ProjectSyncOutcome.MergeRequired or
                ProjectSyncOutcome.NeedsSync => ShellNotificationSeverity.Warning,
                ProjectSyncOutcome.MissingUpstream or
                ProjectSyncOutcome.Cancelled => ShellNotificationSeverity.Information,
                _ => ShellNotificationSeverity.Error
            });
            if (result.Outcome == ProjectSyncOutcome.FastForwarded &&
                result.Status.Kind == ProjectRepositoryStatusKind.GitClean)
            {
                TrackerId? trackerId = SelectedTracker?.Id;
                ShellDestination destination = SelectedDestination;
                await ReloadCatalogAsync(CancellationToken.None);
                Project? refreshedProject = Projects.FirstOrDefault(item => item.Id == project.Id);
                Tracker? refreshedTracker = trackerId is null
                    ? null
                    : await _trackerRepository.GetAsync(trackerId, CancellationToken.None);
                if (refreshedTracker?.ProjectId != project.Id ||
                    refreshedTracker.LifecycleState != CatalogLifecycleState.Active)
                    refreshedTracker = null;
                await ApplyContextAsync(
                    refreshedProject,
                    refreshedTracker,
                    refreshedTracker is null && IsTrackerDestination(destination)
                        ? ShellDestination.ProjectDashboard
                        : destination,
                    true,
                    CancellationToken.None);
            }
            else
            {
                await RefreshDashboardsAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Project synchronization failed.");
            SetNotification(
                $"Synchronization could not be completed: {exception.Message}",
                ShellNotificationSeverity.Error);
            await RefreshRepositoryStatusAsync(CancellationToken.None);
        }
        finally
        {
            _synchronizationCancellation.Dispose();
            _synchronizationCancellation = null;
            IsBusy = false;
            BusyMessage = string.Empty;
            OnPropertyChanged(nameof(CanCancelSynchronization));
        }
    }

    public void CancelSynchronization() => _synchronizationCancellation?.Cancel();

    public async Task OpenTrackerAsync(TrackerId trackerId)
    {
        Tracker? tracker = Trackers.FirstOrDefault(item => item.Id == trackerId);
        if (tracker is not null)
        {
            await SelectTrackerAsync(tracker);
            await NavigateAsync(ShellDestination.Overview);
        }
    }

    public async Task<bool> OpenComparisonCellAsync(ProjectComparisonCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (!cell.IsPresent || cell.EntityId is null)
        {
            return false;
        }

        Tracker? tracker = Trackers.FirstOrDefault(item => item.Id == cell.TrackerId);
        if (tracker is null || !await SelectTrackerAsync(tracker))
        {
            return false;
        }

        if (!await NavigateAsync(ShellDestination.Overview))
        {
            return false;
        }

        return CurrentWorkspace?.OpenEntityDetails(cell.EntityId) == true;
    }

    public void DismissDefaultNamePrompt() => ShowDefaultNamePrompt = false;

    public void DismissNotification() => NotificationMessage = null;

    private bool CanNavigate(ShellNavigationItem item) =>
        !IsBusy && (!item.RequiresProject || HasProject) && (!item.RequiresTracker || HasTracker);

    private bool ConfirmLeavingDirtyWorkspace()
    {
        if (CurrentWorkspace?.HasUnsavedWork != true)
        {
            return true;
        }

        if (!_discardConfirmation.ConfirmDiscard(
                "The current tracker has an unfinished edit, entity creation, or synchronization review."))
        {
            return false;
        }

        CurrentWorkspace.DiscardTransientWork();
        return true;
    }

    private bool WouldLeaveDirtyFlow(ShellDestination destination) =>
        CurrentWorkspace?.HasUnsavedWork == true && destination != SelectedDestination;

    private async Task<bool> ApplyContextAsync(
        Project? project,
        Tracker? tracker,
        ShellDestination destination,
        bool persist,
        CancellationToken cancellationToken)
    {
        Project? previousProject = SelectedProject;
        Tracker? previousTracker = SelectedTracker;
        MainWindowViewModel? previousWorkspace = CurrentWorkspace;
        ShellDestination previousDestination = SelectedDestination;
        ProjectRepositoryStatus? previousRepositoryStatus = ActiveRepositoryStatus;
        Tracker[] previousTrackers = Trackers.ToArray();
        IsBusy = true;
        BusyMessage = tracker is null ? "Loading project context…" : "Loading tracker workspace…";
        try
        {
            CurrentWorkspace?.PrepareForDeactivation();
            ActiveRepositoryStatus = project is null
                ? null
                : _repositoryManager is null
                    ? new ProjectRepositoryStatus(project.Id, ProjectRepositoryStatusKind.SQLiteOnly)
                    : await _repositoryManager.GetCachedStatusAsync(project.Id, cancellationToken);
            Tracker[] availableTrackers = project is null
                ? []
                : (await _trackerRepository.GetByProjectAsync(project.Id, cancellationToken))
                    .Where(static item => item.LifecycleState == CatalogLifecycleState.Active)
                    .ToArray();
            Tracker? selectedTracker = tracker is null
                ? null
                : availableTrackers.FirstOrDefault(item => item.Id == tracker.Id);
            if (tracker is not null && selectedTracker is null)
            {
                throw new InvalidOperationException(
                    "The selected tracker is not active in the selected project.");
            }
            SelectedProject = project;
            Replace(Trackers, availableTrackers);
            SelectedTracker = selectedTracker;

            if (selectedTracker is null)
            {
                CurrentWorkspace = null;
            }
            else
            {
                await _historyInitializer.EnsureInitializedAsync(selectedTracker.Id, cancellationToken);
                if (!_workspaces.TryGetValue(selectedTracker.Id, out MainWindowViewModel? workspace))
                {
                    workspace = _workspaceFactory.Create(selectedTracker.Id);
                    workspace.PersistedStateChanged += OnWorkspacePersistedStateChanged;
                    workspace.PropertyChanged += OnWorkspacePropertyChanged;
                    try
                    {
                        await workspace.InitializeAsync(cancellationToken);
                        _workspaces.Add(selectedTracker.Id, workspace);
                    }
                    catch
                    {
                        workspace.PersistedStateChanged -= OnWorkspacePersistedStateChanged;
                        workspace.PropertyChanged -= OnWorkspacePropertyChanged;
                        workspace.Dispose();
                        throw;
                    }
                }
                else
                {
                    await workspace.RefreshAsync(cancellationToken);
                }

                CurrentWorkspace = workspace;
            }

            SetDestination(destination);
            await RefreshDashboardsAsync(cancellationToken);
            ShowDefaultNamePrompt =
                project?.Name == "Default project" || selectedTracker?.Name == "Default tracker";
            if (persist)
            {
                await _settingsStore.SaveActiveContextAsync(
                    project?.Id,
                    selectedTracker?.Id,
                    cancellationToken);
            }

            _navigateCommand.NotifyCanExecuteChanged();
            return true;
        }
        catch (Exception exception)
        {
            SelectedProject = previousProject;
            SelectedTracker = previousTracker;
            CurrentWorkspace = previousWorkspace;
            ActiveRepositoryStatus = previousRepositoryStatus;
            Replace(Trackers, previousTrackers);
            SetDestination(previousDestination);
            _logger.LogError(exception, "Application context could not be changed.");
            SetNotification(
                $"The application context could not be changed: {exception.Message}",
                ShellNotificationSeverity.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private void SetDestination(ShellDestination destination)
    {
        SelectedDestination = destination;
        if (CurrentWorkspace is not null)
        {
            CurrentWorkspace.SelectedTab = destination switch
            {
                ShellDestination.Overview => MainWindowTab.Overview,
                ShellDestination.Archived => MainWindowTab.Archived,
                ShellDestination.Reports => MainWindowTab.Reports,
                ShellDestination.SchemaSynchronization => MainWindowTab.SchemaSynchronization,
                ShellDestination.AddEntity => MainWindowTab.AddEntity,
                _ => CurrentWorkspace.SelectedTab
            };
        }
    }

    private async Task ReloadCatalogAsync(CancellationToken cancellationToken)
    {
        Replace(Projects, (await _projectRepository.GetAllAsync(cancellationToken))
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Active));
        await RefreshDashboardsAsync(cancellationToken);
    }

    private async Task RefreshDashboardsAsync(CancellationToken cancellationToken)
    {
        await PortfolioReporting.RefreshAsync(cancellationToken);
        Portfolio = PortfolioReporting.Dashboard;
        if (SelectedProject is null)
        {
            ProjectReporting = null;
            ProjectDashboard = null;
            return;
        }

        if (!_projectDashboards.TryGetValue(
                SelectedProject.Id,
                out ProjectDashboardViewModel? projectDashboard))
        {
            projectDashboard = _dashboardFactory.CreateProject(SelectedProject.Id);
            _projectDashboards.Add(SelectedProject.Id, projectDashboard);
        }

        await projectDashboard.RefreshAsync(cancellationToken);
        ProjectReporting = projectDashboard;
        ProjectDashboard = projectDashboard.Dashboard;
    }

    private async Task RefreshRepositoryStatusAsync(CancellationToken cancellationToken)
    {
        ActiveRepositoryStatus = SelectedProject is null
            ? null
            : _repositoryManager is null
                ? new ProjectRepositoryStatus(SelectedProject.Id, ProjectRepositoryStatusKind.SQLiteOnly)
                : await _repositoryManager.GetStatusAsync(SelectedProject.Id, cancellationToken);
    }

    private async Task RunRepositoryActionAsync(
        string busyMessage,
        Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyMessage = busyMessage;
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or
                                          IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Project repository action failed.");
            SetNotification(
                $"Repository action could not be completed: {exception.Message}",
                ShellNotificationSeverity.Error);
            await RefreshRepositoryStatusAsync(CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private void SetNotification(string message, ShellNotificationSeverity severity)
    {
        NotificationSeverity = severity;
        NotificationMessage = message;
    }

    private async void OnWorkspacePersistedStateChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshDashboardsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Dashboard summaries could not be refreshed.");
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedTab) ||
            sender is not MainWindowViewModel workspace ||
            !ReferenceEquals(workspace, CurrentWorkspace))
        {
            return;
        }

        ShellDestination destination = workspace.SelectedTab switch
        {
            MainWindowTab.Overview => ShellDestination.Overview,
            MainWindowTab.Archived => ShellDestination.Archived,
            MainWindowTab.Reports => ShellDestination.Reports,
            MainWindowTab.SchemaSynchronization => ShellDestination.SchemaSynchronization,
            MainWindowTab.AddEntity => ShellDestination.AddEntity,
            _ => SelectedDestination
        };

        if (IsTrackerDestination(destination))
        {
            SelectedDestination = destination;
        }
    }

    private async void OnCatalogChanged(object? sender, EventArgs e)
    {
        await ReloadAfterCatalogChangeAsync();
    }

    private async Task ReloadAfterCatalogChangeAsync()
    {
        ProjectId? selectedProjectId = SelectedProject?.Id;
        TrackerId? selectedTrackerId = SelectedTracker?.Id;
        await ReloadCatalogAsync(CancellationToken.None);
        Project? project = selectedProjectId is null
            ? null
            : Projects.FirstOrDefault(item => item.Id == selectedProjectId);
        Tracker? candidate = selectedTrackerId is null || project is null
            ? null
            : await _trackerRepository.GetAsync(selectedTrackerId);
        Tracker? tracker = candidate?.LifecycleState == CatalogLifecycleState.Active
            ? candidate
            : null;
        if (project is null && selectedProjectId is not null)
        {
            await ApplyContextAsync(null, null, ShellDestination.Portfolio, true, CancellationToken.None);
        }
        else if (selectedTrackerId is not null && tracker is null)
        {
            await ApplyContextAsync(project, null, ShellDestination.ProjectDashboard, true, CancellationToken.None);
        }
        else
        {
            await ApplyContextAsync(project, tracker, SelectedDestination, true, CancellationToken.None);
        }
    }

    private async void OnCatalogSelectionRequested(object? sender, CatalogSelectionRequestedEventArgs e)
    {
        await ReloadCatalogAsync(CancellationToken.None);
        Project? project = Projects.FirstOrDefault(item => item.Id == e.ProjectId);
        Tracker? tracker = e.TrackerId is null
            ? null
            : await _trackerRepository.GetAsync(e.TrackerId);
        await ApplyContextAsync(
            project,
            tracker,
            tracker is null ? ShellDestination.ProjectDashboard : ShellDestination.Overview,
            true,
            CancellationToken.None);
    }

    private static bool IsTrackerDestination(ShellDestination destination) => destination is
        ShellDestination.Overview or
        ShellDestination.Archived or
        ShellDestination.Reports or
        ShellDestination.SchemaSynchronization or
        ShellDestination.AddEntity;

    private static string FormatSyncTime(string label, DateTimeOffset? timestamp) =>
        timestamp is null ? string.Empty : $"{label}: {timestamp.Value.ToLocalTime():g}";

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (T item in items)
        {
            collection.Add(item);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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
        _synchronizationCancellation?.Cancel();
        _synchronizationCancellation?.Dispose();
        Catalog.Changed -= OnCatalogChanged;
        Catalog.SelectionRequested -= OnCatalogSelectionRequested;
        foreach (MainWindowViewModel workspace in _workspaces.Values)
        {
            workspace.PersistedStateChanged -= OnWorkspacePersistedStateChanged;
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }
    }
}
