using EntityTracker.Application.History;
using EntityTracker.Domain;

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
    public void Build_ManagerSummaryUsesLatestSnapshotAndLocalDataDateRegardlessOfRange()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "TestPlusTwo",
            TimeSpan.FromHours(2),
            "TestPlusTwo",
            "TestPlusTwo");
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 1, ready: 1),
            new ProgressSnapshot(
                new DateTimeOffset(2026, 2, 1, 23, 30, 0, TimeSpan.Zero),
                new ProgressSnapshotState(2, 3, 4, 5, 6, 7))
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots,
            ProgressDateRange.Inclusive(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)),
            new DateOnly(2026, 2, 2),
            timeZone);

        Assert.Equal(27, report.ManagerSummary.ActiveEntityCount);
        Assert.Equal(18, report.ManagerSummary.ImplementedEntityCount);
        Assert.Equal(7, report.ManagerSummary.ReconciledEntityCount);
        Assert.Equal(2, report.ManagerSummary.ReadyEntityCount);
        Assert.Equal(3, report.ManagerSummary.WaitingOnDependenciesEntityCount);
        Assert.Equal(new DateOnly(2026, 2, 2), report.ManagerSummary.DataAsOfDate);
    }

    [Fact]
    public void Build_ReportsManualBlockAndActiveReworkSeparately()
    {
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(0, 1, 0, 1, 0, 0,
                manuallyBlockedCount: 2, reworkingCount: 3));

        ProgressDashboardReport report = _builder.Build(
            [snapshot], ProgressDateRange.AllHistory, new DateOnly(2026, 2, 1), Utc);

        Assert.Equal(2, Count(report, ProgressStatusCategory.Blocked));
        Assert.Equal(3, Count(report, ProgressStatusCategory.Reworking));
        Assert.Equal(4, report.ManagerSummary.ImplementedEntityCount);
        Assert.Equal(1, report.ManagerSummary.WaitingOnDependenciesEntityCount);
        Assert.Equal(2, report.ManagerSummary.ManuallyBlockedEntityCount);
        Assert.Equal(7, report.ManagerSummary.ActiveEntityCount);
    }

    [Fact]
    public void Build_EmptyHistoryReturnsEmptyManagerSummary()
    {
        ProgressDashboardReport report = _builder.Build(
            [],
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 1),
            Utc);

        Assert.Equal(ProgressManagerSummary.Empty, report.ManagerSummary);
        Assert.False(report.HasHistoricalData);
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
        Assert.Equal((0, 2), (report.WeeklyNetImplementedChange.Single().Increase,
            report.WeeklyNetImplementedChange.Single().Decrease));
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

    [Fact]
    public void Build_CountsEachEntityThatBecameOrStoppedBeingImplementedFromTheStatusHistory()
    {
        // One save moves one entity to Completed and another back to In progress: the saved totals don't
        // change, but the history shows both.
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 5, inProgress: 1, completed: 1),
            Snapshot(2026, 1, 6, inProgress: 1, completed: 1),
            Snapshot(2026, 1, 13, completed: 2)
        ];
        EntityId first = EntityId.New(), second = EntityId.New(), third = EntityId.New();
        EntityStatusHistoryEntry[] history =
        [
            Moved(first, DevelopmentStatus.InProgress, DevelopmentStatus.DevelopmentCompleted, 2026, 1, 6),
            Moved(second, DevelopmentStatus.DevelopmentCompleted, DevelopmentStatus.InProgress, 2026, 1, 6),
            // Between two implemented statuses, and between two unimplemented ones: no change.
            Moved(first, DevelopmentStatus.DevelopmentCompleted, DevelopmentStatus.Reconciled, 2026, 1, 7),
            Moved(third, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, 2026, 1, 7),
            Moved(second, DevelopmentStatus.InProgress, DevelopmentStatus.ReworkNeeded, 2026, 1, 13),
            // Before the range: not counted.
            Moved(third, DevelopmentStatus.InProgress, DevelopmentStatus.DevelopmentCompleted, 2025, 12, 1)
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots,
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 14),
            Utc,
            history);

        Assert.Equal(
        [
            new WeeklyImplementedChange(new DateOnly(2026, 1, 5), 1, 1),
            new WeeklyImplementedChange(new DateOnly(2026, 1, 12), 1, 0)
        ], report.WeeklyNetImplementedChange);
    }

    [Fact]
    public void Build_WithoutStatusHistoryCountsTheStepsBetweenSavedStates()
    {
        ProgressSnapshot[] snapshots =
        [
            Snapshot(2026, 1, 5, inProgress: 2),
            Snapshot(2026, 1, 6, completed: 2),
            Snapshot(2026, 1, 7, inProgress: 1, completed: 1)
        ];

        ProgressDashboardReport report = _builder.Build(
            snapshots, ProgressDateRange.AllHistory, new DateOnly(2026, 1, 7), Utc);

        Assert.Equal([new WeeklyImplementedChange(new DateOnly(2026, 1, 5), 2, 1)], report.WeeklyNetImplementedChange);
    }

    private static EntityStatusHistoryEntry Moved(
        EntityId entity, DevelopmentStatus from, DevelopmentStatus to, int year, int month, int day) =>
        new(entity, from, to, new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero),
            StatusHistoryEntryKind.Transition);

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
