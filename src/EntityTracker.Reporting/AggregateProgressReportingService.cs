using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Reporting;

public sealed class AggregateProgressReportingService
{
    private readonly IProjectRepository _projectRepository;
    private readonly ITrackerRepository _trackerRepository;
    private readonly IProgressHistoryRepository _historyRepository;
    private readonly AggregateProgressDashboardBuilder _builder;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeProvider _timeProvider;

    public AggregateProgressReportingService(
        IProjectRepository projectRepository,
        ITrackerRepository trackerRepository,
        IProgressHistoryRepository historyRepository,
        TimeZoneInfo timeZone,
        AggregateProgressDashboardBuilder? builder = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectRepository);
        ArgumentNullException.ThrowIfNull(trackerRepository);
        ArgumentNullException.ThrowIfNull(historyRepository);
        ArgumentNullException.ThrowIfNull(timeZone);
        _projectRepository = projectRepository;
        _trackerRepository = trackerRepository;
        _historyRepository = historyRepository;
        _timeZone = timeZone;
        _builder = builder ?? new AggregateProgressDashboardBuilder();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProgressDashboardReport> GetPortfolioReportAsync(
        ProgressDateRange range,
        CancellationToken cancellationToken = default)
    {
        ProjectId[] activeProjectIds = (await _projectRepository.GetAllAsync(cancellationToken))
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Active)
            .Select(static project => project.Id)
            .ToArray();
        HashSet<ProjectId> activeProjects = activeProjectIds.ToHashSet();
        Tracker[] trackers = (await _trackerRepository.GetAllAsync(cancellationToken))
            .Where(tracker => tracker.LifecycleState == CatalogLifecycleState.Active &&
                              activeProjects.Contains(tracker.ProjectId))
            .ToArray();
        return await BuildAsync(trackers, range, cancellationToken);
    }

    public async Task<ProgressDashboardReport?> GetProjectReportAsync(
        ProjectId projectId,
        ProgressDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Project? project = await _projectRepository.GetAsync(projectId, cancellationToken);
        if (project?.LifecycleState != CatalogLifecycleState.Active)
        {
            return null;
        }

        Tracker[] trackers = (await _trackerRepository.GetByProjectAsync(projectId, cancellationToken))
            .Where(static tracker => tracker.LifecycleState == CatalogLifecycleState.Active)
            .ToArray();
        return await BuildAsync(trackers, range, cancellationToken);
    }

    private async Task<ProgressDashboardReport> BuildAsync(
        IReadOnlyList<Tracker> trackers,
        ProgressDateRange range,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProgressSnapshot>[] histories = await Task.WhenAll(trackers.Select(
            tracker => _historyRepository.GetProgressSnapshotsAsync(
                tracker.Id,
                cancellationToken)));
        Dictionary<TrackerId, IReadOnlyList<ProgressSnapshot>> byTracker = trackers
            .Select((tracker, index) => (tracker.Id, History: histories[index]))
            .ToDictionary(static item => item.Id, static item => item.History);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);
        return _builder.Build(byTracker, range, today, _timeZone);
    }
}
