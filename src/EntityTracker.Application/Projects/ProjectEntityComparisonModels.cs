using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public enum ProjectComparisonFilter
{
    ActionableDifferences,
    All
}

[Flags]
public enum ProjectComparisonCategory
{
    None = 0,
    Missing = 1,
    Divergent = 2,
    Blocked = 4,
    ReworkNeeded = 8,
    Unresolved = 16
}

public sealed record ProjectComparisonCategoryCounts(
    int Missing,
    int Divergent,
    int Blocked,
    int ReworkNeeded,
    int Unresolved)
{
    public static ProjectComparisonCategoryCounts Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record ProjectComparisonTracker(
    TrackerId TrackerId,
    string Name);

public sealed record ProjectComparisonCell(
    TrackerId TrackerId,
    EntityId? EntityId,
    DevelopmentStatus? DevelopmentStatus,
    EntityWorkflowState? WorkStatus,
    IReadOnlyList<string> IssueNames)
{
    public bool IsPresent => EntityId is not null;

    public bool HasIssues => IssueNames.Count > 0;
}

public sealed record ProjectComparisonRow(
    string NormalizedSourceKey,
    string DisplayName,
    IReadOnlyList<ProjectComparisonCell> Cells,
    ProjectComparisonCategory Categories)
{
    public bool IsActionable => Categories != ProjectComparisonCategory.None;
}

public sealed record ProjectEntityComparison(
    ProjectId ProjectId,
    IReadOnlyList<ProjectComparisonTracker> Trackers,
    IReadOnlyList<ProjectComparisonRow> Rows,
    int TotalEntityCount,
    int ActionableEntityCount,
    ProjectComparisonFilter Filter,
    ProjectComparisonCategory SelectedCategory,
    ProjectComparisonCategoryCounts CategoryCounts);
