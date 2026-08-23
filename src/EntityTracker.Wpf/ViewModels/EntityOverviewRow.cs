using EntityTracker.Domain;
using EntityTracker.Application.Workflow;
using EntityTracker.Application.Ranking;

namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityOverviewRow(
    EntityId EntityId,
    EntityLifecycleState LifecycleState,
    EntityWorkflowState WorkflowState,
    DependencyResolutionState? DependencyState,
    string Rank,
    string SourceName,
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
