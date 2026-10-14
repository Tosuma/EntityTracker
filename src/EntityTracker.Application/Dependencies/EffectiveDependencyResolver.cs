using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

/// <summary>
/// Resolves the dependencies of the active entities against one candidate set of entities. A
/// dependency on an active entity is resolved; one on a placeholder or an archived entity is an
/// unresolved reference to that entity's name.
/// </summary>
public sealed class EffectiveDependencyResolver
{
    public EffectiveDependencyState Resolve(
        IEnumerable<TrackedEntity> allEntities,
        IEnumerable<EntityDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(allEntities);
        ArgumentNullException.ThrowIfNull(dependencies);

        Dictionary<EntityId, TrackedEntity> entitiesById = allEntities.ToDictionary(static entity => entity.Id);
        List<PersistedDependency> resolved = [];
        List<PersistedUnresolvedDependency> unresolved = [];
        foreach (EntityDependency dependency in dependencies.Distinct())
        {
            if (!entitiesById.TryGetValue(dependency.DependentEntityId, out TrackedEntity? owner) ||
                owner.LifecycleState != EntityLifecycleState.Active ||
                !entitiesById.TryGetValue(dependency.DependencyEntityId, out TrackedEntity? target))
                continue;
            if (target.LifecycleState == EntityLifecycleState.Active)
                resolved.Add(new PersistedDependency(new DependencyEdge(owner.Id, target.Id)));
            else
                unresolved.Add(new PersistedUnresolvedDependency(
                    new UnresolvedDependency(owner.Id, target.SourceName)));
        }

        return new EffectiveDependencyState(
            resolved
                .OrderBy(static dependency => dependency.Edge.DependentEntityId.Value)
                .ThenBy(static dependency => dependency.Edge.DependencyEntityId.Value)
                .ToArray(),
            unresolved
                .OrderBy(static dependency => dependency.Dependency.DependentEntityId.Value)
                .ThenBy(
                    static dependency => EntitySourceKey.From(
                        dependency.Dependency.DependencySourceName).Value,
                    StringComparer.Ordinal)
                .ToArray());
    }
}
