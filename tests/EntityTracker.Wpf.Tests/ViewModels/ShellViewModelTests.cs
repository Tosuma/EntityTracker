using System.IO;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Infrastructure.Importing;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task ProjectSyncPresentation_DisablesLocalOnlyAndReportsSuccessfulPush()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        StubRepositorySynchronization repositories = new(harness.DefaultProject.Id);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(lastProjectId: harness.DefaultProject.Id),
            new RecordingDiscardConfirmation(true),
            repositories,
            repositories);
        await shell.InitializeAsync();

        Assert.Equal(1, repositories.CachedStatusCallCount);
        Assert.Equal(0, repositories.FullStatusCallCount);
        Assert.True(shell.CanSyncRepository);
        Assert.Equal(string.Empty, shell.SyncDisabledReason);

        repositories.Status = repositories.Status with
        {
            SyncState = ProjectSyncState.Ahead,
            Upstream = "team/main",
            AheadCount = 2,
            BehindCount = 0,
            Diagnostic = null
        };
        await shell.SelectProjectAsync(null);
        await shell.OpenProjectAsync(harness.DefaultProject.Id);
        Assert.True(shell.CanSyncRepository);

        await shell.SyncSelectedProjectAsync();

        Assert.Equal(1, repositories.SyncCallCount);
        Assert.Equal(ProjectSyncState.UpToDate, shell.ActiveRepositoryStatus?.SyncState);
        Assert.Equal("Pushed 2 local commits.", shell.NotificationMessage);
        Assert.Equal(ShellNotificationSeverity.Success, shell.NotificationSeverity);
    }

    [Fact]
    public async Task RepositoryLinkNotificationsDistinguishSuccessAndFailure()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        StubRepositorySynchronization repositories = new(harness.DefaultProject.Id);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(lastProjectId: harness.DefaultProject.Id),
            new RecordingDiscardConfirmation(true),
            repositories,
            repositories);
        await shell.InitializeAsync();

        await shell.LinkSelectedProjectAsync(@"C:\Projects\Shared");

        Assert.Contains("is now Git-backed", shell.NotificationMessage, StringComparison.Ordinal);
        Assert.Equal(ShellNotificationSeverity.Success, shell.NotificationSeverity);

        repositories.LinkFailure = new InvalidOperationException("Repository is unavailable.");
        await shell.LinkSelectedProjectAsync(@"C:\Projects\Unavailable");

        Assert.StartsWith("Repository action could not be completed", shell.NotificationMessage);
        Assert.Equal(ShellNotificationSeverity.Error, shell.NotificationSeverity);
    }

    [Fact]
    public async Task CachedTrackerNavigationRemainsAvailableWhenRepositoryStatusIsBlocked()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        StubRepositorySynchronization repositories = new(harness.DefaultProject.Id)
        {
            Status = new ProjectRepositoryStatus(
                harness.DefaultProject.Id,
                ProjectRepositoryStatusKind.Blocked,
                @"C:\Projects\Shared",
                "main",
                "Repository validation is required.")
        };
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            new RecordingDiscardConfirmation(true),
            repositories,
            repositories);

        await shell.InitializeAsync();

        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        Assert.NotNull(shell.CurrentWorkspace);
        Assert.True(shell.CanReadActiveProjectCache);
        Assert.False(shell.CanUseActiveRepository);
        Assert.Equal(0, repositories.FullStatusCallCount);
    }

    [Fact]
    public async Task InitializeAsync_RestoresValidContextAndUsesTypedNavigation()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        RecordingDiscardConfirmation confirmation = new(true);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            confirmation);

        await shell.InitializeAsync();

        Assert.Equal(harness.DefaultProject.Id, shell.SelectedProject?.Id);
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        Assert.Equal(ShellDestination.Overview, shell.SelectedDestination);
        Assert.True(shell.IsTrackerWorkspace);
        Assert.Equal(ProjectRepositoryStatusKind.SQLiteOnly, shell.ActiveRepositoryStatus?.Kind);
        Assert.True(shell.CanLinkRepository);
        Assert.True(shell.NavigateCommand.CanExecute(ShellDestination.Reports));

        Assert.True(await shell.NavigateAsync(ShellDestination.Reports));
        Assert.Equal(MainWindowTab.Reports, shell.CurrentWorkspace?.SelectedTab);
        Assert.True(await shell.NavigateAsync(ShellDestination.Settings));
        Assert.False(shell.IsTrackerWorkspace);
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);

        Assert.True(await shell.NavigateAsync(ShellDestination.Portfolio));
        Assert.Equal(harness.DefaultProject.Id, shell.SelectedProject?.Id);
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        Assert.True(shell.NavigateCommand.CanExecute(ShellDestination.Overview));

        await shell.OpenProjectAsync(harness.DefaultProject.Id);
        Assert.Equal(ShellDestination.ProjectDashboard, shell.SelectedDestination);
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        await shell.OpenTrackerAsync(harness.DefaultTracker.Id);
        Assert.Equal(ShellDestination.Overview, shell.SelectedDestination);
    }

    [Fact]
    public async Task WorkspaceInitiatedTabChangesKeepShellDestinationInSync()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            new RecordingDiscardConfirmation(true));
        await shell.InitializeAsync();
        MainWindowViewModel workspace = Assert.IsType<MainWindowViewModel>(shell.CurrentWorkspace);

        workspace.SelectedTab = MainWindowTab.AddEntity;
        Assert.Equal(ShellDestination.AddEntity, shell.SelectedDestination);

        workspace.ManualCreation.CancelCommand.Execute(null);
        Assert.Equal(MainWindowTab.Overview, workspace.SelectedTab);
        Assert.Equal(ShellDestination.Overview, shell.SelectedDestination);
    }

    [Fact]
    public async Task ComparisonCellNavigation_GuardsDirtyStateAndOpensTargetDetails()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        Tracker second = await harness.TrackerManagement.CreateBlankAsync(
            harness.DefaultProject.Id,
            "Second tracker");
        await harness.AddEntityAsync(harness.DefaultTracker.Id, "First only");
        await harness.AddEntityAsync(second.Id, "Second only");
        RecordingDiscardConfirmation confirmation = new(false);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            confirmation);
        await shell.InitializeAsync();
        Assert.True(await shell.NavigateAsync(ShellDestination.ProjectDashboard));
        await WaitUntilAsync(() => shell.ProjectReporting?.Comparison is not null);
        Assert.Equal(2, shell.ProjectReporting!.MissingCount);
        Assert.True(shell.ProjectReporting.SelectCategoryCommand.CanExecute(
            ProjectComparisonCategory.Missing));
        shell.ProjectReporting.SelectCategoryCommand.Execute(ProjectComparisonCategory.Missing);
        await WaitUntilAsync(() =>
            shell.ProjectReporting.SelectedCategory == ProjectComparisonCategory.Missing &&
            shell.ProjectReporting.Comparison?.Rows.Count == 2);
        Assert.All(
            shell.ProjectReporting.Comparison!.Rows,
            static row => Assert.True(
                (row.Categories & ProjectComparisonCategory.Missing) != 0));
        shell.ProjectReporting.SelectCategoryCommand.Execute(ProjectComparisonCategory.Missing);
        await WaitUntilAsync(() =>
            shell.ProjectReporting.SelectedCategory == ProjectComparisonCategory.None &&
            shell.ProjectReporting.Comparison?.SelectedCategory == ProjectComparisonCategory.None);
        ProjectComparisonCell cell = shell.ProjectReporting!.Comparison!.Rows
            .Single(static row => row.DisplayName == "Second only")
            .Cells.Single(candidate => candidate.TrackerId == second.Id);

        shell.CurrentWorkspace!.ManualCreation.EntityName = "Unsaved draft";
        Assert.False(await shell.OpenComparisonCellAsync(cell));
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        Assert.Equal(ShellDestination.ProjectDashboard, shell.SelectedDestination);
        Assert.Equal(1, confirmation.CallCount);

        confirmation.Result = true;
        Assert.True(await shell.OpenComparisonCellAsync(cell));
        Assert.Equal(second.Id, shell.SelectedTracker?.Id);
        Assert.Equal(ShellDestination.Overview, shell.SelectedDestination);
        Assert.True(shell.CurrentWorkspace?.IsEntityDetailsOpen);
        Assert.Equal(cell.EntityId, shell.CurrentWorkspace?.SelectedEntityDetails?.EntityId);
    }

    [Fact]
    public async Task InitializeAsync_InvalidSavedContextFallsBackToPortfolioAndClearsIt()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: ProjectId.New(),
                lastTrackerId: TrackerId.New()),
            new RecordingDiscardConfirmation(true));

        await shell.InitializeAsync();

        Assert.Equal(ShellDestination.Portfolio, shell.SelectedDestination);
        Assert.Null(shell.SelectedProject);
        Assert.Null(shell.SelectedTracker);
        SettingsLoadResult persisted = await harness.SettingsStore.LoadAsync();
        Assert.Null(persisted.Settings.LastProjectId);
        Assert.Null(persisted.Settings.LastTrackerId);
    }

    [Fact]
    public async Task TrackerSwitch_PreservesPerTrackerTableStateClearsSelectionAndGuardsDirtyWork()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        Tracker second = await harness.TrackerManagement.CreateBlankAsync(
            harness.DefaultProject.Id,
            "Second tracker");
        await harness.AddEntityAsync(harness.DefaultTracker.Id, "First entity");
        await harness.AddEntityAsync(second.Id, "Second entity");
        RecordingDiscardConfirmation confirmation = new(false);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            confirmation);
        await shell.InitializeAsync();
        Assert.True(shell.NotificationMessage is null, shell.NotificationMessage);
        MainWindowViewModel firstWorkspace = Assert.IsType<MainWindowViewModel>(
            shell.CurrentWorkspace);
        firstWorkspace.ActiveTable.SearchQuery = "First";
        firstWorkspace.UpdateOverviewSelection(firstWorkspace.OverviewItems);
        firstWorkspace.ManualCreation.EntityName = "Unfinished entity";
        Tracker secondSelection = shell.Trackers.Single(item => item.Id == second.Id);

        Assert.False(await shell.SelectTrackerAsync(secondSelection));
        Assert.Equal(harness.DefaultTracker.Id, shell.SelectedTracker?.Id);
        Assert.True(firstWorkspace.HasUnsavedWork);
        Assert.Equal(1, confirmation.CallCount);

        confirmation.Result = true;
        Assert.True(await shell.SelectTrackerAsync(secondSelection));
        Assert.False(firstWorkspace.HasUnsavedWork);
        Assert.Equal(0, firstWorkspace.SelectedActiveEntityCount);
        MainWindowViewModel secondWorkspace = Assert.IsType<MainWindowViewModel>(
            shell.CurrentWorkspace);
        secondWorkspace.ActiveTable.SearchQuery = "Second";

        Tracker firstSelection = shell.Trackers.Single(
            item => item.Id == harness.DefaultTracker.Id);
        Assert.True(await shell.SelectTrackerAsync(firstSelection));
        Assert.Same(firstWorkspace, shell.CurrentWorkspace);
        Assert.Equal("First", firstWorkspace.ActiveTable.SearchQuery);
        Assert.Equal("Second", secondWorkspace.ActiveTable.SearchQuery);
    }

    [Fact]
    public async Task HelpSqlNavigation_GuardsAndDiscardsUnappliedSynchronizationReview()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        await harness.AddEntityAsync(harness.DefaultTracker.Id, "Existing");
        harness.SetCsvPath(
            "table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count",
            "Existing;;0;;0;0");
        RecordingDiscardConfirmation confirmation = new(false);
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            confirmation);
        await shell.InitializeAsync();
        Assert.True(await shell.NavigateAsync(ShellDestination.SchemaSynchronization));
        MainWindowViewModel workspace = Assert.IsType<MainWindowViewModel>(shell.CurrentWorkspace);
        await workspace.ImportCsvAsync();
        Assert.True(workspace.Review.HasReview);

        Assert.False(await shell.NavigateAsync(ShellDestination.HelpSql));
        Assert.Equal(ShellDestination.SchemaSynchronization, shell.SelectedDestination);
        Assert.True(workspace.Review.HasReview);

        confirmation.Result = true;
        Assert.True(await shell.NavigateAsync(ShellDestination.HelpSql));
        Assert.Equal(ShellDestination.HelpSql, shell.SelectedDestination);
        Assert.False(workspace.Review.HasReview);
        Assert.Equal(2, confirmation.CallCount);
    }

    [Fact]
    public async Task CatalogDialogs_ValidateReservedNamesCancelSafelyAndRequireExactPurgeName()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(),
            new RecordingDiscardConfirmation(true));
        CatalogManagementViewModel catalog = shell.Catalog;
        int initialProjectCount = await harness.GetProjectCountAsync();

        catalog.OpenCreateProject();
        catalog.Name = $" {harness.DefaultProject.Name.ToUpperInvariant()} ";
        catalog.SubmitNameCommand.Execute(null);
        await WaitUntilAsync(() => catalog.ErrorMessage is not null);

        Assert.Contains("reserved", catalog.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(initialProjectCount, await harness.GetProjectCountAsync());

        catalog.CancelCommand.Execute(null);
        Assert.False(catalog.IsOpen);
        catalog.OpenCreateProject();
        catalog.Name = "Canceled project";
        catalog.CancelCommand.Execute(null);
        Assert.Equal(initialProjectCount, await harness.GetProjectCountAsync());

        await catalog.RequestPurgeAsync(harness.DefaultProject);
        catalog.TypedConfirmation = harness.DefaultProject.Name.ToUpperInvariant();
        Assert.False(catalog.ConfirmPurgeCommand.CanExecute(null));
        catalog.TypedConfirmation = harness.DefaultProject.Name;
        Assert.True(catalog.ConfirmPurgeCommand.CanExecute(null));
        catalog.CancelCommand.Execute(null);

        Assert.Equal(initialProjectCount, await harness.GetProjectCountAsync());
    }

    [Fact]
    public async Task DefaultNamePrompt_IdentifiesEachRemainingDefaultAndClearsAfterBothRenames()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            new RecordingDiscardConfirmation(true));
        await shell.InitializeAsync();

        Assert.True(shell.ShowDefaultNamePrompt);
        Assert.Contains("Tracker", shell.DefaultNamePromptMessage, StringComparison.Ordinal);
        Assert.Equal("Rename tracker", shell.DefaultNamePromptActionLabel);

        shell.Catalog.OpenRenameTracker(shell.SelectedTracker!);
        shell.Catalog.Name = "Named tracker";
        shell.Catalog.SubmitNameCommand.Execute(null);
        await WaitUntilAsync(() =>
            shell.SelectedTracker?.Name == "Named tracker" &&
            !shell.IsBusy &&
            !shell.Catalog.IsBusy);

        Assert.True(shell.ShowDefaultNamePrompt);
        Assert.Contains("Project", shell.DefaultNamePromptMessage, StringComparison.Ordinal);
        Assert.Equal("Rename project", shell.DefaultNamePromptActionLabel);

        shell.Catalog.OpenRenameProject(shell.SelectedProject!);
        shell.Catalog.Name = "Named project";
        shell.Catalog.SubmitNameCommand.Execute(null);
        await WaitUntilAsync(() =>
            shell.SelectedProject?.Name == "Named project" &&
            !shell.IsBusy &&
            !shell.Catalog.IsBusy);

        Assert.False(shell.ShowDefaultNamePrompt);
        Assert.Equal(string.Empty, shell.DefaultNamePromptMessage);
    }

    [Fact]
    public async Task TrackerRecycleAndRestore_ReturnToOwningProjectDashboard()
    {
        await using ShellHarness harness = await ShellHarness.CreateAsync();
        await harness.TrackerManagement.CreateBlankAsync(
            harness.DefaultProject.Id,
            "Second tracker");
        using ShellViewModel shell = harness.CreateShell(
            new EntityTrackerSettings(
                lastProjectId: harness.DefaultProject.Id,
                lastTrackerId: harness.DefaultTracker.Id),
            new RecordingDiscardConfirmation(true));
        await shell.InitializeAsync();
        await shell.NavigateAsync(ShellDestination.ProjectDashboard);

        shell.Catalog.RequestRecycle(shell.SelectedTracker!);
        shell.Catalog.ConfirmRecycleCommand.Execute(null);
        await WaitUntilAsync(() =>
            !shell.Catalog.IsOpen &&
            !shell.IsBusy &&
            shell.SelectedDestination == ShellDestination.ProjectDashboard &&
            shell.SelectedTracker is null &&
            shell.ProjectDashboard?.Trackers.Count == 1);

        Assert.Equal(harness.DefaultProject.Id, shell.SelectedProject?.Id);

        await shell.Catalog.OpenRecycleBinAsync(shell.SelectedProject);
        Tracker recycled = Assert.Single(shell.Catalog.RecycledTrackers);
        await shell.Catalog.RestoreAsync(recycled);
        await WaitUntilAsync(() =>
            !shell.Catalog.IsOpen &&
            !shell.IsBusy &&
            shell.SelectedDestination == ShellDestination.ProjectDashboard &&
            shell.ProjectDashboard?.Trackers.Count == 2);

        Assert.Equal(harness.DefaultProject.Id, shell.SelectedProject?.Id);
        Assert.Null(shell.SelectedTracker);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ShellHarness : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly SqliteTrackedStateStore _stateStore;
        private readonly IProjectRepository _projects;
        private readonly ITrackerRepository _trackers;
        private readonly DashboardViewModelFactory _dashboardFactory;
        private readonly ProgressHistoryInitializer _historyInitializer;
        private readonly TrackerWorkspaceViewModelFactory _workspaceFactory;
        private readonly CatalogManagementViewModel _catalog;
        private readonly AppearanceViewModel _appearance;
        private readonly TestAdapters _adapters;
        private readonly TestClipboard _clipboard = new();

        private ShellHarness(
            string directory,
            SqliteTrackedStateStore stateStore,
            IProjectRepository projects,
            ITrackerRepository trackers,
            Project defaultProject,
            Tracker defaultTracker,
            TrackerManagementService trackerManagement,
            DashboardViewModelFactory dashboardFactory,
            ProgressHistoryInitializer historyInitializer,
            TrackerWorkspaceViewModelFactory workspaceFactory,
            CatalogManagementViewModel catalog,
            EntityTrackerSettingsStore settingsStore,
            AppearanceViewModel appearance,
            TestAdapters adapters)
        {
            _directory = directory;
            _stateStore = stateStore;
            _projects = projects;
            _trackers = trackers;
            DefaultProject = defaultProject;
            DefaultTracker = defaultTracker;
            TrackerManagement = trackerManagement;
            _dashboardFactory = dashboardFactory;
            _historyInitializer = historyInitializer;
            _workspaceFactory = workspaceFactory;
            _catalog = catalog;
            SettingsStore = settingsStore;
            _appearance = appearance;
            _adapters = adapters;
        }

        public Project DefaultProject { get; }
        public Tracker DefaultTracker { get; }
        public TrackerManagementService TrackerManagement { get; }
        public EntityTrackerSettingsStore SettingsStore { get; }

        public async Task<int> GetProjectCountAsync() =>
            (await _projects.GetAllAsync()).Count;

        public static async Task<ShellHarness> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "EntityTracker.ShellTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SqliteDatabase database = new(Path.Combine(directory, "entity-tracker.db"));
            await database.InitializeAsync();
            SqliteProjectRepository projects = new(database);
            SqliteTrackerRepository trackers = new(database);
            SqliteEntityRepository entities = new(database);
            SqliteDependencyRepository dependencies = new(database);
            SqliteManualDependencyOverrideRepository overrides = new(database);
            SqliteTrackedStateStore stateStore = new(database);
            SqliteProgressHistoryRepository history = new(database);
            SqliteProjectTrackerStore catalogStore = new(database);
            EffectiveDependencyResolver resolver = new();
            DependencyRanker ranker = new();
            PriorityPlanningService priorities = new();
            ProgressSnapshotCalculator snapshots = new();
            EntityDependencyEditorService editor = new(
                entities,
                dependencies,
                overrides,
                resolver,
                ranker,
                stateStore,
                priorities,
                snapshots);
            SchemaSynchronizationPlanner planner = new(ranker, resolver, snapshots);
            SchemaSynchronizationService synchronization = new(
                new CsvSchemaImportFileParser(new CsvSchemaImportParser()),
                entities,
                dependencies,
                overrides,
                planner,
                editor,
                stateStore);
            EntityOverviewService overview = new(
                entities,
                new SqliteEntityAuditReader(database),
                dependencies,
                overrides,
                ranker,
                resolver,
                new WorkflowReadinessEvaluator(),
                priorities);
            TrackerManagementService trackerManagement = new(
                projects,
                trackers,
                entities,
                dependencies,
                overrides,
                catalogStore,
                resolver,
                snapshots);
            ProjectManagementService projectManagement = new(projects, catalogStore);
            PortfolioQueryService portfolio = new(
                projects,
                trackers,
                entities,
                dependencies,
                overrides,
                history,
                resolver,
                snapshots);
            ProgressHistoryInitializer historyInitializer = new(
                entities,
                dependencies,
                overrides,
                history,
                stateStore,
                resolver,
                snapshots);
            ProgressChartPresentationBuilder chartPresentation = new();
            AggregateProgressReportingService aggregateReporting = new(
                projects,
                trackers,
                history,
                TimeZoneInfo.Utc,
                timeProvider: TimeProvider.System);
            ProjectEntityComparisonQueryService comparison = new(
                projects,
                trackers,
                overview);
            DashboardViewModelFactory dashboardFactory = new(
                portfolio,
                comparison,
                aggregateReporting,
                chartPresentation);
            TestAdapters adapters = new();
            TrackerWorkspaceViewModelFactory workspaceFactory = new(
                overview,
                synchronization,
                new BulkStatusUpdateService(
                    entities,
                    dependencies,
                    overrides,
                    resolver,
                    stateStore),
                new ManualEntityCreationService(
                    entities,
                    dependencies,
                    overrides,
                    ranker,
                    resolver,
                    stateStore),
                editor,
                new EntityLifecycleService(
                    entities,
                    dependencies,
                    overrides,
                    stateStore,
                    resolver,
                    ranker),
                adapters,
                new ProgressReportingService(history, TimeZoneInfo.Utc),
                chartPresentation,
                new ProgressChartPngExporter(chartPresentation),
                adapters,
                adapters,
                adapters,
                adapters,
                NullLoggerFactory.Instance);
            CatalogManagementViewModel catalog = new(
                projectManagement,
                trackerManagement,
                new TrackerCsvCreationService(
                    projects,
                    new CsvSchemaImportFileParser(new CsvSchemaImportParser()),
                    planner,
                    catalogStore),
                new CatalogNameValidationService(projects, trackers),
                new CatalogPurgeImpactService(trackers, entities, history, stateStore),
                portfolio,
                projects,
                trackers,
                adapters);
            EntityTrackerSettingsStore settings = new(Path.Combine(directory, "settings.json"));
            AppearanceViewModel appearance = new(
                settings,
                new TestThemeService());
            Tracker defaultTracker = Assert.Single(await trackers.GetAllAsync());
            Project defaultProject = Assert.IsType<Project>(
                await projects.GetAsync(defaultTracker.ProjectId));
            return new ShellHarness(
                directory,
                stateStore,
                projects,
                trackers,
                defaultProject,
                defaultTracker,
                trackerManagement,
                dashboardFactory,
                historyInitializer,
                workspaceFactory,
                catalog,
                settings,
                appearance,
                adapters);
        }

        public ShellViewModel CreateShell(
            EntityTrackerSettings initialSettings,
            IContextDiscardConfirmation confirmation,
            IProjectRepositoryManager? repositoryManager = null,
            IProjectSynchronizationService? synchronizationService = null) => new(
                _projects,
                _trackers,
                _dashboardFactory,
                _historyInitializer,
                SettingsStore,
                _workspaceFactory,
                confirmation,
                _catalog,
                _appearance,
                _clipboard,
                initialSettings,
                repositoryManager,
                synchronizationService);

        public Task AddEntityAsync(TrackerId trackerId, string name) =>
            _stateStore.ApplyAsync(
                trackerId,
                new TrackedStateChangeSet(
                    [new TrackedEntity(EntityId.New(), trackerId, name)],
                    [], [], [], [], [],
                    progressSnapshotAfterChanges:
                        new ProgressSnapshotState(1, 0, 0, 0, 0, 0)));

        public void SetCsvPath(params string[] lines)
        {
            string path = Path.Combine(_directory, "schema.csv");
            File.WriteAllLines(path, lines);
            _adapters.CsvPath = path;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDiscardConfirmation(bool result) : IContextDiscardConfirmation
    {
        public bool Result { get; set; } = result;
        public int CallCount { get; private set; }

        public bool ConfirmDiscard(string description)
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class StubRepositorySynchronization(ProjectId projectId) :
        IProjectRepositoryManager,
        IProjectSynchronizationService
    {
        public ProjectRepositoryStatus Status { get; set; } = new(
            projectId,
            ProjectRepositoryStatusKind.GitClean,
            @"C:\Projects\Shared",
            "main",
            "No upstream is configured for the managed branch.",
            ProjectSyncState.NoUpstream);

        public int SyncCallCount { get; private set; }
        public int CachedStatusCallCount { get; private set; }
        public int FullStatusCallCount { get; private set; }
        public Exception? LinkFailure { get; set; }

        public Task<IReadOnlyList<ProjectRepositoryStatus>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectRepositoryStatus>>([Status]);

        public Task<ProjectRepositoryStatus> GetStatusAsync(ProjectId id, CancellationToken cancellationToken = default)
        {
            FullStatusCallCount++;
            return Task.FromResult(Status);
        }

        public Task<ProjectRepositoryStatus> GetCachedStatusAsync(ProjectId id, CancellationToken cancellationToken = default)
        {
            CachedStatusCallCount++;
            return Task.FromResult(Status);
        }

        public Task<ProjectSyncResult> SyncAsync(ProjectId id, CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            Status = Status with
            {
                SyncState = ProjectSyncState.UpToDate,
                AheadCount = 0,
                BehindCount = 0,
                LastSuccessfulFetchAtUtc = DateTimeOffset.UtcNow,
                LastSuccessfulPushAtUtc = DateTimeOffset.UtcNow
            };
            return Task.FromResult(new ProjectSyncResult(
                ProjectSyncOutcome.Pushed,
                ProjectSyncFailureKind.None,
                "Pushed 2 local commits.",
                Status));
        }

        public Task LinkAsync(ProjectId id, string repositoryPath, CancellationToken cancellationToken = default)
        {
            if (LinkFailure is not null) throw LinkFailure;
            Status = Status with
            {
                RepositoryPath = repositoryPath,
                Kind = ProjectRepositoryStatusKind.GitClean,
                SyncState = ProjectSyncState.NoUpstream
            };
            return Task.CompletedTask;
        }
        public Task<ProjectId> OpenAsync(string repositoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LocateAsync(ProjectId id, string repositoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RebuildCacheAsync(ProjectId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestAdapters :
        ICsvFilePicker,
        IProgressChartFilePicker,
        IClipboardService,
        ISchemaSynchronizationConfirmation,
        IContextDiscardConfirmation
    {
        public string? CsvPath { get; set; }

        public string? SelectCsvFile() => CsvPath;
        public string? SelectPngPath(string suggestedFileName) => null;
        public void SetPng(byte[] png) { }
        public void SetText(string text) { }
        public bool ConfirmArchiveMissingEntities(int entityCount) => true;
        public bool ConfirmDiscard(string description) => true;
    }

    private sealed class TestClipboard : IClipboardService
    {
        public void SetPng(byte[] png) { }
        public void SetText(string text) { }
    }

    private sealed class TestThemeService : IApplicationThemeService
    {
        public ApplicationAppearance CurrentAppearance { get; private set; } =
            ApplicationAppearance.System;

        public void Apply(ApplicationAppearance appearance) => CurrentAppearance = appearance;
    }
}
