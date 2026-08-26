namespace EntityTracker.Application.Workflow;

/// <summary>
/// Reports how many uniquely selected entities changed or already matched the target status.
/// </summary>
public sealed record BulkStatusUpdateResult(int ChangedCount, int UnchangedCount)
{
    public int SelectedCount => ChangedCount + UnchangedCount;
}
