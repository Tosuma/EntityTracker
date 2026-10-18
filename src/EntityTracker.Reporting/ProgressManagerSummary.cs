namespace EntityTracker.Reporting;

/// <param name="WaitingOnDependenciesEntityCount">Entities whose work waits on an unfinished or unresolved dependency.</param>
/// <param name="ManuallyBlockedEntityCount">Entities whose development status was set to Blocked.</param>
public sealed record ProgressManagerSummary(
    int ActiveEntityCount,
    int ImplementedEntityCount,
    int ReconciledEntityCount,
    int ReadyEntityCount,
    int WaitingOnDependenciesEntityCount,
    DateOnly? DataAsOfDate,
    int ManuallyBlockedEntityCount = 0)
{
    public static ProgressManagerSummary Empty { get; } = new(0, 0, 0, 0, 0, null);
}
