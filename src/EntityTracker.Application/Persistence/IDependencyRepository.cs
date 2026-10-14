using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>Reads the dependencies the entities of a Tracker declare, by name.</summary>
public interface IDependencyRepository
{
    Task<IReadOnlyList<EntityDependency>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}
