using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>The words and colours a report uses, matching the app.</summary>
public static class ReportLabels
{
    internal const string Green100 = "#123836";
    internal const string Green80 = "#41605E";
    internal const string Green60 = "#718886";
    internal const string Green40 = "#A0AFAF";
    internal const string Coral = "#FF6359";
    // The status extension, as in the app's palette.
    internal const string InProgressBlue = "#3D6A8A";
    internal const string ReworkingLavender = "#B58BD0";
    internal const string BlockedBrick = "#9E2B25";
    internal const string CompletedGreen = "#A8D5A2";
    internal const string WaitingAmber = "#D9922E";

    /// <summary>Every development status, in the order the app lists them.</summary>
    internal static IReadOnlyList<string> StatusOrder { get; } =
    [
        Status(DevelopmentStatus.NotStarted),
        Status(DevelopmentStatus.Blocked),
        Status(DevelopmentStatus.InProgress),
        Status(DevelopmentStatus.ReworkNeeded),
        Status(DevelopmentStatus.Reworking),
        Status(DevelopmentStatus.DevelopmentCompleted),
        Status(DevelopmentStatus.Reconciled)
    ];

    /// <summary>Every work status of an active entity, in the order the app lists them.</summary>
    internal static IReadOnlyList<string> WorkStatusOrder { get; } =
    [
        WorkStatus(EntityWorkflowState.Ready),
        WorkStatus(EntityWorkflowState.Blocked),
        WorkStatus(EntityWorkflowState.ManuallyBlocked),
        WorkStatus(EntityWorkflowState.InProgress),
        WorkStatus(EntityWorkflowState.DevelopmentCompleted),
        WorkStatus(EntityWorkflowState.Reconciled)
    ];

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
        ProgressStatusCategory.InProgress => InProgressBlue,
        ProgressStatusCategory.Reworking => ReworkingLavender,
        ProgressStatusCategory.Blocked => BlockedBrick,
        ProgressStatusCategory.DevelopmentCompleted => CompletedGreen,
        ProgressStatusCategory.Reconciled => Green100,
        _ => Coral
    };

    public static string Status(DevelopmentStatus status) => status switch
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
    public static string WorkStatus(EntityWorkflowState state) => state switch
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
