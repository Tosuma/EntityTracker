using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public enum ProjectComparisonFilter
{
    ActionableDifferences,
    All
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
    bool IsActionable);

public sealed record ProjectEntityComparison(
    ProjectId ProjectId,
    IReadOnlyList<ProjectComparisonTracker> Trackers,
    IReadOnlyList<ProjectComparisonRow> Rows,
    int TotalEntityCount,
    int ActionableEntityCount,
    ProjectComparisonFilter Filter);
