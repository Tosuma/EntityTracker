using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetAsync(
        EntityId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates imported schema fields without changing status or notes.
    /// </summary>
    Task<bool> UpdateSchemaMetadataAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates status and notes without changing imported schema fields.
    /// </summary>
    Task<bool> UpdateProgressAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default);
}
