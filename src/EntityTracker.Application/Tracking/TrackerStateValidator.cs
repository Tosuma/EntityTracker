using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public static class TrackerStateValidator
{
    public static void EnsureOwned(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<EntityDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dependencies);

        TrackedEntity[] entityArray = entities.ToArray();
        if (entityArray.Any(entity => entity.TrackerId != trackerId))
        {
            throw new InvalidDataException("Tracker state contains an entity owned by another tracker.");
        }

        HashSet<EntityId> entityIds = entityArray.Select(static entity => entity.Id).ToHashSet();
        if (dependencies.Any(dependency => !entityIds.Contains(dependency.DependentEntityId)))
        {
            throw new InvalidDataException(
                "A dependency is owned by an entity outside the tracker.");
        }
    }
}
