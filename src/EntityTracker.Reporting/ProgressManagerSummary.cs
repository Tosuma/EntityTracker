namespace EntityTracker.Reporting;

public sealed record ProgressManagerSummary(
    int ActiveEntityCount,
    int ImplementedEntityCount,
    int ReconciledEntityCount,
    int ReadyEntityCount,
    int BlockedEntityCount,
    DateOnly? DataAsOfDate)
{
    public static ProgressManagerSummary Empty { get; } = new(0, 0, 0, 0, 0, null);
}
