using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Reads durable manual dependency corrections independently of imported relationships.
/// </summary>
public interface IManualDependencyOverrideRepository
{
    Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
