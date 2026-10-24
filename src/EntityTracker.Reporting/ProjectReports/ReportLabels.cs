using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>The words and colours a report uses, matching the app.</summary>
public static class ReportLabels
{
    /// <summary>Every development status, in the order the app lists them.</summary>
    internal static IReadOnlyList<string> StatusOrder { get; } =
    [
        Status(DevelopmentStatus.NotStarted),
        Status(DevelopmentStatus.Blocked),
        Status(DevelopmentStatus.InProgress),
        Status(DevelopmentStatus.ReworkNeeded),
        Status(DevelopmentStatus.Reworking),
        Status(DevelopmentStatus.DevelopmentCompleted),
        Status(DevelopmentStatus.Reconciled),
        Status(DevelopmentStatus.NotRelevant)
    ];

    /// <summary>Every work status of an active entity, in the order the app lists them.</summary>
    internal static IReadOnlyList<string> WorkStatusOrder { get; } =
    [
        WorkStatus(EntityWorkflowState.Ready),
        WorkStatus(EntityWorkflowState.Blocked),
        WorkStatus(EntityWorkflowState.ManuallyBlocked),
        WorkStatus(EntityWorkflowState.InProgress),
        WorkStatus(EntityWorkflowState.DevelopmentCompleted),
        WorkStatus(EntityWorkflowState.Reconciled),
        WorkStatus(EntityWorkflowState.NotRelevant)
    ];

    internal static string Status(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => "Not started",
        ProgressStatusCategory.InProgress => "In progress",
        ProgressStatusCategory.ReworkNeeded => "Rework needed",
        ProgressStatusCategory.DevelopmentCompleted => "Completed",
        ProgressStatusCategory.Reconciled => "Reconciled",
        ProgressStatusCategory.Blocked => "Blocked",
        ProgressStatusCategory.Reworking => "Reworking",
        _ => status.ToString()
    };

    internal static string StatusColor(ProgressStatusCategory status) => ProgressChartPalette.Status(status).Fill;

    public static string Status(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.DevelopmentCompleted => "Completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        DevelopmentStatus.Blocked => "Blocked",
        DevelopmentStatus.Reworking => "Reworking",
        DevelopmentStatus.NotRelevant => "Not relevant",
        _ => status.ToString()
    };

    /// <summary>The work status as the Overview shows it.</summary>
    public static string WorkStatus(EntityWorkflowState state) => state switch
    {
        EntityWorkflowState.Ready or EntityWorkflowState.ReworkNeeded => "Ready",
        EntityWorkflowState.Blocked => "Waiting on dependencies",
        EntityWorkflowState.ManuallyBlocked => "Blocked",
        EntityWorkflowState.InProgress or EntityWorkflowState.Reworking => "In progress",
        EntityWorkflowState.DevelopmentCompleted => "Completed",
        EntityWorkflowState.Reconciled => "Reconciled",
        EntityWorkflowState.Archived => "Archived",
        EntityWorkflowState.NotRelevant => "Not relevant",
        _ => state.ToString()
    };
}
