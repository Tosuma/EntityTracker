using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed class PortfolioQueryService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IDependencyRepository dependencyRepository,
    IManualDependencyOverrideRepository overrideRepository,
    IProgressHistoryRepository progressHistoryRepository,
    EffectiveDependencyResolver effectiveDependencyResolver,
    ProgressSnapshotCalculator snapshotCalculator)
{
    public async Task<PortfolioDashboard> GetPortfolioAsync(
        CancellationToken cancellationToken = default)
    {
        Project[] projects = (await projectRepository.GetAllAsync(cancellationToken))
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Active)
            .OrderBy(static project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id.Value)
            .ToArray();
        HashSet<ProjectId> activeProjectIds = projects.Select(static project => project.Id).ToHashSet();
        Tracker[] trackers = (await trackerRepository.GetAllAsync(cancellationToken))
            .Where(tracker => tracker.LifecycleState == CatalogLifecycleState.Active &&
                              activeProjectIds.Contains(tracker.ProjectId))
            .ToArray();
        Dictionary<TrackerId, TrackerProgressSummary> summaries = await LoadSummariesAsync(
            trackers,
            cancellationToken);

        ProjectPortfolioSummary[] result = projects.Select(project =>
        {
            Tracker[] projectTrackers = trackers
                .Where(tracker => tracker.ProjectId == project.Id)
                .ToArray();
            return new ProjectPortfolioSummary(
                project.Id,
                project.Name,
                projectTrackers.Length,
                Aggregate(projectTrackers.Select(tracker => summaries[tracker.Id])));
        }).ToArray();
        return new PortfolioDashboard(result, Aggregate(summaries.Values));
    }

    public async Task<ProjectDashboard?> GetProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Project? project = await projectRepository.GetAsync(projectId, cancellationToken);
        if (project?.LifecycleState != CatalogLifecycleState.Active)
        {
            return null;
        }

        Tracker[] trackers = (await trackerRepository.GetByProjectAsync(projectId, cancellationToken))
            .Where(static tracker => tracker.LifecycleState == CatalogLifecycleState.Active)
            .OrderBy(static tracker => tracker.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static tracker => tracker.Name, StringComparer.Ordinal)
            .ThenBy(static tracker => tracker.Id.Value)
            .ToArray();
        Dictionary<TrackerId, TrackerProgressSummary> summaries = await LoadSummariesAsync(
            trackers,
            cancellationToken);
        TrackerDashboardSummary[] cards = trackers.Select(tracker => new TrackerDashboardSummary(
            tracker.Id,
            tracker.ProjectId,
            tracker.Name,
            summaries[tracker.Id],
            tracker.CopiedFromTrackerId)).ToArray();
        return new ProjectDashboard(project, cards, Aggregate(cards.Select(static card => card.Progress)));
    }

    private async Task<Dictionary<TrackerId, TrackerProgressSummary>> LoadSummariesAsync(
        IEnumerable<Tracker> trackers,
        CancellationToken cancellationToken)
    {
        Tracker[] items = trackers.ToArray();
        TrackerProgressSummary[] summaries = await Task.WhenAll(items.Select(
            tracker => GetTrackerSummaryAsync(tracker.Id, cancellationToken)));
        return items.Select((tracker, index) => (tracker.Id, Summary: summaries[index]))
            .ToDictionary(static item => item.Id, static item => item.Summary);
    }

    private async Task<TrackerProgressSummary> GetTrackerSummaryAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<TrackedEntity>> entityTask =
            entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependencyTask =
            dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            dependencyRepository.GetAllUnresolvedAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overrideTask =
            overrideRepository.GetAllAsync(trackerId, cancellationToken);
        Task<ProgressSnapshot?> latestSnapshotTask =
            progressHistoryRepository.GetLatestProgressSnapshotAsync(trackerId, cancellationToken);
        await Task.WhenAll(
            entityTask,
            dependencyTask,
            unresolvedTask,
            overrideTask,
            latestSnapshotTask);

        EffectiveDependencyState effective = effectiveDependencyResolver.Resolve(
            await entityTask,
            await dependencyTask,
            await unresolvedTask,
            await overrideTask);
        ProgressSnapshotState state = snapshotCalculator.Calculate(await entityTask, effective);
        return TrackerProgressSummary.From(
            state,
            effective.UnresolvedDependencies.Count,
            (await latestSnapshotTask)?.RecordedAtUtc);
    }

    private static TrackerProgressSummary Aggregate(IEnumerable<TrackerProgressSummary> summaries)
    {
        TrackerProgressSummary[] items = summaries.ToArray();
        ProgressSnapshotState state = new(
            items.Sum(static item => item.ReadyCount),
            items.Sum(static item => item.BlockedCount),
            items.Sum(static item => item.InProgressCount),
            items.Sum(static item => item.ReworkNeededCount),
            items.Sum(static item => item.DevelopmentCompletedCount),
            items.Sum(static item => item.ReconciledCount));
        return new TrackerProgressSummary(
            state,
            items.Sum(static item => item.UnresolvedReferenceCount),
            items.Where(static item => item.LastActivityUtc is not null)
                .Select(static item => item.LastActivityUtc)
                .Max());
    }
}
