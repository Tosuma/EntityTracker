using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

/// <summary>
/// Derives direct implementation blockers from the effective dependency graph.
/// </summary>
public sealed class WorkflowReadinessEvaluator
{
    public IReadOnlyDictionary<EntityId, EntityReadiness> Evaluate(
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveDependencies)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(effectiveDependencies);

        TrackedEntity[] activeEntities = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        IReadOnlyDictionary<EntityId, TrackedEntity> entitiesById = activeEntities
            .ToDictionary(static entity => entity.Id);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyBlocker>> blockers =
            activeEntities.ToDictionary(
                static entity => entity.Id,
                static _ => new Dictionary<EntitySourceKey, DependencyBlocker>());

        foreach (PersistedDependency dependency in effectiveDependencies.ResolvedDependencies)
        {
            if (!blockers.TryGetValue(
                    dependency.Edge.DependentEntityId,
                    out Dictionary<EntitySourceKey, DependencyBlocker>? ownerBlockers) ||
                !entitiesById.TryGetValue(
                    dependency.Edge.DependencyEntityId,
                    out TrackedEntity? target) ||
                IsDependencyImplemented(target.Status))
            {
                continue;
            }

            ownerBlockers.TryAdd(
                EntitySourceKey.From(target.SourceName),
                new DependencyBlocker(
                    target.SourceName,
                    DependencyBlockerKind.Incomplete,
                    dependency.Kind,
                    target.Id,
                    target.Status));
        }

        foreach (PersistedUnresolvedDependency dependency in
                 effectiveDependencies.UnresolvedDependencies)
        {
            if (!blockers.TryGetValue(
                    dependency.Dependency.DependentEntityId,
                    out Dictionary<EntitySourceKey, DependencyBlocker>? ownerBlockers))
            {
                continue;
            }

            ownerBlockers.TryAdd(
                EntitySourceKey.From(dependency.Dependency.DependencySourceName),
                new DependencyBlocker(
                    dependency.Dependency.DependencySourceName,
                    DependencyBlockerKind.Unresolved,
                    dependency.Kind));
        }

        return blockers.ToDictionary(
            static item => item.Key,
            static item => new EntityReadiness(
                item.Key,
                item.Value.Values
                    .OrderBy(static blocker => blocker.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static blocker => blocker.SourceName, StringComparer.Ordinal)));
    }

    public EntityWorkflowState Classify(
        TrackedEntity entity,
        EntityReadiness? readiness = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.LifecycleState == EntityLifecycleState.Archived)
        {
            return EntityWorkflowState.Archived;
        }

        return entity.Status switch
        {
            DevelopmentStatus.NotStarted when readiness?.IsReady == true =>
                EntityWorkflowState.Ready,
            DevelopmentStatus.NotStarted => EntityWorkflowState.Blocked,
            DevelopmentStatus.InProgress => EntityWorkflowState.InProgress,
            DevelopmentStatus.ReworkNeeded => EntityWorkflowState.ReworkNeeded,
            DevelopmentStatus.DevelopmentCompleted =>
                EntityWorkflowState.DevelopmentCompleted,
            DevelopmentStatus.Reconciled => EntityWorkflowState.Reconciled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entity),
                entity.Status,
                "The entity development status is not defined.")
        };
    }

    private static bool IsDependencyImplemented(DevelopmentStatus status) =>
        status is DevelopmentStatus.ReworkNeeded or
            DevelopmentStatus.DevelopmentCompleted or
            DevelopmentStatus.Reconciled;
}
