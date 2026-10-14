using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

/// <summary>Returns a fixed dependency list for any Tracker.</summary>
internal sealed class StubDependencyRepository(IReadOnlyList<EntityDependency>? dependencies = null)
    : IDependencyRepository
{
    public Task<IReadOnlyList<EntityDependency>> GetAllAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(dependencies ?? (IReadOnlyList<EntityDependency>)[]);
}
