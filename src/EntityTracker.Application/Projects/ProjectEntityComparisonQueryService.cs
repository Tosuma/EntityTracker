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
            ProjectComparisonCategory.Divergent |
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
            Count(ProjectComparisonCategory.Divergent),
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

        // Not relevant in one Tracker and relevant in another is intended, not a difference to act on.
        ProjectComparisonCell[] present = cells
            .Where(static cell => cell.IsPresent && cell.DevelopmentStatus != DevelopmentStatus.NotRelevant)
            .ToArray();
        if (present.Select(static cell => cell.DevelopmentStatus).Distinct().Skip(1).Any() ||
            present.Select(static cell => cell.WorkStatus).Distinct().Skip(1).Any())
        {
            categories |= ProjectComparisonCategory.Divergent;
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
}
