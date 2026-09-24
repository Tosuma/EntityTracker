using EntityTracker.Application.Projects;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record ProjectComparisonDisplayCell(
    ProjectComparisonCell Model,
    string DevelopmentStatus,
    string WorkStatus,
    string IssueText,
    string AutomationName)
{
    public bool IsPresent => Model.IsPresent;

    public bool HasIssues => Model.HasIssues;

    public static ProjectComparisonDisplayCell Create(
        string entityName,
        string trackerName,
        ProjectComparisonCell cell)
    {
        if (!cell.IsPresent)
        {
            return new ProjectComparisonDisplayCell(
                cell,
                "Not present",
                string.Empty,
                string.Empty,
                $"{entityName} in {trackerName}: not present");
        }

        string development = FormatDevelopmentStatus(cell.DevelopmentStatus);
        string work = FormatWorkStatus(cell.WorkStatus);
        string issue = cell.HasIssues
            ? $"Issues: {string.Join(", ", cell.IssueNames)}"
            : "No dependency issues";
        return new ProjectComparisonDisplayCell(
            cell,
            development,
            work,
            issue,
            $"Open {entityName} in {trackerName}. Development status {development}; work status {work}; {issue}");
    }

    private static string FormatDevelopmentStatus(DevelopmentStatus? status) => status switch
    {
        Domain.DevelopmentStatus.NotStarted => "Not started",
        Domain.DevelopmentStatus.InProgress => "In progress",
        Domain.DevelopmentStatus.ReworkNeeded => "Rework needed",
        Domain.DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        Domain.DevelopmentStatus.Reconciled => "Reconciled",
        null => "Not present",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string FormatWorkStatus(EntityWorkflowState? status) => status switch
    {
        EntityWorkflowState.Ready => "Ready",
        EntityWorkflowState.Blocked => "Blocked",
        EntityWorkflowState.InProgress => "In progress",
        EntityWorkflowState.ReworkNeeded => "Rework needed",
        EntityWorkflowState.DevelopmentCompleted => "Dev. completed",
        EntityWorkflowState.Reconciled => "Reconciled",
        EntityWorkflowState.Archived => "Archived",
        null => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

public sealed record ProjectComparisonDisplayRow(
    string NormalizedSourceKey,
    string DisplayName,
    IReadOnlyList<ProjectComparisonDisplayCell> Cells,
    bool IsActionable);
