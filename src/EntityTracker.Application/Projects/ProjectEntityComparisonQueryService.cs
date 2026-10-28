using EntityTracker.Application.Dependencies;
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
        ProjectComparisonCategory categories = ProjectComparisonCategory.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);

        const ProjectComparisonCategory allCategories =
            ProjectComparisonCategory.Missing |
            ProjectComparisonCategory.DifferentScope |
            ProjectComparisonCategory.DoneVsStuck |
            ProjectComparisonCategory.Blocked |
            ProjectComparisonCategory.ReworkNeeded |
            ProjectComparisonCategory.Unresolved |
            ProjectComparisonCategory.WaitingOnDependencies;
        if ((categories & ~allCategories) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(categories));
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
                Categorize(cells));
        }).ToArray();
        // Every entity unless cards are chosen; chosen cards add up (a row in any of them).
        ProjectComparisonRow[] rows = categories == ProjectComparisonCategory.None
            ? allRows
            : allRows.Where(row => (row.Categories & categories) != 0).ToArray();
        ProjectComparisonCategoryCounts counts = new(
            Count(ProjectComparisonCategory.Missing),
            Count(ProjectComparisonCategory.DifferentScope),
            Count(ProjectComparisonCategory.DoneVsStuck),
            Count(ProjectComparisonCategory.Blocked),
            Count(ProjectComparisonCategory.ReworkNeeded),
            Count(ProjectComparisonCategory.Unresolved),
            Count(ProjectComparisonCategory.WaitingOnDependencies));

        return new ProjectEntityComparison(
            projectId,
            columns,
            rows,
            allRows.Length,
            categories,
            counts);

        int Count(ProjectComparisonCategory value) =>
            allRows.Count(row => (row.Categories & value) != 0);
    }

    private static ProjectComparisonCategory Categorize(
        IReadOnlyList<ProjectComparisonCell> cells)
    {
        ProjectComparisonCategory categories = ProjectComparisonCategory.None;
        if (cells.Any(static cell => !cell.IsPresent))
        {
            categories |= ProjectComparisonCategory.Missing;
        }

        // The Trackers are parallel copies that progress at their own pace, so a different status alone is
        // not out of sync. These are: in scope in one Tracker and Not relevant in another, and finished in
        // one Tracker while stuck in another.
        ProjectComparisonCell[] present = cells.Where(static cell => cell.IsPresent).ToArray();
        if (present.Any(static cell => cell.DevelopmentStatus == DevelopmentStatus.NotRelevant) &&
            present.Any(static cell => cell.DevelopmentStatus != DevelopmentStatus.NotRelevant))
        {
            categories |= ProjectComparisonCategory.DifferentScope;
        }

        if (present.Any(IsDone) && present.Any(static cell => !IsDone(cell) && IsStuck(cell)))
        {
            categories |= ProjectComparisonCategory.DoneVsStuck;
        }

        if (cells.Any(static cell => cell.DevelopmentStatus == DevelopmentStatus.Blocked))
        {
            categories |= ProjectComparisonCategory.Blocked;
        }

        if (cells.Any(static cell => cell.WorkStatus == EntityWorkflowState.Blocked))
        {
            categories |= ProjectComparisonCategory.WaitingOnDependencies;
        }

        if (cells.Any(static cell =>
                cell.DevelopmentStatus is DevelopmentStatus.ReworkNeeded or DevelopmentStatus.Reworking))
        {
            categories |= ProjectComparisonCategory.ReworkNeeded;
        }

        if (cells.Any(static cell => cell.HasIssues))
        {
            categories |= ProjectComparisonCategory.Unresolved;
        }

        return categories;
    }

    private static bool IsDone(ProjectComparisonCell cell) =>
        cell.DevelopmentStatus is DevelopmentStatus.DevelopmentCompleted or DevelopmentStatus.Reconciled;

    // Blocked, waiting on dependencies, or in rework.
    private static bool IsStuck(ProjectComparisonCell cell) =>
        cell.DevelopmentStatus is DevelopmentStatus.Blocked or DevelopmentStatus.ReworkNeeded
            or DevelopmentStatus.Reworking ||
        cell.WorkStatus == EntityWorkflowState.Blocked;
}
