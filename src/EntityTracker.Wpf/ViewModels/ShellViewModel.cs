using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Application.History;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntityTracker.Application.Diagnostics;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly ITrackerRepository _trackerRepository;
    private readonly ProjectDeveloperService? _developerService;
    private readonly LocalProjectIdentitySettingsViewModel? _localIdentitySettings;
    private readonly DashboardViewModelFactory _dashboardFactory;
    private readonly ProgressHistoryInitializer _historyInitializer;
    private readonly EntityTrackerSettingsStore _settingsStore;
    private readonly TrackerWorkspaceViewModelFactory _workspaceFactory;
    private readonly IContextDiscardConfirmation _discardConfirmation;
    private readonly ProjectGitSyncService? _gitSync;
    // A Project import waiting for a local name; its retry continues this notice.
    private NotificationItem? _importNotice;
    private readonly ProjectAutoSyncService? _autoSync;
    private readonly WpfProjectUnsavedEditsGate? _syncEditGate;
    private readonly ProjectSyncIndicators? _syncIndicators;
    private string? _availableUpdateVersion;
    private Func<Task>? _update;
    // Kept so the refresh timer isn't collected.
    private readonly DispatcherTimer? _syncIndicatorTimer;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly Dictionary<TrackerId, MainWindowViewModel> _workspaces = [];
    private readonly Dictionary<ProjectId, ProjectDashboardViewModel> _projectDashboards = [];
    private readonly ProjectId? _startupProjectId;
    private readonly TrackerId? _startupTrackerId;
    private readonly AsyncCommand<ShellDestination> _navigateCommand;
    private ShellDestination _selectedDestination = ShellDestination.Portfolio;
    private SettingsCategory _selectedSettingsCategory = SettingsCategory.General;
    private Project? _settingsProject;
    private Project? _selectedProject;
    private Tracker? _selectedTracker;
    private MainWindowViewModel? _currentWorkspace;
    private PortfolioDashboard? _portfolio;
    private ProjectDashboard? _projectDashboard;
    private ProjectDashboardViewModel? _projectReporting;
    private ProjectDevelopersViewModel? _developers;
    private IReadOnlyList<ProjectSyncLink> _pendingProjectDeletions = [];
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private bool _showDefaultNamePrompt;

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
        EntityTrackerSettings initialSettings,
        ILogger<ShellViewModel>? logger = null,
        ProjectGitSyncService? gitSync = null,
        WpfProjectUnsavedEditsGate? syncEditGate = null,
        NotificationCenter? notifications = null,
        AutoSyncSettingsViewModel? autoSyncSettings = null,
        ProjectAutoSyncService? autoSync = null,
        ProjectDeveloperService? developerService = null,
        ResponsibilitySearchSettingsViewModel? responsibilitySearch = null,
        LocalProjectIdentitySettingsViewModel? localIdentitySettings = null,
        OverviewExportSettingsViewModel? overviewExport = null,
        DependencyGraphSettingsViewModel? graphSettings = null,
        EntityTracker.Reporting.ProjectReports.ProjectReportBuilder? reportBuilder = null,
        IProjectReportFiles? reportFiles = null,
        DiagnosticsSettingsViewModel? diagnostics = null,
        ProjectSyncIndicators? syncIndicators = null)
    {
        Diagnostics = diagnostics;
        _reportBuilder = reportBuilder;
        _reportFiles = reportFiles;
        _projectRepository = projectRepository;
        _trackerRepository = trackerRepository;
        _developerService = developerService;
        _localIdentitySettings = localIdentitySettings;
        _dashboardFactory = dashboardFactory;
        _historyInitializer = historyInitializer;
        _settingsStore = settingsStore;
        _workspaceFactory = workspaceFactory;
        _discardConfirmation = discardConfirmation;
        _gitSync = gitSync;
        _autoSync = autoSync;
        _syncEditGate = syncEditGate;
        Notifications = notifications ?? new NotificationCenter();
        Notifications.NavigateToProjectAsync = OpenProjectAsync;
        if (_autoSync is not null) _autoSync.StateChanged += OnAutoSyncStateChanged;
        _syncEditGate?.Attach(this);
        _syncIndicators = syncIndicators;
        if (_syncIndicators is not null)
        {
            _syncIndicators.Changed += (_, _) => OnSelectedProjectSyncIndicatorChanged();
            // Also catches changes made outside a Tracker workspace, such as developers or Trackers,
            // and keeps "last synced" times current.
            _syncIndicatorTimer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background,
                (_, _) => RefreshSyncIndicators(), Dispatcher.CurrentDispatcher);
            RefreshSyncIndicators();
        }
        SyncSelectedProjectCommand = new AsyncCommand(SyncSelectedProjectAsync);
        UpdateAppCommand = new AsyncCommand(() => _update?.Invoke() ?? Task.CompletedTask);
        Catalog = catalogManagement;
        Appearance = appearance;
        AutoSync = autoSyncSettings;
        ResponsibilitySearch = responsibilitySearch;
        OverviewExport = overviewExport;
        if (ResponsibilitySearch is not null)
            ResponsibilitySearch.Changed += OnResponsibilitySearchChanged;
        GraphSettings = graphSettings;
        if (GraphSettings is not null)
            GraphSettings.Changed += OnGraphSettingsChanged;
        _startupProjectId = initialSettings.LastProjectId;
        _startupTrackerId = initialSettings.LastTrackerId;
        _logger = logger ?? NullLogger<ShellViewModel>.Instance;
        Projects = [];
        Trackers = [];
        PortfolioReporting = dashboardFactory.CreatePortfolio();
        NavigationItems =
        [
            new(ShellDestination.Portfolio, string.Empty, "Portfolio", false, false,
                IconKey: "Icon.Grid"),
            new(ShellDestination.ProjectDashboard, string.Empty, "Project dashboard", true, false,
                IconKey: "Icon.Board", DisabledHint: "Select a Project to open its dashboard."),
            new(ShellDestination.Developers, string.Empty, "Developers", true, false, InSidebar: false),
            new(ShellDestination.ProjectReport, string.Empty, "Project report", true, false, InSidebar: false),
            new(ShellDestination.Overview, "Tracker", "Overview", true, true,
                IconKey: "Icon.Table", DisabledHint: "Select a Tracker to open Overview.", Shortcut: "Ctrl+1"),
            new(ShellDestination.DependencyGraph, "Tracker", "Dependency graph", true, true,
                IconKey: "Icon.Flowchart", DisabledHint: "Select a Tracker to view its dependency graph.",
                Shortcut: "Ctrl+2"),
            new(ShellDestination.Archived, "Tracker", "Archived", true, true,
                IconKey: "Icon.Archive", DisabledHint: "Select a Tracker to view archived entities.",
                Shortcut: "Ctrl+3"),
            new(ShellDestination.Help, "Utilities", "Help", false, false, IconKey: "Icon.QuestionCircle"),
            new(ShellDestination.Settings, "Utilities", "Settings", false, false, IconKey: "Icon.Settings")
        ];
        SidebarItems = NavigationItems
            .Where(static item => item.InSidebar && item.Group != "Utilities")
            .Select(static item => new ShellNavigationEntry(item))
            .ToArray();
        SidebarGroups = new ListCollectionView(SidebarItems.ToList());
        SidebarGroups.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShellNavigationEntry.Group)));
        UtilityItems = NavigationItems
            .Where(static item => item.InSidebar && item.Group == "Utilities")
            .Select(static item => new ShellNavigationEntry(item))
            .ToArray();
        _navigateCommand = new AsyncCommand<ShellDestination>(
            NavigateFromCommandAsync,
            destination => CanNavigate(NavigationItems.Single(item => item.Destination == destination)));
        RefreshNavigationEntries();
        Catalog.Changed += OnCatalogChanged;
        Catalog.PropertyChanged += OnModalSourceChanged;
        Catalog.SelectionRequested += OnCatalogSelectionRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Project> Projects { get; }

    public ObservableCollection<Tracker> Trackers { get; }

    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; }

    /// <summary>The sidebar's main list: Portfolio, Project dashboard and the Tracker pages.</summary>
    public IReadOnlyList<ShellNavigationEntry> SidebarItems { get; }

    /// <summary><see cref="SidebarItems"/> grouped by section, for the sidebar's headings.</summary>
    public ListCollectionView SidebarGroups { get; }

    /// <summary>The sidebar's list pinned to the bottom: Help and Settings.</summary>
    public IReadOnlyList<ShellNavigationEntry> UtilityItems { get; }

    /// <summary>
    /// The main list's current location; Developers and Project report show the Project dashboard.
    /// Choosing another entry navigates there, and the list goes back if navigation is refused.
    /// </summary>
    public ShellNavigationEntry? SelectedSidebarEntry
    {
        get => SidebarItems.FirstOrDefault(static entry => entry.IsSelected);
        set => ChooseSidebarEntry(value);
    }

    /// <summary>The utilities list's current location, if any.</summary>
    public ShellNavigationEntry? SelectedUtilityEntry
    {
        get => UtilityItems.FirstOrDefault(static entry => entry.IsSelected);
        set => ChooseSidebarEntry(value);
    }
    public IReadOnlyList<ProjectSyncLink> PendingProjectDeletions => _pendingProjectDeletions;

    public string AppVersion => AppVersionFormatter.ForAssembly(typeof(App).Assembly);

    /// <summary>The newer minor or patch version that can be installed, such as "3.7.1"; null when none.</summary>
    public string? AvailableUpdateVersion
    {
        get => _availableUpdateVersion;
        private set
        {
            if (!SetField(ref _availableUpdateVersion, value)) return;
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
    }

    public bool IsUpdateAvailable => _availableUpdateVersion is not null;

    /// <summary>Settings › About's Update button: closes EntityTracker and installs the available version.</summary>
    public ICommand UpdateAppCommand { get; }

    /// <summary>
    /// Offers a newer minor or patch version (null when there is none): Settings › About shows it with
    /// Update, and the Settings item in the sidebar carries a dot until it is installed.
    /// </summary>
    public void OfferUpdate(string? version, Func<Task>? update)
    {
        _update = version is null ? null : update;
        AvailableUpdateVersion = version;
        if (UtilityItems.FirstOrDefault(static entry => entry.Destination == ShellDestination.Settings) is { } settings)
            settings.Badge = version is null ? null : $"EntityTracker {version} is available";
    }

    /// <summary>Release notes for Settings › About and the required-update screen.</summary>
    public WhatsNewViewModel WhatsNew { get; } = new();

    /// <summary>Opens Settings › About, where What's new is listed.</summary>
    public async Task OpenWhatsNewAsync()
    {
        SelectedSettingsCategory = SettingsCategory.About;
        await NavigateAsync(ShellDestination.Settings);
    }

    public CatalogManagementViewModel Catalog { get; }

    public AppearanceViewModel Appearance { get; }
    public AutoSyncSettingsViewModel? AutoSync { get; }
    public ResponsibilitySearchSettingsViewModel? ResponsibilitySearch { get; }

    /// <summary>Gets Settings › About's log folder and detailed logging switch.</summary>
    public DiagnosticsSettingsViewModel? Diagnostics { get; }
    public DependencyGraphSettingsViewModel? GraphSettings { get; }
    public OverviewExportSettingsViewModel? OverviewExport { get; }
    public LocalProjectIdentitySettingsViewModel? LocalIdentity => _localIdentitySettings;
    public IReadOnlyList<SettingsCategory> SettingsCategories { get; } = Enum.GetValues<SettingsCategory>();

    public SettingsCategory SelectedSettingsCategory
    {
        get => _selectedSettingsCategory;
        set => SetField(ref _selectedSettingsCategory, value);
    }

    /// <summary>The Project whose local settings are edited; independent of the current context.</summary>
    public Project? SettingsProject
    {
        get => _settingsProject;
        set
        {
            if (SetField(ref _settingsProject, value) && _localIdentitySettings is not null)
                _ = _localIdentitySettings.SetProjectAsync(value?.Id, value?.Name);
        }
    }

    public NotificationCenter Notifications { get; }
    public bool HasActiveProjectSync => _autoSync?.HasActiveSync == true;

    /// <summary>Whether a linked Project has changes on this computer that are not in its repository yet.</summary>
    public async Task<bool> HasUnsyncedProjectChangesAsync(CancellationToken token = default) =>
        _syncIndicators is not null && (await _syncIndicators.ReadCurrentAsync(token)).Values
            .Any(static indicator => indicator.Kind == ProjectSyncIndicatorKind.Unsynced);

    /// <summary>
    /// Syncs every linked Project before the app closes. Returns
    /// null when nothing is left to sync, otherwise why a Project could not be synced, for the window to
    /// stay open with.
    /// </summary>
    public async Task<string?> SyncBeforeClosingAsync(CancellationToken token = default)
    {
        if (_autoSync is null || _syncIndicators is null) return null;
        await _autoSync.RunPassAsync(token);
        IReadOnlyDictionary<Guid, ProjectSyncIndicator> indicators = await _syncIndicators.ReadCurrentAsync(token);
        RefreshSyncIndicators();
        return DescribeUnsyncedProjects(indicators,
            id => Projects.FirstOrDefault(project => project.Id.Value == id)?.Name,
            id => _autoSync.GetState(new ProjectId(id)).LastResult);
    }

    /// <summary>
    /// Says which Projects are still not synced and why (the last sync result, else the icon's sentence),
    /// or null when every linked Project is synced.
    /// </summary>
    internal static string? DescribeUnsyncedProjects(IReadOnlyDictionary<Guid, ProjectSyncIndicator> indicators,
        Func<Guid, string?> nameOf, Func<Guid, string?> lastResultOf)
    {
        string[] problems = indicators
            .Where(static pair => pair.Value.Kind is not (ProjectSyncIndicatorKind.Synced or ProjectSyncIndicatorKind.None))
            .OrderBy(pair => nameOf(pair.Key), StringComparer.CurrentCulture)
            .Select(pair => $"“{nameOf(pair.Key) ?? "A Project"}”: " +
                            (lastResultOf(pair.Key) is { Length: > 0 } result ? result : pair.Value.Label))
            .ToArray();
        return problems.Length == 0
            ? null
            : $"Not every Project could be synced. {string.Join(" ", problems)}";
    }

    public PortfolioDashboardViewModel PortfolioReporting { get; }

    public ProjectDashboardViewModel? ProjectReporting
    {
        get => _projectReporting;
        private set => SetField(ref _projectReporting, value);
    }

    private readonly EntityTracker.Reporting.ProjectReports.ProjectReportBuilder? _reportBuilder;
    private readonly IProjectReportFiles? _reportFiles;
    private ProjectReportViewModel? _projectReport;

    public ProjectDevelopersViewModel? Developers
    {
        get => _developers;
        private set
        {
            ProjectDevelopersViewModel? previous = _developers;
            if (!SetField(ref _developers, value)) return;
            if (previous is not null) previous.PropertyChanged -= OnModalSourceChanged;
            if (value is not null) value.PropertyChanged += OnModalSourceChanged;
            OnModalStateChanged();
        }
    }

    /// <summary>
    /// Whether a dialog is open on the current page: the catalog dialogs, the entity editor or New entity
    /// overlay on a Tracker page, or a Developers dialog. While one is, the sidebar and header are dimmed
    /// and can't be used, so the dialog is finished or cancelled before anything else happens.
    /// </summary>
    public bool IsModalOpen =>
        Catalog.IsOpen ||
        IsTrackerWorkspace && (CurrentWorkspace!.Editor.IsOpen || CurrentWorkspace.ManualCreation.IsOpen) ||
        IsDevelopers && (Developers?.IsRetirementOpen == true || Developers?.IsRetiredDialogOpen == true);

    /// <summary>
    /// Whether a dialog on the page itself is open. The page dims its own content; the shell then dims
    /// the sidebar and header to match. The catalog dialogs already cover the whole window.
    /// </summary>
    public bool IsPageDialogOpen => IsModalOpen && !Catalog.IsOpen;

    private void OnModalSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CatalogManagementViewModel.IsOpen) or
            nameof(ProjectDevelopersViewModel.IsRetirementOpen) or
            nameof(ProjectDevelopersViewModel.IsRetiredDialogOpen))
            OnModalStateChanged();
    }

    private void OnModalStateChanged()
    {
        OnPropertyChanged(nameof(IsModalOpen));
        OnPropertyChanged(nameof(IsPageDialogOpen));
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
                OnSelectedProjectSyncIndicatorChanged();
            }
        }
    }

    /// <summary>The selected Project's sync icon, shown in the header.</summary>
    public ProjectSyncIndicator SelectedProjectSyncIndicator =>
        SelectedProject is { } project && _syncIndicators is not null
            ? _syncIndicators.Get(project.Id.Value)
            : ProjectSyncIndicator.None;

    /// <summary>The header icon's tooltip: the state, then what a click does.</summary>
    /// <remarks>It names the Project, because the icon stays the same on that Project's Tracker pages.</remarks>
    public string SelectedProjectSyncTooltip => SelectedProjectSyncIndicator.Kind switch
    {
        ProjectSyncIndicatorKind.Synced or ProjectSyncIndicatorKind.Unsynced =>
            $"{ProjectContextName}: {SelectedProjectSyncIndicator.Label} Click to sync the project now.",
        ProjectSyncIndicatorKind.NeedsAttention or ProjectSyncIndicatorKind.Paused =>
            $"{ProjectContextName}: {SelectedProjectSyncIndicator.Label} Click to open the project dashboard.",
        _ => SelectedProjectSyncIndicator.Label
    };

    private void OnSelectedProjectSyncIndicatorChanged()
    {
        OnPropertyChanged(nameof(SelectedProjectSyncIndicator));
        OnPropertyChanged(nameof(SelectedProjectSyncTooltip));
    }

    /// <summary>
    /// The header's sync icon: syncs the selected Project, or opens it when its sync needs attention or
    /// is paused, since the Project dashboard explains why.
    /// </summary>
    public ICommand SyncSelectedProjectCommand { get; }

    private async Task SyncSelectedProjectAsync()
    {
        if (SelectedProject is not { } project) return;
        ProjectSyncIndicatorKind kind = SelectedProjectSyncIndicator.Kind;
        if (kind is ProjectSyncIndicatorKind.None or ProjectSyncIndicatorKind.Syncing) return;
        ICommand? sync = ProjectReporting?.RepositoryCard?.SyncCommand;
        if (kind is ProjectSyncIndicatorKind.Synced or ProjectSyncIndicatorKind.Unsynced &&
            sync?.CanExecute(null) == true)
        {
            sync.Execute(null);
            return;
        }
        await OpenProjectAsync(project.Id);
    }

    private void RefreshSyncIndicators() => _ = _syncIndicators?.RefreshAsync();

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
            MainWindowViewModel? previous = _currentWorkspace;
            if (SetField(ref _currentWorkspace, value))
            {
                if (previous is not null)
                {
                    previous.Editor.PropertyChanged -= OnModalSourceChanged;
                    previous.ManualCreation.PropertyChanged -= OnModalSourceChanged;
                }
                if (value is not null)
                {
                    value.Editor.PropertyChanged += OnModalSourceChanged;
                    value.ManualCreation.PropertyChanged += OnModalSourceChanged;
                }
                OnPropertyChanged(nameof(HasWorkspace));
                OnPropertyChanged(nameof(IsTrackerWorkspace));
                OnModalStateChanged();
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
                OnPropertyChanged(nameof(IsDevelopers));
                OnPropertyChanged(nameof(IsProjectReport));
                OnPropertyChanged(nameof(IsOverview));
                OnPropertyChanged(nameof(IsDependencyGraph));
                OnPropertyChanged(nameof(IsArchived));
                OnPropertyChanged(nameof(IsHelp));
                OnPropertyChanged(nameof(IsSettings));
                OnPropertyChanged(nameof(IsTrackerWorkspace));
                OnModalStateChanged();
                RefreshNavigationEntries();
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
                RefreshNavigationEntries();
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public bool ShowDefaultNamePrompt
    {
        get => _showDefaultNamePrompt;
        private set => SetField(ref _showDefaultNamePrompt, value);
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
    public string ProjectContextName => SelectedProject?.Name ?? "No project selected";
    public string TrackerContextName => SelectedTracker?.Name ?? "No tracker selected";
    public string ContextSummary => HasProject
        ? HasTracker ? $"{ProjectContextName} / {TrackerContextName}" : ProjectContextName
        : "Portfolio";

    public bool IsPortfolio => SelectedDestination == ShellDestination.Portfolio;
    public bool IsProjectDashboard => SelectedDestination == ShellDestination.ProjectDashboard;
    public bool IsDevelopers => SelectedDestination == ShellDestination.Developers;

    public bool IsProjectReport => SelectedDestination == ShellDestination.ProjectReport;

    /// <summary>Gets whether this app can make Project reports.</summary>
    public bool CanCreateProjectReports => _reportBuilder is not null && _reportFiles is not null;

    /// <summary>Gets the Project Report page, made fresh each time it opens so it lists the current Trackers.</summary>
    public ProjectReportViewModel? ProjectReport
    {
        get => _projectReport;
        private set => SetField(ref _projectReport, value);
    }
    public bool IsOverview => SelectedDestination == ShellDestination.Overview;
    public bool IsDependencyGraph => SelectedDestination == ShellDestination.DependencyGraph;
    public bool IsArchived => SelectedDestination == ShellDestination.Archived;
    public bool IsHelp => SelectedDestination == ShellDestination.Help;
    public bool IsSettings => SelectedDestination == ShellDestination.Settings;

    public ICommand NavigateCommand => _navigateCommand;

    private async Task NavigateFromCommandAsync(ShellDestination destination) =>
        await NavigateAsync(destination);

    private async Task OpenIdentitySettingsAsync()
    {
        SelectedSettingsCategory = SettingsCategory.Project;
        await NavigateAsync(ShellDestination.Settings);
    }

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
        if (SelectedDestination == ShellDestination.Developers)
            Developers?.CloseRetired();
        SetDestination(destination);
        if (destination is ShellDestination.Portfolio or ShellDestination.ProjectDashboard)
        {
            await RefreshDashboardsAsync(cancellationToken);
        }
        if (destination == ShellDestination.Developers && Developers is not null)
            await Developers.RefreshAsync(cancellationToken);
        if (destination == ShellDestination.Settings)
            await SetSettingsProjectAsync(SelectedProject, cancellationToken);

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

    public void StartAutomaticSync() => _autoSync?.Start();

    private void OnAutoSyncStateChanged(object? sender, ProjectId projectId)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnAutoSyncStateChanged(sender, projectId));
            return;
        }
        RefreshSyncIndicators();
        ProjectSyncState state = _autoSync!.GetState(projectId);
        if (state.Kind is ProjectSyncStateKind.DeletionApproval or
            ProjectSyncStateKind.Conflict or ProjectSyncStateKind.AuthenticationRequired or
            ProjectSyncStateKind.ConfigurationInvalid or ProjectSyncStateKind.Failed)
        {
            if (Notifications.FindActionForProject(projectId) is null)
                Notifications.RequireAction("Project sync needs attention", state.LastResult,
                    "Open Project", () => OpenProjectAsync(projectId), projectId);
        }
        else if (state.Kind is ProjectSyncStateKind.UpToDate or ProjectSyncStateKind.Unlinked)
        {
            Notifications.DismissProjectActions(projectId);
            if (SettingsProject?.Id == projectId && _localIdentitySettings is not null)
                _ = _localIdentitySettings.RefreshAsync();
        }
    }

    /// <summary>
    /// Imports a Project from a Git checkout. A progress notice follows each step and ends as a success
    /// or with the failure, which is then thrown on. When the Project's name is taken, the notice waits
    /// while a local name is chosen: a retry with that name continues it, and
    /// <see cref="StopProjectImport"/> ends it when none is chosen.
    /// </summary>
    public async Task<ProjectSyncLink> ImportProjectAsync(string repositoryPath, string? localName = null)
    {
        if (_gitSync is null) throw new InvalidOperationException("Project import is unavailable.");
        NotificationItem notice;
        if (_importNotice is { } waiting)
        {
            notice = waiting;
            Notifications.Restart(notice, NotificationCenter.DescribeProjectImportPhase(ProjectSyncPhase.Inspecting));
        }
        else
        {
            notice = Notifications.BeginProgress("Project import",
                NotificationCenter.DescribeProjectImportPhase(ProjectSyncPhase.Inspecting));
        }
        _importNotice = null;
        IProgress<ProjectSyncPhase> progress = new ProjectSyncProgressReporter(phase =>
            Notifications.Progress(notice, NotificationCenter.DescribeProjectImportPhase(phase)));
        try
        {
            // On a background thread, so the window keeps redrawing; see ProjectRepositoryCardViewModel.
            ProjectSyncLink link = await Task.Run(() => _gitSync.ImportAsync(repositoryPath, localName, progress: progress));
            RefreshSyncIndicators();
            await ReloadCatalogAsync(CancellationToken.None);
            string? name = Projects.FirstOrDefault(project => project.Id.Value == link.ProjectId)?.Name;
            if (name is not null) await OpenProjectAsync(new ProjectId(link.ProjectId));
            Notifications.Complete(notice, name is null
                ? "Imported the Project from the checkout."
                : $"Imported “{name}” from the checkout.");
            return link;
        }
        catch (ProjectNameCollisionException collision)
        {
            Notifications.Progress(notice,
                $"A Project here is already named “{collision.ConflictingName}”. Choose a local name to continue.");
            _importNotice = notice;
            throw;
        }
        catch (ProjectSyncIdentityRequiredException)
        {
            Notifications.Progress(notice, "Git needs your name and email. Enter them to continue.");
            _importNotice = notice;
            throw;
        }
        catch (Exception error)
        {
            Notifications.Complete(notice, error.Message, NotificationKind.Failure);
            throw;
        }
    }

    /// <summary>Ends a Project import that waited for the user (a local name, a Git identity) when they cancelled.</summary>
    public void StopProjectImport(string message = "Import stopped: no local name was chosen.")
    {
        if (_importNotice is not { } notice) return;
        _importNotice = null;
        Notifications.Complete(notice, message, NotificationKind.Information);
    }

    /// <summary>
    /// Saves the Git name and email an import is waiting for; the import is then retried with
    /// <see cref="ImportProjectAsync"/>. A failure ends the waiting import's notice.
    /// </summary>
    public async Task SetGitIdentityAsync(string repositoryPath, string name, string email, GitConfigScope scope)
    {
        if (_gitSync is null) throw new InvalidOperationException("Project import is unavailable.");
        if (_importNotice is { } notice) Notifications.Progress(notice, "Saving your Git name and email…");
        try
        {
            await Task.Run(() => _gitSync.SetCommitIdentityAsync(repositoryPath, name, email, scope));
        }
        catch
        {
            if (_importNotice is { } waiting)
            {
                _importNotice = null;
                Notifications.Complete(waiting,
                    "Your Git name and email could not be saved. Set them with Git in a terminal, then import again.",
                    NotificationKind.Failure);
            }
            throw;
        }
    }

    public Task PublishPendingDeletionAsync(ProjectSyncLink link) =>
        PublishPendingDeletionAsync(link, null);

    private async Task PublishPendingDeletionAsync(ProjectSyncLink link,
        NotificationItem? existingNotice)
    {
        if (_gitSync is null) throw new InvalidOperationException("Project sync is unavailable.");
        NotificationItem notice = existingNotice ?? Notifications.BeginProgress(
            "Project deletion", "Checking the linked checkout…", new ProjectId(link.ProjectId));
        if (existingNotice is not null) Notifications.Restart(notice, "Checking the linked checkout…");
        IProgress<ProjectSyncPhase> progress = new ProjectSyncProgressReporter(phase =>
            Notifications.Progress(notice, NotificationCenter.DescribeProjectSyncPhase(phase)));
        IProgress<ProjectSyncTiming> timing = new ProjectSyncTimingReporter(result =>
            _logger.LogInformation(
                "Project deletion sync timing: outcome={Outcome} totalMs={TotalMs} stages={Stages} networkFetchMs={NetworkMs} gitSnapshotReadMs={SnapshotReadMs} snapshotFiles={SnapshotFiles} reusedSnapshots={ReusedSnapshots} blobReadProcesses={BlobReadProcesses}",
                result.Outcome, result.Total.TotalMilliseconds,
                string.Join(", ", result.Stages.OrderBy(stage => stage.Key)
                    .Select(stage => $"{stage.Key}={stage.Value.TotalMilliseconds:0}")),
                result.NetworkFetch.TotalMilliseconds, result.GitSnapshotRead.TotalMilliseconds,
                result.GitSnapshotFileCount, result.ReusedSnapshots, result.BlobReadProcesses));
        try
        {
            await Task.Run(() => _gitSync.SyncNowAsync(new ProjectId(link.ProjectId), progress: progress,
                timing: timing));
            Notifications.DismissProjectActions(new ProjectId(link.ProjectId), notice);
            Notifications.Complete(notice, "Project deletion published to the linked repository.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Publishing the deletion of Project {ProjectId} was cancelled.", link.ProjectId);
            Notifications.Complete(notice, "Sync cancelled.", NotificationKind.Information);
        }
        catch (Exception error)
        {
            _logger.LogHandled(error, "Publishing the deletion of Project {ProjectId} failed.", link.ProjectId);
            Notifications.NeedAction(notice, error.Message, "Retry",
                () => PublishPendingDeletionAsync(link, notice));
        }
        await ReloadCatalogAsync(CancellationToken.None);
    }

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

    public void ShowNotification(string message) =>
        Notifications.Show("EntityTracker", message, NotificationKind.Failure);

    private bool CanNavigate(ShellNavigationItem item) =>
        !IsBusy && (!item.RequiresProject || HasProject) && (!item.RequiresTracker || HasTracker);

    private bool ConfirmLeavingDirtyWorkspace()
    {
        if (SelectedDestination == ShellDestination.Developers &&
            (Developers?.HasUnsavedForm == true || Developers?.IsRetirementOpen == true))
        {
            if (!_discardConfirmation.ConfirmDiscard("The developer form has unfinished changes."))
                return false;
            Developers.Cancel();
            Developers.CancelRetirement();
        }
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
        destination != SelectedDestination &&
        (CurrentWorkspace?.HasUnsavedWork == true ||
         SelectedDestination == ShellDestination.Developers &&
         (Developers?.HasUnsavedForm == true || Developers?.IsRetirementOpen == true));

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
        bool switchingTrackerPage = project is not null && previousProject?.Id == project.Id &&
            IsTrackerDestination(destination);
        // A search in progress follows the user to the next Tracker instead of being lost.
        WorkspaceSearchState? carriedSearch = previousWorkspace?.CaptureSearch();
        ProjectDevelopersViewModel? previousDevelopers = Developers;
        ShellDestination previousDestination = SelectedDestination;
        Tracker[] previousTrackers = Trackers.ToArray();
        IsBusy = true;
        BusyMessage = tracker is null ? "Loading project context…" : "Loading tracker workspace…";
        try
        {
            CurrentWorkspace?.PrepareForDeactivation();
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
            await SetSettingsProjectAsync(project, cancellationToken);
            if (!switchingTrackerPage)
            {
                Developers = project is null || _developerService is null ? null :
                    new ProjectDevelopersViewModel(project.Id, _developerService,
                        OnProjectDevelopersChangedAsync, Notifications);
                if (Developers is not null) await Developers.RefreshAsync(cancellationToken);
            }
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
                    workspace.SetSearchResponsibleNames(
                        ResponsibilitySearch?.IsEnabled ?? true);
                    ApplyGraphSettings(workspace);
                    workspace.DependencyGraph.PropertyChanged += OnDependencyGraphPropertyChanged;
                    workspace.PersistedStateChanged += OnWorkspacePersistedStateChanged;
                    workspace.IdentitySettingsRequested += OpenIdentitySettingsAsync;
                    workspace.PropertyChanged += OnWorkspacePropertyChanged;
                    try
                    {
                        await workspace.InitializeAsync(cancellationToken);
                        _workspaces.Add(selectedTracker.Id, workspace);
                    }
                    catch
                    {
                        workspace.PersistedStateChanged -= OnWorkspacePersistedStateChanged;
                        workspace.DependencyGraph.PropertyChanged -= OnDependencyGraphPropertyChanged;
                        workspace.IdentitySettingsRequested -= OpenIdentitySettingsAsync;
                        workspace.PropertyChanged -= OnWorkspacePropertyChanged;
                        workspace.Dispose();
                        throw;
                    }
                }
                else
                {
                    workspace.SetSearchResponsibleNames(
                        ResponsibilitySearch?.IsEnabled ?? true);
                    await workspace.RefreshAsync(cancellationToken);
                }

                if (carriedSearch is { IsSearching: true } search && !ReferenceEquals(workspace, previousWorkspace))
                    workspace.RestoreSearch(search);
                CurrentWorkspace = workspace;
            }

            SetDestination(destination);
            // A Tracker switch changes no dashboard data. Refresh those pages on navigation,
            // edits and sync, without making the visible Tracker wait for hidden charts/comparisons.
            if (!switchingTrackerPage)
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
            RefreshNavigationEntries();
            return true;
        }
        catch (Exception exception)
        {
            SelectedProject = previousProject;
            await SetSettingsProjectAsync(previousProject, cancellationToken);
            SelectedTracker = previousTracker;
            CurrentWorkspace = previousWorkspace;
            Developers = previousDevelopers;
            Replace(Trackers, previousTrackers);
            SetDestination(previousDestination);
            _logger.LogError(exception, "Application context could not be changed.");
            Notifications.Show("Context could not be changed", exception.Message,
                NotificationKind.Failure);
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
        if (destination == ShellDestination.ProjectReport)
            ProjectReport = SelectedProject is null || _reportBuilder is null || _reportFiles is null
                ? null
                : new ProjectReportViewModel(SelectedProject.Id, SelectedProject.Name, Trackers,
                    _reportBuilder, _reportFiles, Notifications);
        SelectedDestination = destination;
        if (CurrentWorkspace is not null)
        {
            CurrentWorkspace.SelectedTab = destination switch
            {
                ShellDestination.Overview => MainWindowTab.Overview,
                ShellDestination.DependencyGraph => MainWindowTab.DependencyGraph,
                ShellDestination.Archived => MainWindowTab.Archived,
                _ => CurrentWorkspace.SelectedTab
            };
        }
    }

    private async Task ReloadCatalogAsync(CancellationToken cancellationToken)
    {
        _pendingProjectDeletions = _gitSync is null ? [] :
            await _gitSync.ListPendingDeletionsAsync(cancellationToken);
        OnPropertyChanged(nameof(PendingProjectDeletions));
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

    private async void OnWorkspacePersistedStateChanged(object? sender, EventArgs e)
    {
        RefreshSyncIndicators();
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
            MainWindowTab.DependencyGraph => ShellDestination.DependencyGraph,
            MainWindowTab.Archived => ShellDestination.Archived,
            _ => SelectedDestination
        };

        if (IsTrackerDestination(destination))
        {
            SelectedDestination = destination;
        }
    }

    private void OnGraphSettingsChanged(object? sender, EventArgs e)
    {
        foreach (MainWindowViewModel workspace in _workspaces.Values)
            ApplyGraphSettings(workspace);
    }

    private void ApplyGraphSettings(MainWindowViewModel workspace)
    {
        workspace.DependencyGraph.IsAnimationEnabled = GraphSettings?.IsAnimationEnabled ?? true;
        workspace.DependencyGraph.ShowRings = GraphSettings?.ShowRings ?? false;
        if (GraphSettings is null) return;
        workspace.DependencyGraph.View = GraphSettings.View;
        workspace.DependencyGraph.SolarHighlightMode = GraphSettings.SolarHighlightMode;
        workspace.DependencyGraph.TreeHighlightMode = GraphSettings.TreeHighlightMode;
    }

    /// <summary>
    /// Remembers a view or highlight picked in one Tracker's graph and uses it in the others too.
    /// Each view keeps its own highlight.
    /// </summary>
    private void OnDependencyGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DependencyGraphViewModel graph || GraphSettings is null) return;
        switch (e.PropertyName)
        {
            case nameof(DependencyGraphViewModel.View):
                foreach (MainWindowViewModel workspace in _workspaces.Values)
                    workspace.DependencyGraph.View = graph.View;
                _ = GraphSettings.SetViewAsync(graph.View);
                break;
            case nameof(DependencyGraphViewModel.SolarHighlightMode):
                foreach (MainWindowViewModel workspace in _workspaces.Values)
                    workspace.DependencyGraph.SolarHighlightMode = graph.SolarHighlightMode;
                _ = GraphSettings.SetHighlightModeAsync(DependencyGraphView.SolarSystem, graph.SolarHighlightMode);
                break;
            case nameof(DependencyGraphViewModel.TreeHighlightMode):
                foreach (MainWindowViewModel workspace in _workspaces.Values)
                    workspace.DependencyGraph.TreeHighlightMode = graph.TreeHighlightMode;
                _ = GraphSettings.SetHighlightModeAsync(DependencyGraphView.Tree, graph.TreeHighlightMode);
                break;
        }
    }

    private void OnResponsibilitySearchChanged(object? sender, bool enabled)
    {
        foreach (MainWindowViewModel workspace in _workspaces.Values)
            workspace.SetSearchResponsibleNames(enabled);
    }

    private async Task SetSettingsProjectAsync(Project? project,
        CancellationToken cancellationToken = default)
    {
        _settingsProject = project;
        OnPropertyChanged(nameof(SettingsProject));
        if (_localIdentitySettings is not null)
            await _localIdentitySettings.SetProjectAsync(project?.Id, project?.Name, cancellationToken);
    }

    private async Task OnProjectDevelopersChangedAsync(ProjectId projectId)
    {
        if (SettingsProject?.Id == projectId && _localIdentitySettings is not null)
            await _localIdentitySettings.RefreshAsync();
        if (SelectedProject?.Id != projectId || CurrentWorkspace is null) return;
        try { await CurrentWorkspace.RefreshAsync(); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The overview could not be refreshed after a Developer change.");
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
        ShellDestination.DependencyGraph or
        ShellDestination.Archived;

    private void ChooseSidebarEntry(ShellNavigationEntry? entry)
    {
        if (entry is null || entry.IsSelected) return;
        _ = ChooseSidebarEntryAsync(entry);
    }

    private async Task ChooseSidebarEntryAsync(ShellNavigationEntry entry)
    {
        // A refused navigation (busy, or unfinished work kept) leaves the destination as it was; tell the
        // lists again so they show the current location instead of the refused choice.
        if (!await NavigateAsync(entry.Destination))
        {
            OnPropertyChanged(nameof(SelectedSidebarEntry));
            OnPropertyChanged(nameof(SelectedUtilityEntry));
        }
    }

    /// <summary>Updates which sidebar entries can be opened and which one is the current location.</summary>
    private void RefreshNavigationEntries()
    {
        if (SidebarItems is null || UtilityItems is null) return;
        ShellDestination location = SelectedDestination is ShellDestination.Developers or ShellDestination.ProjectReport
            ? ShellDestination.ProjectDashboard
            : SelectedDestination;
        foreach (ShellNavigationEntry entry in SidebarItems.Concat(UtilityItems))
        {
            entry.IsEnabled = CanNavigate(entry.Item);
            entry.IsSelected = entry.Destination == location;
        }
        OnPropertyChanged(nameof(SelectedSidebarEntry));
        OnPropertyChanged(nameof(SelectedUtilityEntry));
    }

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
        if (_autoSync is not null) _autoSync.StateChanged -= OnAutoSyncStateChanged;
        if (ResponsibilitySearch is not null)
            ResponsibilitySearch.Changed -= OnResponsibilitySearchChanged;
        if (GraphSettings is not null)
            GraphSettings.Changed -= OnGraphSettingsChanged;
        foreach (ProjectDashboardViewModel dashboard in _projectDashboards.Values)
            dashboard.RepositoryCard?.Dispose();
        Catalog.Changed -= OnCatalogChanged;
        Catalog.SelectionRequested -= OnCatalogSelectionRequested;
        foreach (MainWindowViewModel workspace in _workspaces.Values)
        {
            workspace.PersistedStateChanged -= OnWorkspacePersistedStateChanged;
            workspace.DependencyGraph.PropertyChanged -= OnDependencyGraphPropertyChanged;
            workspace.IdentitySettingsRequested -= OpenIdentitySettingsAsync;
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }
    }
}
