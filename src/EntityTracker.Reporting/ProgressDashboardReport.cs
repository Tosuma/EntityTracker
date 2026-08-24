namespace EntityTracker.Reporting;

public sealed record ProgressStatusCount(ProgressStatusCategory Status, int Count);

public sealed record ImplementedProgressPoint(DateOnly Date, int ImplementedCount);

public sealed record ReadinessProgressPoint(DateOnly Date, int ReadyCount, int BlockedCount);

public sealed record WeeklyImplementedChange(DateOnly WeekStartingMonday, int NetChange);

public sealed record ProgressDashboardReport(
    IReadOnlyList<ProgressStatusCount> CurrentStatusCounts,
    IReadOnlyList<ImplementedProgressPoint> ImplementedOverTime,
    IReadOnlyList<ReadinessProgressPoint> ReadyAndBlockedOverTime,
    IReadOnlyList<WeeklyImplementedChange> WeeklyNetImplementedChange,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo)
{
    public bool HasHistoricalData => ImplementedOverTime.Count > 0;
}
