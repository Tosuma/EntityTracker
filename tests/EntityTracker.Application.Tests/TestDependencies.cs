using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

internal static class TestDependencies
{
    /// <summary>
    /// Turns dependency edges between test entities and unresolved names into the dependencies the app
    /// stores: every one by ID, an unresolved name on its placeholder (see <see cref="WithPlaceholders"/>).
    /// </summary>
    public static EntityDependency[] Named(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency>? resolved = null,
        IEnumerable<PersistedUnresolvedDependency>? unresolved = null)
    {
        Dictionary<EntityId, TrackerId> trackers = entities.ToDictionary(e => e.Id, e => e.TrackerId);
        return (resolved ?? [])
            .Select(d => new EntityDependency(d.Edge.DependentEntityId, d.Edge.DependencyEntityId))
            .Concat((unresolved ?? []).Select(d => new EntityDependency(d.Dependency.DependentEntityId,
                TrackedEntity.PlaceholderId(trackers[d.Dependency.DependentEntityId],
                    d.Dependency.DependencySourceName))))
            .ToArray();
    }

    /// <summary>The entities with a placeholder for each unresolved name.</summary>
    public static TrackedEntity[] WithPlaceholders(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedUnresolvedDependency>? unresolved = null)
    {
        TrackedEntity[] all = entities.ToArray();
        Dictionary<EntityId, TrackerId> trackers = all.ToDictionary(e => e.Id, e => e.TrackerId);
        return
        [
            .. all,
            .. (unresolved ?? [])
                .Select(d => TrackedEntity.Placeholder(trackers[d.Dependency.DependentEntityId],
                    d.Dependency.DependencySourceName))
                .DistinctBy(static placeholder => placeholder.Id)
        ];
    }

    public static EffectiveDependencyState Resolve(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency>? resolved = null,
        IEnumerable<PersistedUnresolvedDependency>? unresolved = null)
    {
        TrackedEntity[] all = entities.ToArray();
        return new EffectiveDependencyResolver().Resolve(
            WithPlaceholders(all, unresolved), Named(all, resolved, unresolved));
    }
}
