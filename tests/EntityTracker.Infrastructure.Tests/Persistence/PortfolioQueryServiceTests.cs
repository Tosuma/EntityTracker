using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
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
                [completed, pending, archived], [], [],
                progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 1, 0)));
        await state.ApplyAsync(
            emptyTracker.Id,
            new TrackedStateChangeSet(
                [secondCompleted], [], [],
                progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 0, 0, 0, 1)));

        PortfolioQueryService query = new(
            projects,
            trackers,
            entities,
            dependencies,
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
    public async Task ProjectComparison_NormalizesKeysDefaultsToEveryRowAndAddsUpCategories()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
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
        // #73: not relevant in Alpha, still to do in Zulu: present in both, with a different scope (#164).
        TrackedEntity alphaStable = new(EntityId.New(), alpha.Id, "Stable", DevelopmentStatus.NotRelevant);
        // #164: done in Alpha and needing rework in Zulu.
        TrackedEntity alphaStuck = new(
            EntityId.New(), alpha.Id, "Stuck", DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity alphaArchived = new(
            EntityId.New(), alpha.Id, "Archived only",
            lifecycleState: EntityLifecycleState.Archived);
        TrackedEntity alphaMissing = TrackedEntity.Placeholder(alpha.Id, "Missing target");
        await state.ApplyAsync(alpha.Id, new TrackedStateChangeSet(
            [alphaCommon, alphaOnly, alphaAttention, alphaStable, alphaStuck, alphaArchived, alphaMissing],
            [], [],
            [alphaAttention.Id],
            [new EntityDependency(alphaAttention.Id, alphaMissing.Id)]));

        TrackedEntity zuluCommon = new(
            EntityId.New(), zulu.Id, "customer", DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity zuluOnly = new(EntityId.New(), zulu.Id, "Zulu only");
        TrackedEntity zuluAttention = new(
            EntityId.New(), zulu.Id, "Attention", DevelopmentStatus.Reconciled);
        TrackedEntity zuluStable = new(EntityId.New(), zulu.Id, "stable");
        TrackedEntity zuluStuck = new(EntityId.New(), zulu.Id, "Stuck", DevelopmentStatus.ReworkNeeded);
        await state.ApplyAsync(zulu.Id, new TrackedStateChangeSet(
            [zuluCommon, zuluOnly, zuluAttention, zuluStable, zuluStuck], [], []));

        EntityOverviewService overview = new(
            entities,
            new SqliteEntityAuditReader(database),
            dependencies,
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService());
        ProjectEntityComparisonQueryService query = new(projects, trackers, overview);

        ProjectEntityComparison all = Assert.IsType<ProjectEntityComparison>(
            await query.GetAsync(project.Id));
        ProjectEntityComparison missing = Assert.IsType<ProjectEntityComparison>(
            await query.GetAsync(project.Id, ProjectComparisonCategory.Missing));
        // Chosen categories add up: a row in either.
        ProjectEntityComparison missingOrUnresolved = Assert.IsType<ProjectEntityComparison>(
            await query.GetAsync(
                project.Id,
                ProjectComparisonCategory.Missing | ProjectComparisonCategory.Unresolved));

        Assert.Equal(["Alpha", "Zulu"], all.Trackers.Select(static item => item.Name));
        Assert.Equal(6, all.TotalEntityCount);
        Assert.Equal(
            ["ALPHA ONLY", "ATTENTION", "CUSTOMER", "STABLE", "STUCK", "ZULU ONLY"],
            all.Rows.Select(static row => row.NormalizedSourceKey));
        Assert.Equal(2, all.CategoryCounts.Missing);
        Assert.Equal(1, all.CategoryCounts.DifferentScope);
        Assert.Equal(1, all.CategoryCounts.DoneVsStuck);
        Assert.Equal(1, all.CategoryCounts.ReworkNeeded);
        Assert.Equal(1, all.CategoryCounts.Unresolved);
        Assert.Equal(all.CategoryCounts, missing.CategoryCounts);
        Assert.Equal(["ALPHA ONLY", "ZULU ONLY"],
            missing.Rows.Select(static row => row.NormalizedSourceKey));
        Assert.Equal(["ALPHA ONLY", "ATTENTION", "ZULU ONLY"],
            missingOrUnresolved.Rows.Select(static row => row.NormalizedSourceKey));
        Assert.DoesNotContain(all.Rows, static row => row.NormalizedSourceKey == "ARCHIVED ONLY");
        ProjectComparisonRow alphaOnlyRow = all.Rows[0];
        Assert.True(alphaOnlyRow.Cells[0].IsPresent);
        Assert.False(alphaOnlyRow.Cells[1].IsPresent);
        Assert.True(all.Rows[1].Cells[0].HasIssues);
        Assert.False(all.Rows[1].Cells[1].HasIssues);
        // #164: Trackers progress at their own pace, so In progress in one and Completed in another is not
        // out of sync; present in one Tracker only is Missing, nothing more.
        ProjectComparisonCategory Categories(string key) =>
            all.Rows.Single(row => row.NormalizedSourceKey == key).Categories;
        Assert.Equal(ProjectComparisonCategory.None, Categories("CUSTOMER"));
        Assert.Equal(ProjectComparisonCategory.Missing, Categories("ALPHA ONLY"));
        Assert.Equal(ProjectComparisonCategory.DifferentScope, Categories("STABLE"));
        Assert.Equal(
            ProjectComparisonCategory.DoneVsStuck | ProjectComparisonCategory.ReworkNeeded,
            Categories("STUCK"));
        ProjectComparisonRow stableRow = all.Rows.Single(static row => row.NormalizedSourceKey == "STABLE");
        Assert.True(stableRow.Cells[0].IsPresent);
        Assert.Equal(DevelopmentStatus.NotRelevant, stableRow.Cells[0].DevelopmentStatus);
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
                [], []));
        await trackerManagement.RecycleAsync(tracker.Id);
        await projectManagement.RecycleAsync(project.Id);

        CatalogNameValidationService validation = new(projects, trackers);
        CatalogPurgeImpactService impact = new(
            trackers,
            entities,
            new SqliteProgressHistoryRepository(database));

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
        new SqliteProjectTrackerStore(database),
        new EffectiveDependencyResolver(),
        new ProgressSnapshotCalculator());
}
