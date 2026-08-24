namespace EntityTracker.Application.Workflow;

public enum EntityWorkflowState
{
    Ready,
    Blocked,
    InProgress,
    ReworkNeeded,
    DevelopmentCompleted,
    Reconciled,
    Archived
}
