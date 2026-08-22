using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

/// <summary>
/// Combines imported dependency facts and durable manual overrides for one active candidate state.
/// </summary>
public sealed class EffectiveDependencyResolver
{
    public EffectiveDependencyState Resolve(
        IEnumerable<TrackedEntity> allEntities,
        IEnumerable<PersistedDependency> importedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> importedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> manualOverrides)
    {
        ArgumentNullException.ThrowIfNull(allEntities);
        ArgumentNullException.ThrowIfNull(importedResolvedDependencies);
        ArgumentNullException.ThrowIfNull(importedUnresolvedDependencies);
        ArgumentNullException.ThrowIfNull(manualOverrides);

        TrackedEntity[] entities = allEntities.ToArray();
        PersistedDependency[] importedResolved = importedResolvedDependencies.ToArray();
        Dictionary<EntityId, TrackedEntity> allById = entities.ToDictionary(static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> activeByKey = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToDictionary(static entity => EntitySourceKey.From(entity.SourceName));
        HashSet<EntityId> activeIds = activeByKey.Values
            .Select(static entity => entity.Id)
            .ToHashSet();

        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> declarations =
            DependencyStateResolver.BuildCurrentDeclarations(
                importedResolved,
                importedUnresolvedDependencies,
                allById);

        foreach (EntityId inactiveOwnerId in declarations.Keys
                     .Where(ownerId => !activeIds.Contains(ownerId))
                     .ToArray())
        {
            declarations.Remove(inactiveOwnerId);
        }

        foreach (ManualDependencyOverride dependencyOverride in manualOverrides
                     .OrderBy(static item => item.DependentEntityId.Value)
                     .ThenBy(
                         static item => EntitySourceKey.From(item.DependencySourceName).Value,
                         StringComparer.Ordinal)
                     .ThenBy(static item => item.Action))
        {
            if (!activeIds.Contains(dependencyOverride.DependentEntityId))
            {
                continue;
            }

            EntitySourceKey targetKey = EntitySourceKey.From(
                dependencyOverride.DependencySourceName);
            if (dependencyOverride.Action == ManualDependencyOverrideAction.Suppress)
            {
                if (declarations.TryGetValue(
                        dependencyOverride.DependentEntityId,
                        out Dictionary<EntitySourceKey, DependencyDeclaration>? ownerDeclarations))
                {
                    ownerDeclarations.Remove(targetKey);
                }

                continue;
            }

            DependencyStateResolver.AddDeclaration(
                declarations,
                dependencyOverride.DependentEntityId,
                new DependencyDeclaration(
                    targetKey,
                    dependencyOverride.DependencySourceName,
                    ImportedDependencyKind.Mandatory,
                    null));
        }

        DependencyStateResolver.Resolve(declarations, activeByKey);
        PersistedDependency[] effectiveResolved =
            DependencyStateResolver.CreateResolvedDependencies(declarations)
                .Concat(importedResolved.Where(dependency =>
                    activeIds.Contains(dependency.Edge.DependentEntityId) &&
                    !allById.ContainsKey(dependency.Edge.DependencyEntityId)))
                .OrderBy(static dependency => dependency.Edge.DependentEntityId.Value)
                .ThenBy(static dependency => dependency.Edge.DependencyEntityId.Value)
                .ToArray();
        PersistedUnresolvedDependency[] effectiveUnresolved =
            DependencyStateResolver.CreateUnresolvedDependencies(declarations)
                .OrderBy(static dependency => dependency.Dependency.DependentEntityId.Value)
                .ThenBy(
                    static dependency => EntitySourceKey.From(
                        dependency.Dependency.DependencySourceName).Value,
                    StringComparer.Ordinal)
                .ToArray();
        return new EffectiveDependencyState(
            effectiveResolved,
            effectiveUnresolved);
    }
}
