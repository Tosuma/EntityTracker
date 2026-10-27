namespace EntityTracker.Reporting;

public sealed record ProgressStatusCount(ProgressStatusCategory Status, int Count);

/// <summary>
/// A day's counts for the Progress over time chart: implemented entities and, next to them, the ones in
/// progress, not started (ready or waiting on dependencies) and blocked.
/// </summary>
public sealed record ImplementedProgressPoint(
    DateOnly Date, int ImplementedCount, int InProgressCount = 0, int NotStartedCount = 0, int BlockedCount = 0);

public sealed record ReadinessProgressPoint(DateOnly Date, int ReadyCount, int BlockedCount);

/// <summary>
/// A week's change in implemented entities: how many became implemented and how many stopped being
/// implemented (both counted up, from one saved progress state to the next).
/// </summary>
public sealed record WeeklyImplementedChange(DateOnly WeekStartingMonday, int Increase, int Decrease);

public sealed record ProgressDashboardReport(
    ProgressManagerSummary ManagerSummary,
    IReadOnlyList<ProgressStatusCount> CurrentStatusCounts,
    IReadOnlyList<ImplementedProgressPoint> ImplementedOverTime,
    IReadOnlyList<ReadinessProgressPoint> ReadyAndBlockedOverTime,
    IReadOnlyList<WeeklyImplementedChange> WeeklyNetImplementedChange,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo)
{
    public bool HasHistoricalData => ImplementedOverTime.Count > 0;
}
