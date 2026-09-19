using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class PortfolioQueryServiceTests
{
    [Fact]
    public async Task PortfolioAndProjectQueries_AreActiveOnlyAndEntityWeighted()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteManualDependencyOverrideRepository overrides = new(database);
        SqliteTrackedStateStore state = new(database);
        ProjectManagementService projectManagement = new(
            projects,
            new SqliteProjectTrackerStore(database));
        TrackerManagementService trackerManagement = CreateTrackerManagement(database);
        Tracker defaultTracker = Assert.Single(await trackers.GetAllAsync());
        Project secondProject = await projectManagement.CreateAsync("Second project");
        Tracker emptyTracker = await trackerManagement.CreateBlankAsync(
            secondProject.Id,
            "Empty tracker");
        TrackedEntity completed = new(EntityId.New(), defaultTracker.Id, "Completed");
        completed.ChangeStatus(DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity pending = new(EntityId.New(), defaultTracker.Id, "Pending");
        TrackedEntity archived = new(
            EntityId.New(),
            defaultTracker.Id,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        TrackedEntity secondCompleted = new(
            EntityId.New(),
            emptyTracker.Id,
            "Second completed",
            DevelopmentStatus.Reconciled);
        await state.ApplyAsync(
            defaultTracker.Id,
            new TrackedStateChangeSet(
                [completed, pending, archived], [], [], [], [], [],
                progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 1, 0)));
        await state.ApplyAsync(
            emptyTracker.Id,
            new TrackedStateChangeSet(
                [secondCompleted], [], [], [], [], [],
                progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 0, 0, 0, 1)));

        PortfolioQueryService query = new(
            projects,
            trackers,
            entities,
            dependencies,
            overrides,
            new SqliteProgressHistoryRepository(database),
            new EffectiveDependencyResolver(),
            new ProgressSnapshotCalculator());
        PortfolioDashboard portfolio = await query.GetPortfolioAsync();
        ProjectDashboard second = Assert.IsType<ProjectDashboard>(
            await query.GetProjectAsync(secondProject.Id));

        Assert.Equal(2, portfolio.Projects.Count);
        ProjectPortfolioSummary defaultSummary = portfolio.Projects.Single(
            item => item.ProjectId == defaultTracker.ProjectId);
        Assert.Equal(2, defaultSummary.Progress.ActiveEntityCount);
        Assert.Equal(1, defaultSummary.Progress.ImplementedEntityCount);
        Assert.Equal(50, defaultSummary.Progress.ImplementedPercentage);
        Assert.Equal(3, portfolio.Progress.ActiveEntityCount);
        Assert.Equal(2, portfolio.Progress.ImplementedEntityCount);
        Assert.Equal(200d / 3d, portfolio.Progress.ImplementedPercentage!.Value, 6);
        Assert.Equal(100, Assert.Single(second.Trackers).Progress.ImplementedPercentage);
        Assert.NotNull(defaultSummary.Progress.LastActivityUtc);

        await trackerManagement.RecycleAsync(emptyTracker.Id);
        second = Assert.IsType<ProjectDashboard>(await query.GetProjectAsync(secondProject.Id));
        Assert.Empty(second.Trackers);
        Assert.Equal(0, second.Progress.ActiveEntityCount);
    }

    [Fact]
    public async Task ProjectComparison_NormalizesKeysAndDefaultsToActionableRows()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteManualDependencyOverrideRepository overrides = new(database);
        SqliteTrackedStateStore state = new(database);
        ProjectManagementService projectManagement = new(
            projects,
            new SqliteProjectTrackerStore(database));
        TrackerManagementService trackerManagement = CreateTrackerManagement(database);
        Project project = await projectManagement.CreateAsync("Comparison project");
        Tracker zulu = await trackerManagement.CreateBlankAsync(project.Id, "Zulu");
        Tracker alpha = await trackerManagement.CreateBlankAsync(project.Id, "Alpha");

        TrackedEntity alphaCommon = new(
            EntityId.New(), alpha.Id, " Customer ", DevelopmentStatus.InProgress);
        TrackedEntity alphaOnly = new(EntityId.New(), alpha.Id, "Alpha only");
        TrackedEntity alphaAttention = new(
            EntityId.New(), alpha.Id, "Attention", DevelopmentStatus.Reconciled);
        TrackedEntity alphaArchived = new(
            EntityId.New(), alpha.Id, "Archived only",
            lifecycleState: EntityLifecycleState.Archived);
        await state.ApplyAsync(alpha.Id, new TrackedStateChangeSet(
            [alphaCommon, alphaOnly, alphaAttention, alphaArchived],
            [], [], [alphaAttention.Id], [],
            [new PersistedUnresolvedDependency(
                new UnresolvedDependency(alphaAttention.Id, "Missing target"),
                ImportedDependencyKind.Mandatory)]));

        TrackedEntity zuluCommon = new(
            EntityId.New(), zulu.Id, "customer", DevelopmentStatus.InProgress);
        TrackedEntity zuluOnly = new(EntityId.New(), zulu.Id, "Zulu only");
        TrackedEntity zuluAttention = new(
            EntityId.New(), zulu.Id, "Attention", DevelopmentStatus.Reconciled);
        await state.ApplyAsync(zulu.Id, new TrackedStateChangeSet(
            [zuluCommon, zuluOnly, zuluAttention], [], [], [], [], []));

        EntityOverviewService overview = new(
            entities,
            new SqliteEntityAuditReader(database),
            dependencies,
            overrides,
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService());
        ProjectEntityComparisonQueryService query = new(projects, trackers, overview);

        ProjectEntityComparison actionable = Assert.IsType<ProjectEntityComparison>(
            await query.GetAsync(project.Id));
        ProjectEntityComparison all = Assert.IsType<ProjectEntityComparison>(
            await query.GetAsync(project.Id, ProjectComparisonFilter.All));

        Assert.Equal(["Alpha", "Zulu"], actionable.Trackers.Select(static item => item.Name));
        Assert.Equal(4, all.TotalEntityCount);
        Assert.Equal(3, actionable.ActionableEntityCount);
        Assert.Equal(
            ["ALPHA ONLY", "ATTENTION", "ZULU ONLY"],
            actionable.Rows.Select(static row => row.NormalizedSourceKey));
        Assert.DoesNotContain(all.Rows, static row => row.NormalizedSourceKey == "ARCHIVED ONLY");
        ProjectComparisonRow alphaOnlyRow = actionable.Rows[0];
        Assert.True(alphaOnlyRow.Cells[0].IsPresent);
        Assert.False(alphaOnlyRow.Cells[1].IsPresent);
        Assert.True(actionable.Rows[1].Cells[0].HasIssues);
        Assert.False(actionable.Rows[1].Cells[1].HasIssues);
        Assert.False(all.Rows.Single(static row => row.NormalizedSourceKey == "CUSTOMER").IsActionable);
    }

    [Fact]
    public async Task NameValidationAndPurgeImpact_IncludeRecycledReservationsAndHierarchy()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteTrackedStateStore state = new(database);
        ProjectManagementService projectManagement = new(
            projects,
            new SqliteProjectTrackerStore(database));
        TrackerManagementService trackerManagement = CreateTrackerManagement(database);
        Project project = await projectManagement.CreateAsync("Reserved project");
        Tracker tracker = await trackerManagement.CreateBlankAsync(project.Id, "Reserved tracker");
        await state.ApplyAsync(
            tracker.Id,
            new TrackedStateChangeSet(
                [new TrackedEntity(EntityId.New(), tracker.Id, "One")],
                [], [], [], [], []));
        await trackerManagement.RecycleAsync(tracker.Id);
        await projectManagement.RecycleAsync(project.Id);

        CatalogNameValidationService validation = new(projects, trackers);
        CatalogPurgeImpactService impact = new(
            trackers,
            entities,
            new SqliteProgressHistoryRepository(database),
            state);

        Assert.False((await validation.ValidateProjectAsync(" reserved PROJECT ")).IsValid);
        Assert.False((await validation.ValidateTrackerAsync(
            project.Id,
            "RESERVED tracker")).IsValid);
        CatalogPurgeImpact result = await impact.GetProjectImpactAsync(project.Id);
        Assert.Equal(1, result.TrackerCount);
        Assert.Equal(1, result.ActiveEntityCount);
        Assert.True(result.HistoryCount >= 1);
    }

    private static TrackerManagementService CreateTrackerManagement(SqliteDatabase database) => new(
        new SqliteProjectRepository(database),
        new SqliteTrackerRepository(database),
        new SqliteEntityRepository(database),
        new SqliteDependencyRepository(database),
        new SqliteManualDependencyOverrideRepository(database),
        new SqliteProjectTrackerStore(database),
        new EffectiveDependencyResolver(),
        new ProgressSnapshotCalculator());
}
