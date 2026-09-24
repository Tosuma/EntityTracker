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
        ProjectComparisonCategory category = ProjectComparisonCategory.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        if (!Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }

        const ProjectComparisonCategory allCategories =
            ProjectComparisonCategory.Missing |
            ProjectComparisonCategory.Divergent |
            ProjectComparisonCategory.Blocked |
            ProjectComparisonCategory.ReworkNeeded |
            ProjectComparisonCategory.Unresolved;
        if ((category & ~allCategories) != 0 ||
            category != ProjectComparisonCategory.None && !IsSingleCategory(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
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
        int actionableCount = allRows.Count(static row => row.IsActionable);
        ProjectComparisonRow[] rows = filter == ProjectComparisonFilter.All
            ? allRows
            : category == ProjectComparisonCategory.None
                ? allRows.Where(static row => row.IsActionable).ToArray()
                : allRows.Where(row => (row.Categories & category) != 0).ToArray();
        ProjectComparisonCategoryCounts counts = new(
            Count(ProjectComparisonCategory.Missing),
            Count(ProjectComparisonCategory.Divergent),
            Count(ProjectComparisonCategory.Blocked),
            Count(ProjectComparisonCategory.ReworkNeeded),
            Count(ProjectComparisonCategory.Unresolved));

        return new ProjectEntityComparison(
            projectId,
            columns,
            rows,
            allRows.Length,
            actionableCount,
            filter,
            category,
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

        ProjectComparisonCell[] present = cells.Where(static cell => cell.IsPresent).ToArray();
        if (present.Select(static cell => cell.DevelopmentStatus).Distinct().Skip(1).Any() ||
            present.Select(static cell => cell.WorkStatus).Distinct().Skip(1).Any())
        {
            categories |= ProjectComparisonCategory.Divergent;
        }

        if (cells.Any(static cell => cell.WorkStatus == EntityWorkflowState.Blocked))
        {
            categories |= ProjectComparisonCategory.Blocked;
        }

        if (cells.Any(static cell =>
                cell.DevelopmentStatus == DevelopmentStatus.ReworkNeeded ||
                cell.WorkStatus == EntityWorkflowState.ReworkNeeded))
        {
            categories |= ProjectComparisonCategory.ReworkNeeded;
        }

        if (cells.Any(static cell => cell.HasIssues))
        {
            categories |= ProjectComparisonCategory.Unresolved;
        }

        return categories;
    }

    private static bool IsSingleCategory(ProjectComparisonCategory category) =>
        ((int)category & ((int)category - 1)) == 0;
}
