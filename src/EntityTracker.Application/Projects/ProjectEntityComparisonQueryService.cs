using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed class ProjectEntityComparisonQueryService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    EntityOverviewService overviewService)
{
    public async Task<ProjectEntityComparison?> GetAsync(
        ProjectId projectId,
        ProjectComparisonFilter filter = ProjectComparisonFilter.ActionableDifferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        if (!Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }

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
        ProjectComparisonTracker[] columns = trackers
            .Select(static tracker => new ProjectComparisonTracker(tracker.Id, tracker.Name))
            .ToArray();

        Dictionary<TrackerId, IReadOnlyDictionary<EntitySourceKey, EntityOverviewItem>> byTracker = [];
        foreach (Tracker tracker in trackers)
        {
            EntityOverviewResult overview = await overviewService.GetAsync(
                tracker.Id,
                cancellationToken);
            if (!overview.IsSuccess)
            {
                string diagnostics = string.Join(
                    "; ",
                    overview.Diagnostics.Select(static diagnostic => diagnostic.Message));
                throw new InvalidDataException(
                    $"Tracker '{tracker.Name}' cannot be compared because its dependency graph is invalid: {diagnostics}");
            }

            byTracker[tracker.Id] = overview.Items.ToDictionary(
                static item => EntitySourceKey.From(item.SourceName));
        }

        EntitySourceKey[] keys = byTracker.Values
            .SelectMany(static entities => entities.Keys)
            .Distinct()
            .OrderBy(static key => key.Value, StringComparer.Ordinal)
            .ToArray();
        ProjectComparisonRow[] allRows = keys.Select(key =>
        {
            ProjectComparisonCell[] cells = trackers.Select(tracker =>
            {
                if (!byTracker[tracker.Id].TryGetValue(key, out EntityOverviewItem? item))
                {
                    return new ProjectComparisonCell(tracker.Id, null, null, null, []);
                }

                return new ProjectComparisonCell(
                    tracker.Id,
                    item.EntityId,
                    item.Status,
                    item.WorkflowState,
                    item.DependencyResolutionIssueNames);
            }).ToArray();
            string displayName = trackers
                .Select(tracker => byTracker[tracker.Id].GetValueOrDefault(key)?.SourceName)
                .First(static name => name is not null)!;
            return new ProjectComparisonRow(
                key.Value,
                displayName,
                cells,
                IsActionable(cells));
        }).ToArray();
        int actionableCount = allRows.Count(static row => row.IsActionable);
        ProjectComparisonRow[] rows = filter == ProjectComparisonFilter.All
            ? allRows
            : allRows.Where(static row => row.IsActionable).ToArray();

        return new ProjectEntityComparison(
            projectId,
            columns,
            rows,
            allRows.Length,
            actionableCount,
            filter);
    }

    private static bool IsActionable(IReadOnlyList<ProjectComparisonCell> cells)
    {
        if (cells.Any(static cell => !cell.IsPresent ||
                                     cell.HasIssues ||
                                     cell.DevelopmentStatus == DevelopmentStatus.ReworkNeeded ||
                                     cell.WorkStatus == EntityWorkflowState.Blocked))
        {
            return true;
        }

        return cells.Select(static cell => cell.DevelopmentStatus).Distinct().Skip(1).Any() ||
               cells.Select(static cell => cell.WorkStatus).Distinct().Skip(1).Any() ||
               cells.Select(static cell => cell.HasIssues).Distinct().Skip(1).Any();
    }
}
