namespace EntityTracker.Application.Workflow;

public enum EntityWorkflowState
{
    Ready,
    Blocked,
    InProgress,
    DevelopmentCompleted,
    Reconciled,
    Archived
}
