using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.Tests;

public sealed class AggregateProgressDashboardBuilderTests
{
    private readonly AggregateProgressDashboardBuilder _builder = new();

    [Fact]
    public void Build_CarriesEachTrackerStateAndWeightsEntities()
    {
        TrackerId large = TrackerId.New();
        TrackerId small = TrackerId.New();
        Dictionary<TrackerId, IReadOnlyList<ProgressSnapshot>> histories = new()
        {
            [large] =
            [
                Snapshot(1, ready: 9),
                Snapshot(3, completed: 9)
            ],
            [small] =
            [
                Snapshot(1, ready: 1),
                Snapshot(4, reconciled: 1)
            ]
        };

        ProgressDashboardReport report = _builder.Build(
            histories,
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 4),
            TimeZoneInfo.Utc);

        Assert.Equal(10, report.ManagerSummary.ActiveEntityCount);
        Assert.Equal(10, report.ManagerSummary.ImplementedEntityCount);
        Assert.Equal([0, 0, 9, 10], report.ImplementedOverTime.Select(static point => point.ImplementedCount));
        Assert.Equal(9, report.CurrentStatusCounts.Single(
            static item => item.Status == ProgressStatusCategory.DevelopmentCompleted).Count);
        Assert.Equal(1, report.CurrentStatusCounts.Single(
            static item => item.Status == ProgressStatusCategory.Reconciled).Count);
    }

    [Fact]
    public void Build_SameTimestampIsDeterministicAndEmptyScopeHasNoData()
    {
        DateTimeOffset timestamp = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        Dictionary<TrackerId, IReadOnlyList<ProgressSnapshot>> histories = new()
        {
            [new TrackerId(Guid.Parse("00000000-0000-0000-0000-000000000002"))] =
                [new ProgressSnapshot(timestamp, new ProgressSnapshotState(0, 1, 0, 0, 0, 0))],
            [new TrackerId(Guid.Parse("00000000-0000-0000-0000-000000000001"))] =
                [new ProgressSnapshot(timestamp, new ProgressSnapshotState(2, 0, 0, 0, 0, 0))]
        };

        ProgressDashboardReport report = _builder.Build(
            histories,
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 2),
            TimeZoneInfo.Utc);
        ProgressDashboardReport empty = _builder.Build(
            new Dictionary<TrackerId, IReadOnlyList<ProgressSnapshot>>(),
            ProgressDateRange.AllHistory,
            new DateOnly(2026, 1, 2),
            TimeZoneInfo.Utc);

        Assert.Equal(2, report.ManagerSummary.ReadyEntityCount);
        Assert.Equal(1, report.ManagerSummary.BlockedEntityCount);
        Assert.Single(report.ImplementedOverTime);
        Assert.False(empty.HasHistoricalData);
        Assert.Equal(ProgressManagerSummary.Empty, empty.ManagerSummary);
    }

    private static ProgressSnapshot Snapshot(
        int day,
        int ready = 0,
        int blocked = 0,
        int completed = 0,
        int reconciled = 0) =>
        new(
            new DateTimeOffset(2026, 1, day, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(ready, blocked, 0, 0, completed, reconciled));
}
