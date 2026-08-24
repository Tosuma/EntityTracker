using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProgressHistoryRepository
{
    Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
        CancellationToken cancellationToken = default);
}
