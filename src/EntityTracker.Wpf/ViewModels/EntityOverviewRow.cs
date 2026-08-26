using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityOverviewRow(
    EntityId EntityId,
    EntityLifecycleState LifecycleState,
    DevelopmentStatus DevelopmentStatus,
    EntityWorkflowState WorkflowState,
    DependencyResolutionState? DependencyState,
    string Priority,
    string Rank,
    string SourceName,
    string ResponsibleDeveloper,
    string GroupName,
    string Provenance,
    string Status,
    string WorkStatus,
    string DependencyCount,
    IReadOnlyList<string> DependencyNames,
    IReadOnlyList<string> DependencyResolutionIssueNames,
    string GraphIssueTitle,
    string GraphIssueDescription,
    string GraphIssueNames,
    string MissingDependencies,
    string Notes,
    string ActionLabel)
{
    public bool HasGraphIssue =>
        DependencyState is DependencyResolutionState.Unresolved or
            DependencyResolutionState.Blocked;

    public bool IsDirectlyUnresolved =>
        DependencyState == DependencyResolutionState.Unresolved;
}
