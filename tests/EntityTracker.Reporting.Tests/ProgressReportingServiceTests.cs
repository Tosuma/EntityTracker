using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.Tests;

public sealed class ProgressReportingServiceTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadyVsWaiting_ComesFromTheHistoriesAndStartsAtTheBaseline()
    {
        // #179: the saved totals say 5 waiting every day; the histories say one entity waits from the
        // baseline on day 2 until its dependency is removed on day 3.
        TrackerId tracker = TrackerId.New();
        EntityId customer = EntityId.New();
        EntityId invoice = EntityId.New();
        StubHistory history = new(
            [new ProgressSnapshot(Day1, new ProgressSnapshotState(0, 5, 1, 0, 0, 0))],
            [
                new EntityStatusHistoryEntry(customer, null, DevelopmentStatus.NotStarted, Day1, StatusHistoryEntryKind.Baseline),
                new EntityStatusHistoryEntry(invoice, null, DevelopmentStatus.NotStarted, Day1, StatusHistoryEntryKind.Baseline),
                new EntityStatusHistoryEntry(customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1,
                    StatusHistoryEntryKind.Transition)
            ],
            [
                new DependencyHistoryEntry(Guid.NewGuid(), customer, DependencyHistoryChange.Lifecycle, Day1.AddDays(1),
                    lifecycleState: EntityLifecycleState.Active, isBaseline: true),
                new DependencyHistoryEntry(Guid.NewGuid(), invoice, DependencyHistoryChange.Lifecycle, Day1.AddDays(1),
                    lifecycleState: EntityLifecycleState.Active, isBaseline: true),
                new DependencyHistoryEntry(Guid.NewGuid(), invoice, DependencyHistoryChange.DependencyAdded, Day1.AddDays(1),
                    customer, isBaseline: true),
                new DependencyHistoryEntry(Guid.NewGuid(), invoice, DependencyHistoryChange.DependencyRemoved, Day1.AddDays(2),
                    customer)
            ]);
        ProgressReportingService service = new(history, TimeZoneInfo.Utc, timeProvider: new FixedTime(Day1.AddDays(3)));

        ProgressDashboardReport report = await service.GetReportAsync(tracker, ProgressDateRange.AllHistory);

        Assert.Equal(DateOnly.FromDateTime(Day1.Date), report.ImplementedOverTime[0].Date);
        Assert.Equal(
            [(DateOnly.FromDateTime(Day1.AddDays(1).Date), 0, 1), (DateOnly.FromDateTime(Day1.AddDays(2).Date), 1, 0),
             (DateOnly.FromDateTime(Day1.AddDays(3).Date), 1, 0)],
            report.ReadyAndBlockedOverTime.Select(point => (point.Date, point.ReadyCount, point.BlockedCount)));
    }

    private sealed class StubHistory(
        IReadOnlyList<ProgressSnapshot> snapshots,
        IReadOnlyList<EntityStatusHistoryEntry> statuses,
        IReadOnlyList<DependencyHistoryEntry> dependencies) : IProgressHistoryRepository
    {
        public Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
            TrackerId trackerId, CancellationToken cancellationToken = default) => Task.FromResult(statuses);

        public Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
            TrackerId trackerId, CancellationToken cancellationToken = default) => Task.FromResult(snapshots);

        public Task<IReadOnlyList<DependencyHistoryEntry>> GetDependencyHistoryAsync(
            TrackerId trackerId, CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<ProgressSnapshot?> GetLatestProgressSnapshotAsync(
            TrackerId trackerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshots.LastOrDefault());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
