using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Tracking;
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
        await state.ApplyAsync(
            defaultTracker.Id,
            new TrackedStateChangeSet([completed, pending], [], [], [], [], []));

        PortfolioQueryService query = new(
            projects,
            trackers,
            entities,
            dependencies,
            overrides,
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
        Assert.Null(Assert.Single(second.Trackers).Progress.ImplementedPercentage);

        await trackerManagement.RecycleAsync(emptyTracker.Id);
        second = Assert.IsType<ProjectDashboard>(await query.GetProjectAsync(secondProject.Id));
        Assert.Empty(second.Trackers);
        Assert.Equal(0, second.Progress.ActiveEntityCount);
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
