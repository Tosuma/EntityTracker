using EntityTracker.Application.History;

namespace EntityTracker.Reporting.Tests;

public sealed class ProgressDashboardBuilderTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly ProgressDashboardBuilder _builder = new();

    [Fact]
    public void Build_UsesLatestSnapshotForCurrentDistributionRegardlessOfRange()
    {
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 1, ready: 2, blocked: 1),
            Snapshot(2026, 2, 1, rework: 1, completed: 2, reconciled: 3)
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots,
            ProgressDateRange.Inclusive(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)),
            new DateOnly(2026, 2, 2),
            Utc);

        Assert.Equal(1, Count(report, ProgressStatusCategory.ReworkNeeded));
        Assert.Equal(2, Count(report, ProgressStatusCategory.DevelopmentCompleted));
        Assert.Equal(3, Count(report, ProgressStatusCategory.Reconciled));
    }

    [Fact]
    public void Build_CarriesStateIntoInclusiveRange()
    {
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 1, ready: 3, completed: 1),
            Snapshot(2026, 1, 4, ready: 2, completed: 2)
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots,
            ProgressDateRange.Inclusive(new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4)),
            new DateOnly(2026, 1, 4),
            Utc);

        Assert.Equal([1, 2], report.ImplementedOverTime.Select(static point => point.ImplementedCount));
        Assert.Equal(new DateOnly(2026, 1, 3), report.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 1, 4), report.EffectiveTo);
    }

    [Fact]
    public void Build_ImplementedCountIncludesReworkAndCanDecrease()
    {
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 1, rework: 1, completed: 2, reconciled: 1),
            Snapshot(2026, 1, 2, inProgress: 1, completed: 1, reconciled: 1)
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots,
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 2),
            Utc);

        Assert.Equal([4, 2], report.ImplementedOverTime.Select(static point => point.ImplementedCount));
        Assert.Contains(report.WeeklyNetImplementedChange, static point => point.NetChange < 0);
    }

    [Fact]
    public void Build_UsesLocalDatesAndMondayWeeks()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "TestPlusTwo",
            TimeSpan.FromHours(2),
            "TestPlusTwo",
            "TestPlusTwo");
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 1, 4, 23, 30, 0, TimeSpan.Zero),
            new ProgressSnapshotState(1, 0, 0, 0, 0, 0));

        ProgressDashboardReport report = _builder.Build(
            [snapshot],
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 5),
            timeZone);

        Assert.Equal(new DateOnly(2026, 1, 5), report.ImplementedOverTime.Single().Date);
        Assert.Equal(new DateOnly(2026, 1, 5), report.WeeklyNetImplementedChange.Single().WeekStartingMonday);
    }

    [Fact]
    public void Build_RejectsFutureEndDate()
    {
        Assert.Throws<ArgumentException>(() => _builder.Build(
            [Snapshot(2026, 1, 1)],
            ProgressDateRange.Inclusive(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)),
            new DateOnly(2026, 1, 2),
            Utc));
    }

    private static int Count(ProgressDashboardReport report, ProgressStatusCategory status) =>
        report.CurrentStatusCounts.Single(item => item.Status == status).Count;

    private static ProgressSnapshot Snapshot(
        int year,
        int month,
        int day,
        int ready = 0,
        int blocked = 0,
        int inProgress = 0,
        int rework = 0,
        int completed = 0,
        int reconciled = 0) =>
        new(
            new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(
                ready,
                blocked,
                inProgress,
                rework,
                completed,
                reconciled));
}
