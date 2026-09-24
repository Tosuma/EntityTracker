using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.Tests;

public sealed class AggregateProgressReportingServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PortfolioAndProjectReports_ExcludeRecycledCatalogItems()
    {
        Project activeProject = Project("Active");
        Project recycledProject = Project("Recycled", CatalogLifecycleState.Recycled);
        Tracker activeTracker = Tracker(activeProject, "Active tracker");
        Tracker recycledTracker = Tracker(
            activeProject,
            "Recycled tracker",
            CatalogLifecycleState.Recycled);
        Tracker trackerInRecycledProject = Tracker(recycledProject, "Hidden tracker");
        StubHistoryRepository history = new(new Dictionary<TrackerId, IReadOnlyList<ProgressSnapshot>>
        {
            [activeTracker.Id] = [Snapshot(completed: 2)],
            [recycledTracker.Id] = [Snapshot(completed: 20)],
            [trackerInRecycledProject.Id] = [Snapshot(completed: 200)]
        });
        AggregateProgressReportingService service = new(
            new StubProjectRepository([activeProject, recycledProject]),
            new StubTrackerRepository([activeTracker, recycledTracker, trackerInRecycledProject]),
            history,
            TimeZoneInfo.Utc,
            timeProvider: new FixedTimeProvider(Now));

        ProgressDashboardReport portfolio = await service.GetPortfolioReportAsync(
            ProgressDateRange.AllHistory);
        ProgressDashboardReport project = Assert.IsType<ProgressDashboardReport>(
            await service.GetProjectReportAsync(activeProject.Id, ProgressDateRange.AllHistory));
        ProgressDashboardReport? recycled = await service.GetProjectReportAsync(
            recycledProject.Id,
            ProgressDateRange.AllHistory);

        Assert.Equal(2, portfolio.ManagerSummary.ActiveEntityCount);
        Assert.Equal(2, project.ManagerSummary.ActiveEntityCount);
        Assert.Null(recycled);
        Assert.Equal([activeTracker.Id], history.RequestedTrackerIds.Distinct());
    }

    private static Project Project(
        string name,
        CatalogLifecycleState lifecycle = CatalogLifecycleState.Active) =>
        new(
            ProjectId.New(),
            name,
            Now,
            Now,
            lifecycle,
            lifecycle == CatalogLifecycleState.Recycled ? Now : null);

    private static Tracker Tracker(
        Project project,
        string name,
        CatalogLifecycleState lifecycle = CatalogLifecycleState.Active) =>
        new(
            TrackerId.New(),
            project.Id,
            name,
            Now,
            Now,
            lifecycle,
            lifecycle == CatalogLifecycleState.Recycled ? Now : null);

    private static ProgressSnapshot Snapshot(int completed) =>
        new(Now, new ProgressSnapshotState(0, 0, 0, 0, completed, 0));

    private sealed class StubProjectRepository(IReadOnlyList<Project> projects)
        : IProjectRepository
    {
        public Task<Project?> GetAsync(
            ProjectId projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(projects.SingleOrDefault(project => project.Id == projectId));

        public Task<IReadOnlyList<Project>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(projects);

        public Task<bool> IsNameReservedAsync(
            string name,
            ProjectId? excludingProjectId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class StubTrackerRepository(IReadOnlyList<Tracker> trackers)
        : ITrackerRepository
    {
        public Task<Tracker?> GetAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(trackers.SingleOrDefault(tracker => tracker.Id == trackerId));

        public Task<IReadOnlyList<Tracker>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(trackers);

        public Task<IReadOnlyList<Tracker>> GetByProjectAsync(
            ProjectId projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tracker>>(
                trackers.Where(tracker => tracker.ProjectId == projectId).ToArray());

        public Task<bool> IsNameReservedAsync(
            ProjectId projectId,
            string name,
            TrackerId? excludingTrackerId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class StubHistoryRepository(
        IReadOnlyDictionary<TrackerId, IReadOnlyList<ProgressSnapshot>> histories)
        : IProgressHistoryRepository
    {
        public List<TrackerId> RequestedTrackerIds { get; } = [];

        public Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityStatusHistoryEntry>>([]);

        public Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default)
        {
            RequestedTrackerIds.Add(trackerId);
            return Task.FromResult(histories.GetValueOrDefault(trackerId) ?? []);
        }

        public Task<ProgressSnapshot?> GetLatestProgressSnapshotAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(histories.GetValueOrDefault(trackerId)?.LastOrDefault());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
