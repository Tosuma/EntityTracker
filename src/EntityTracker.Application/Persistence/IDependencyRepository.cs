using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IDependencyRepository
{
    Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
