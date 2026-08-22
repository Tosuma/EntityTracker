namespace EntityTracker.Application.Persistence;

public interface IDependencyRepository
{
    Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PersistedDependency dependency,
        CancellationToken cancellationToken = default);
}
