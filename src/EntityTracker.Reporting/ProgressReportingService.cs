using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;

namespace EntityTracker.Reporting;

public sealed class ProgressReportingService
{
    private readonly IProgressHistoryRepository _repository;
    private readonly ProgressDashboardBuilder _builder;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeProvider _timeProvider;

    public ProgressReportingService(
        IProgressHistoryRepository repository,
        TimeZoneInfo timeZone,
        ProgressDashboardBuilder? builder = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeZone);
        _repository = repository;
        _timeZone = timeZone;
        _builder = builder ?? new ProgressDashboardBuilder();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProgressDashboardReport> GetReportAsync(
        ProgressDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        IReadOnlyList<ProgressSnapshot> snapshots =
            await _repository.GetProgressSnapshotsAsync(cancellationToken);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);
        return _builder.Build(snapshots, range, today, _timeZone);
    }
}
