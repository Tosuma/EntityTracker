using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public static class TrackerStateValidator
{
    public static void EnsureOwned(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> resolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies,
        IEnumerable<ManualDependencyOverride> manualOverrides)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(resolvedDependencies);
        ArgumentNullException.ThrowIfNull(unresolvedDependencies);
        ArgumentNullException.ThrowIfNull(manualOverrides);

        TrackedEntity[] entityArray = entities.ToArray();
        if (entityArray.Any(entity => entity.TrackerId != trackerId))
        {
            throw new InvalidDataException("Tracker state contains an entity owned by another tracker.");
        }

        HashSet<EntityId> entityIds = entityArray.Select(static entity => entity.Id).ToHashSet();
        if (resolvedDependencies.Any(dependency =>
                !entityIds.Contains(dependency.Edge.DependentEntityId)))
        {
            throw new InvalidDataException(
                "A resolved dependency is owned by an entity outside the tracker.");
        }

        if (unresolvedDependencies.Any(dependency =>
                !entityIds.Contains(dependency.Dependency.DependentEntityId)) ||
            manualOverrides.Any(dependencyOverride =>
                !entityIds.Contains(dependencyOverride.DependentEntityId)))
        {
            throw new InvalidDataException(
                "A dependency declaration is owned by an entity outside the tracker.");
        }
    }
}
