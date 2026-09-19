using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Reporting;

public sealed class AggregateProgressDashboardBuilder(
    ProgressDashboardBuilder? dashboardBuilder = null)
{
    private readonly ProgressDashboardBuilder _dashboardBuilder =
        dashboardBuilder ?? new ProgressDashboardBuilder();

    public ProgressDashboardReport Build(
        IReadOnlyDictionary<TrackerId, IReadOnlyList<ProgressSnapshot>> trackerSnapshots,
        ProgressDateRange range,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(trackerSnapshots);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(timeZone);

        var events = trackerSnapshots
            .SelectMany(pair => pair.Value.Select(snapshot => new
            {
                TrackerId = pair.Key,
                Snapshot = snapshot
            }))
            .OrderBy(static item => item.Snapshot.RecordedAtUtc)
            .ThenBy(static item => item.TrackerId.Value)
            .ToArray();
        Dictionary<TrackerId, ProgressSnapshotState> current = [];
        List<ProgressSnapshot> aggregate = [];
        foreach (var timestampGroup in events.GroupBy(
                     static item => item.Snapshot.RecordedAtUtc))
        {
            foreach (var item in timestampGroup.OrderBy(static item => item.TrackerId.Value))
            {
                current[item.TrackerId] = item.Snapshot.State;
            }

            ProgressSnapshotState state = new(
                current.Values.Sum(static item => item.ReadyCount),
                current.Values.Sum(static item => item.BlockedCount),
                current.Values.Sum(static item => item.InProgressCount),
                current.Values.Sum(static item => item.ReworkNeededCount),
                current.Values.Sum(static item => item.DevelopmentCompletedCount),
                current.Values.Sum(static item => item.ReconciledCount));
            aggregate.Add(new ProgressSnapshot(timestampGroup.Key, state));
        }

        return _dashboardBuilder.Build(aggregate, range, today, timeZone);
    }
}
