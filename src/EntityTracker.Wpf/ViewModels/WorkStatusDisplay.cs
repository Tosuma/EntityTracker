using EntityTracker.Application.Workflow;

namespace EntityTracker.Wpf.ViewModels;

public enum WorkStatusDisplay
{
    Ready,
    Blocked,
    InProgress,
    Completed,
    Reconciled,
    Archived
}

public static class WorkStatusDisplayMapper
{
    public static WorkStatusDisplay From(EntityWorkflowState state) => state switch
    {
        EntityWorkflowState.Ready => WorkStatusDisplay.Ready,
        EntityWorkflowState.Blocked => WorkStatusDisplay.Blocked,
        EntityWorkflowState.InProgress or EntityWorkflowState.ReworkNeeded => WorkStatusDisplay.InProgress,
        EntityWorkflowState.DevelopmentCompleted => WorkStatusDisplay.Completed,
        EntityWorkflowState.Reconciled => WorkStatusDisplay.Reconciled,
        EntityWorkflowState.Archived => WorkStatusDisplay.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static string Format(WorkStatusDisplay status) => status switch
    {
        WorkStatusDisplay.Ready => "Ready",
        WorkStatusDisplay.Blocked => "Blocked",
        WorkStatusDisplay.InProgress => "In progress",
        WorkStatusDisplay.Completed => "Completed",
        WorkStatusDisplay.Reconciled => "Reconciled",
        WorkStatusDisplay.Archived => "Archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
