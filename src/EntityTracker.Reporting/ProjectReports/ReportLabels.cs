using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>The words and colours a report uses, matching the app.</summary>
internal static class ReportLabels
{
    internal const string Green100 = "#123836";
    internal const string Green80 = "#41605E";
    internal const string Green60 = "#718886";
    internal const string Green40 = "#A0AFAF";
    internal const string Coral = "#FF6359";

    internal static string Status(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => "Not started",
        ProgressStatusCategory.InProgress => "In progress",
        ProgressStatusCategory.ReworkNeeded => "Rework needed",
        ProgressStatusCategory.DevelopmentCompleted => "Dev. completed",
        ProgressStatusCategory.Reconciled => "Reconciled",
        ProgressStatusCategory.Blocked => "Blocked",
        ProgressStatusCategory.Reworking => "Reworking",
        _ => status.ToString()
    };

    internal static string StatusColor(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => Green40,
        ProgressStatusCategory.InProgress or ProgressStatusCategory.Reworking => Green80,
        ProgressStatusCategory.DevelopmentCompleted => Green60,
        ProgressStatusCategory.Reconciled => Green100,
        _ => Coral
    };

    internal static string Status(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        DevelopmentStatus.Blocked => "Blocked",
        DevelopmentStatus.Reworking => "Reworking",
        _ => status.ToString()
    };

    /// <summary>The work status as the Overview shows it.</summary>
    internal static string WorkStatus(EntityWorkflowState state) => state switch
    {
        EntityWorkflowState.Ready or EntityWorkflowState.ReworkNeeded => "Ready",
        EntityWorkflowState.Blocked => "Waiting on dependencies",
        EntityWorkflowState.ManuallyBlocked => "Blocked",
        EntityWorkflowState.InProgress or EntityWorkflowState.Reworking => "In progress",
        EntityWorkflowState.DevelopmentCompleted => "Completed",
        EntityWorkflowState.Reconciled => "Reconciled",
        EntityWorkflowState.Archived => "Archived",
        _ => state.ToString()
    };
}
