namespace EntityTracker.Reporting;

/// <param name="WaitingOnDependenciesEntityCount">Entities whose work waits on an unfinished or unresolved dependency.</param>
/// <param name="ManuallyBlockedEntityCount">Entities whose development status was set to Blocked.</param>
/// <param name="DevelopmentCompletedEntityCount">The Completed part of Implemented.</param>
/// <param name="ReworkNeededEntityCount">The Rework needed part of Implemented.</param>
/// <param name="ReworkingEntityCount">The Reworking part of Implemented.</param>
public sealed record ProgressManagerSummary(
    int ActiveEntityCount,
    int ImplementedEntityCount,
    int ReconciledEntityCount,
    int ReadyEntityCount,
    int WaitingOnDependenciesEntityCount,
    DateOnly? DataAsOfDate,
    int ManuallyBlockedEntityCount = 0,
    int DevelopmentCompletedEntityCount = 0,
    int ReworkNeededEntityCount = 0,
    int ReworkingEntityCount = 0)
{
    public static ProgressManagerSummary Empty { get; } = new(0, 0, 0, 0, 0, null);
}
