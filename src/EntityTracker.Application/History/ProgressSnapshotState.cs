using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

public sealed record ProgressSnapshotState
{
    public ProgressSnapshotState(
        int readyCount,
        int blockedCount,
        int inProgressCount,
        int reworkNeededCount,
        int developmentCompletedCount,
        int reconciledCount,
        int manuallyBlockedCount = 0,
        int reworkingCount = 0)
    {
        if (readyCount < 0 || blockedCount < 0 || inProgressCount < 0 ||
            reworkNeededCount < 0 || developmentCompletedCount < 0 || reconciledCount < 0 ||
            manuallyBlockedCount < 0 || reworkingCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readyCount),
                "Progress snapshot counts cannot be negative.");
        }

        ReadyCount = readyCount;
        BlockedCount = blockedCount;
        InProgressCount = inProgressCount;
        ReworkNeededCount = reworkNeededCount;
        DevelopmentCompletedCount = developmentCompletedCount;
        ReconciledCount = reconciledCount;
        ManuallyBlockedCount = manuallyBlockedCount;
        ReworkingCount = reworkingCount;
    }

    public int ReadyCount { get; }
    public int BlockedCount { get; }
    public int InProgressCount { get; }
    public int ReworkNeededCount { get; }
    public int DevelopmentCompletedCount { get; }
    public int ReconciledCount { get; }
    public int ManuallyBlockedCount { get; }
    public int ReworkingCount { get; }
    public int WaitingOnDependenciesCount => BlockedCount;
    public int NotStartedCount => ReadyCount + BlockedCount;
    public int ImplementedCount => ReworkNeededCount + ReworkingCount +
        DevelopmentCompletedCount + ReconciledCount;
    public int TotalActiveCount =>
        ReadyCount + BlockedCount + InProgressCount + ReworkNeededCount +
        DevelopmentCompletedCount + ReconciledCount + ManuallyBlockedCount + ReworkingCount;
}
