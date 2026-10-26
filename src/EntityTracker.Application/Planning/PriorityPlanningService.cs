using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;

namespace EntityTracker.Application.Planning;

/// <summary>
/// Derives planning urgency from requested priorities and the active effective dependency graph.
/// </summary>
public sealed class PriorityPlanningService
{
    public IReadOnlyDictionary<EntityId, int?> CalculateEffectivePriorities(
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveState) =>
        CalculateEffective(entities, effectiveState, static entity => entity.RequestedPriority);

    /// <summary>
    /// Each active entity's effective due date: the earliest due date of the entity and of every entity
    /// that depends on it, directly or not. A dependency must be done before what needs it, so it inherits
    /// the deadline.
    /// </summary>
    public IReadOnlyDictionary<EntityId, DateOnly?> CalculateEffectiveDueDates(
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveState) =>
        CalculateEffective(entities, effectiveState, static entity => entity.DueDate);

    // The requested value flows from each entity to everything it depends on, and a dependency keeps the
    // most urgent (smallest) value of anything that needs it.
    private static IReadOnlyDictionary<EntityId, T?> CalculateEffective<T>(
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveState,
        Func<TrackedEntity, T?> requested)
        where T : struct, IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(effectiveState);

        TrackedEntity[] activeEntities = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        Dictionary<EntityId, TrackedEntity> entitiesById = activeEntities.ToDictionary(
            static entity => entity.Id);
        IReadOnlyDictionary<EntityId, EntityId[]> dependenciesByOwner =
            BuildDependenciesByOwner(effectiveState, entitiesById);
        Dictionary<EntityId, T?> effectiveValues = activeEntities.ToDictionary(
            static entity => entity.Id,
            requested);
        Queue<EntityId> pending = new(activeEntities
            .Where(entity => requested(entity) is not null)
            .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .ThenBy(static entity => entity.Id.Value)
            .Select(static entity => entity.Id));

        while (pending.Count > 0)
        {
            EntityId ownerId = pending.Dequeue();
            T? ownerValue = effectiveValues[ownerId];
            if (ownerValue is null || !dependenciesByOwner.TryGetValue(
                    ownerId,
                    out EntityId[]? dependencyIds))
            {
                continue;
            }

            foreach (EntityId dependencyId in dependencyIds)
            {
                T? dependencyValue = effectiveValues[dependencyId];
                if (dependencyValue is { } current && current.CompareTo(ownerValue.Value) <= 0)
                {
                    continue;
                }

                effectiveValues[dependencyId] = ownerValue;
                pending.Enqueue(dependencyId);
            }
        }

        return effectiveValues;
    }

    public PriorityPlanningPreview CreatePreview(
        EntityId targetEntityId,
        int? candidateRequestedPriority,
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveState)
    {
        ArgumentNullException.ThrowIfNull(targetEntityId);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(effectiveState);
        EnsureValidRequestedPriority(candidateRequestedPriority);

        TrackedEntity[] entityArray = entities.ToArray();
        Dictionary<EntityId, TrackedEntity> activeById = entityArray
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToDictionary(static entity => entity.Id);
        if (!activeById.TryGetValue(targetEntityId, out TrackedEntity? target))
        {
            throw new InvalidOperationException(
                "Priority can only be previewed for an active tracked entity.");
        }

        TrackedEntity candidateTarget = new(
            target.Id,
            target.TrackerId,
            target.SourceName,
            target.Status,
            target.Notes,
            target.LifecycleState,
            candidateRequestedPriority,
            target.ResponsibleDeveloper,
            target.GroupName,
            target.FilterActive,
            target.SharedNotes,
            target.DueDate);
        TrackedEntity[] candidateEntities = entityArray
            .Select(entity => entity.Id == targetEntityId ? candidateTarget : entity)
            .ToArray();
        IReadOnlyDictionary<EntityId, int?> effectivePriorities =
            CalculateEffectivePriorities(candidateEntities, effectiveState);
        IReadOnlyDictionary<EntityId, EntityId[]> dependenciesByOwner =
            BuildDependenciesByOwner(effectiveState, activeById);
        IReadOnlyDictionary<EntityId, string[]> unresolvedByOwner = effectiveState
            .UnresolvedDependencies
            .Where(item => activeById.ContainsKey(item.Dependency.DependentEntityId))
            .GroupBy(static item => item.Dependency.DependentEntityId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static item => item.Dependency.DependencySourceName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static name => name, StringComparer.Ordinal)
                    .ToArray());
        List<EntityId> dependencySafeIds = [];
        HashSet<EntityId> visited = [];
        HashSet<EntityId> visiting = [];
        HashSet<string> unresolvedNames = new(StringComparer.OrdinalIgnoreCase);

        Visit(targetEntityId);

        return new PriorityPlanningPreview(
            dependencySafeIds.Select(entityId =>
            {
                TrackedEntity entity = activeById[entityId];
                return new PriorityPlanningItem(
                    entity.Id,
                    entity.SourceName,
                    entity.Id == targetEntityId,
                    entity.Id == targetEntityId
                        ? candidateRequestedPriority
                        : entity.RequestedPriority,
                    effectivePriorities[entity.Id]);
            }),
            unresolvedNames
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static name => name, StringComparer.Ordinal));

        void Visit(EntityId entityId)
        {
            if (visited.Contains(entityId))
            {
                return;
            }

            if (!visiting.Add(entityId))
            {
                throw new InvalidOperationException(
                    "Priority preview requires an acyclic dependency graph.");
            }

            if (unresolvedByOwner.TryGetValue(entityId, out string[]? missingNames))
            {
                unresolvedNames.UnionWith(missingNames);
            }

            if (dependenciesByOwner.TryGetValue(entityId, out EntityId[]? dependencyIds))
            {
                foreach (EntityId dependencyId in dependencyIds)
                {
                    Visit(dependencyId);
                }
            }

            visiting.Remove(entityId);
            visited.Add(entityId);
            dependencySafeIds.Add(entityId);
        }
    }

    private static IReadOnlyDictionary<EntityId, EntityId[]> BuildDependenciesByOwner(
        EffectiveDependencyState effectiveState,
        IReadOnlyDictionary<EntityId, TrackedEntity> activeById)
    {
        foreach (DependencyEdge edge in effectiveState.ResolvedDependencies.Select(
                     static item => item.Edge))
        {
            if (!activeById.ContainsKey(edge.DependentEntityId) ||
                !activeById.ContainsKey(edge.DependencyEntityId))
            {
                throw new ArgumentException(
                    "The effective dependency graph references an entity outside the active state.",
                    nameof(effectiveState));
            }
        }

        return effectiveState.ResolvedDependencies
            .Select(static item => item.Edge)
            .Distinct()
            .GroupBy(static edge => edge.DependentEntityId)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(static edge => edge.DependencyEntityId)
                    .OrderBy(id => activeById[id].SourceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(id => activeById[id].SourceName, StringComparer.Ordinal)
                    .ThenBy(static id => id.Value)
                    .ToArray());
    }

    private static void EnsureValidRequestedPriority(int? requestedPriority)
    {
        if (requestedPriority is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedPriority),
                requestedPriority,
                "Requested priority must be between 1 and 5.");
        }
    }
}
