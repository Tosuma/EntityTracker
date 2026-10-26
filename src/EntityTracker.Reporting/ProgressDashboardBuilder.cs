using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Reporting;

public sealed class ProgressDashboardBuilder
{
    /// <param name="statusHistory">
    /// The entities' status changes, so the weekly chart counts each entity that became, or stopped
    /// being, implemented. Without it, the weekly changes come from the steps between saved progress
    /// states, where opposite moves in one save cancel out.
    /// </param>
    public ProgressDashboardReport Build(
        IEnumerable<ProgressSnapshot> snapshots,
        ProgressDateRange range,
        DateOnly today,
        TimeZoneInfo timeZone,
        IEnumerable<EntityStatusHistoryEntry>? statusHistory = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (range.To is { } requestedTo && requestedTo > today)
        {
            throw new ArgumentException("The end date cannot be in the future.", nameof(range));
        }

        ProgressSnapshot[] ordered = snapshots
            .OrderBy(static snapshot => snapshot.RecordedAtUtc)
            .ToArray();
        ProgressSnapshotState current = ordered.LastOrDefault()?.State ??
            new ProgressSnapshotState(0, 0, 0, 0, 0, 0);
        ProgressStatusCount[] currentCounts =
        [
            new(ProgressStatusCategory.NotStarted, current.NotStartedCount),
            new(ProgressStatusCategory.Blocked, current.ManuallyBlockedCount),
            new(ProgressStatusCategory.InProgress, current.InProgressCount),
            new(ProgressStatusCategory.ReworkNeeded, current.ReworkNeededCount),
            new(ProgressStatusCategory.Reworking, current.ReworkingCount),
            new(ProgressStatusCategory.DevelopmentCompleted, current.DevelopmentCompletedCount),
            new(ProgressStatusCategory.Reconciled, current.ReconciledCount)
        ];
        ProgressManagerSummary managerSummary = ordered.Length == 0
            ? ProgressManagerSummary.Empty
            : new ProgressManagerSummary(
                current.TotalActiveCount,
                current.ImplementedCount,
                current.ReconciledCount,
                current.ReadyCount,
                current.WaitingOnDependenciesCount,
                DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(ordered[^1].RecordedAtUtc, timeZone).DateTime),
                current.ManuallyBlockedCount,
                current.DevelopmentCompletedCount,
                current.ReworkNeededCount,
                current.ReworkingCount);

        if (ordered.Length == 0)
        {
            return new ProgressDashboardReport(
                managerSummary,
                currentCounts,
                [],
                [],
                [],
                null,
                null);
        }

        (DateOnly Date, ProgressSnapshotState State)[] dated = ordered
            .Select(snapshot => (
                Date: DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(snapshot.RecordedAtUtc, timeZone).DateTime),
                snapshot.State))
            .ToArray();
        DateOnly requestedFrom = range.From ?? dated[0].Date;
        DateOnly to = range.To ?? today;
        DateOnly from = requestedFrom < dated[0].Date ? dated[0].Date : requestedFrom;
        if (from > to)
        {
            return new ProgressDashboardReport(
                managerSummary,
                currentCounts,
                [],
                [],
                [],
                null,
                null);
        }

        List<ImplementedProgressPoint> implemented = [];
        List<ReadinessProgressPoint> readiness = [];
        ProgressSnapshotState? carried = dated
            .Where(item => item.Date < from)
            .Select(static item => item.State)
            .LastOrDefault();
        int snapshotIndex = 0;
        while (snapshotIndex < dated.Length && dated[snapshotIndex].Date < from)
        {
            snapshotIndex++;
        }

        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            while (snapshotIndex < dated.Length && dated[snapshotIndex].Date <= date)
            {
                carried = dated[snapshotIndex].State;
                snapshotIndex++;
            }

            if (carried is null)
            {
                continue;
            }

            implemented.Add(new ImplementedProgressPoint(date, carried.ImplementedCount));
            readiness.Add(new ReadinessProgressPoint(
                date,
                carried.ReadyCount,
                carried.BlockedCount));
        }

        IEnumerable<(DateOnly Date, int Step)> moves = statusHistory is null
            ? SnapshotSteps(
                dated.Where(item => item.Date >= from && item.Date <= to),
                dated.Where(item => item.Date < from).Select(static item => item.State).LastOrDefault())
            : StatusMoves(statusHistory, timeZone)
                .Where(move => move.Date >= from && move.Date <= to);
        IReadOnlyList<WeeklyImplementedChange> weekly = BuildWeeklyChanges(implemented, moves);
        return new ProgressDashboardReport(
            managerSummary,
            currentCounts,
            implemented,
            readiness,
            weekly,
            implemented.FirstOrDefault()?.Date,
            implemented.LastOrDefault()?.Date);
    }

    /// <summary>
    /// Each week of the range with how many entities became implemented and how many stopped being
    /// implemented. Ups and downs in the same week are counted apart, so they don't cancel out.
    /// </summary>
    private static IReadOnlyList<WeeklyImplementedChange> BuildWeeklyChanges(
        IReadOnlyList<ImplementedProgressPoint> daily,
        IEnumerable<(DateOnly Date, int Step)> moves)
    {
        Dictionary<DateOnly, (int Increase, int Decrease)> weeks = daily
            .Select(static point => StartOfWeek(point.Date))
            .Distinct()
            .ToDictionary(static week => week, static _ => (0, 0));
        foreach ((DateOnly date, int step) in moves)
        {
            DateOnly week = StartOfWeek(date);
            if (!weeks.TryGetValue(week, out (int Increase, int Decrease) counts)) continue;
            weeks[week] = step >= 0 ? (counts.Increase + step, counts.Decrease) : (counts.Increase, counts.Decrease - step);
        }

        return weeks
            .OrderBy(static week => week.Key)
            .Select(static week => new WeeklyImplementedChange(week.Key, week.Value.Increase, week.Value.Decrease))
            .ToArray();
    }

    /// <summary>
    /// Every status change that moved an entity into or out of an implemented status: +1 or -1 on the
    /// day it happened. Archiving and restoring change no status, so they are not counted.
    /// </summary>
    private static IEnumerable<(DateOnly Date, int Step)> StatusMoves(
        IEnumerable<EntityStatusHistoryEntry> statusHistory, TimeZoneInfo timeZone)
    {
        foreach (EntityStatusHistoryEntry entry in statusHistory)
        {
            if (entry.Kind != StatusHistoryEntryKind.Transition || entry.PreviousStatus is not { } previous)
                continue;
            bool was = ProgressSnapshotState.ImplementedStatuses.Contains(previous);
            bool now = ProgressSnapshotState.ImplementedStatuses.Contains(entry.NewStatus);
            if (was == now) continue;
            yield return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.OccurredAtUtc, timeZone).DateTime),
                now ? 1 : -1);
        }
    }

    /// <summary>
    /// The change in implemented entities from each saved progress state to the next, for a caller without
    /// the status history. A save that moves entities both ways counts as its net step.
    /// </summary>
    private static IEnumerable<(DateOnly Date, int Step)> SnapshotSteps(
        IEnumerable<(DateOnly Date, ProgressSnapshotState State)> snapshotsInRange,
        ProgressSnapshotState? stateBeforeRange)
    {
        int? previous = stateBeforeRange?.ImplementedCount;
        foreach ((DateOnly date, ProgressSnapshotState state) in snapshotsInRange)
        {
            yield return (date, state.ImplementedCount - (previous ?? state.ImplementedCount));
            previous = state.ImplementedCount;
        }
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
