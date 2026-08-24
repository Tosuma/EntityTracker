using EntityTracker.Application.History;

namespace EntityTracker.Reporting;

public sealed class ProgressDashboardBuilder
{
    public ProgressDashboardReport Build(
        IEnumerable<ProgressSnapshot> snapshots,
        ProgressDateRange range,
        DateOnly today,
        TimeZoneInfo timeZone)
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
            new(ProgressStatusCategory.InProgress, current.InProgressCount),
            new(ProgressStatusCategory.ReworkNeeded, current.ReworkNeededCount),
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
                current.BlockedCount,
                DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(ordered[^1].RecordedAtUtc, timeZone).DateTime));

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

        IReadOnlyList<WeeklyImplementedChange> weekly = BuildWeeklyChanges(
            implemented,
            dated.Where(item => item.Date < from).Select(static item => item.State).LastOrDefault());
        return new ProgressDashboardReport(
            managerSummary,
            currentCounts,
            implemented,
            readiness,
            weekly,
            implemented.FirstOrDefault()?.Date,
            implemented.LastOrDefault()?.Date);
    }

    private static IReadOnlyList<WeeklyImplementedChange> BuildWeeklyChanges(
        IReadOnlyList<ImplementedProgressPoint> daily,
        ProgressSnapshotState? stateBeforeRange)
    {
        if (daily.Count == 0)
        {
            return [];
        }

        int previous = stateBeforeRange?.ImplementedCount ?? daily[0].ImplementedCount;
        List<WeeklyImplementedChange> result = [];
        foreach (IGrouping<DateOnly, ImplementedProgressPoint> week in daily.GroupBy(
                     static point => StartOfWeek(point.Date)))
        {
            int end = week.Last().ImplementedCount;
            result.Add(new WeeklyImplementedChange(week.Key, end - previous));
            previous = end;
        }

        return result;
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
