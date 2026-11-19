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
/// implemented (both counted up, from one saved progress state to the next), and the entities in
/// progress on the week's last day with data: its Sunday, or the last day of the period when the week
/// isn't over.
/// </summary>
public sealed record WeeklyImplementedChange(
    DateOnly WeekStartingMonday, int Increase, int Decrease, int InProgressAtEnd = 0)
{
    /// <summary>Gets the entities that became implemented minus those that stopped; below zero when more stopped.</summary>
    public int Net => Increase - Decrease;
}

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
