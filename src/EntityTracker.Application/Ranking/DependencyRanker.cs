using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

/// <summary>
/// Computes a complete dependency-safe ordering without retaining or mutating graph state.
/// </summary>
public sealed class DependencyRanker : IDependencyRankingService
{
    public DependencyRankingResult Rank(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<DependencyEdge> dependencyEdges)
    {
        return Rank(entities, dependencyEdges, []);
    }

    public DependencyRankingResult Rank(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<DependencyEdge> dependencyEdges,
        IEnumerable<UnresolvedDependency> unresolvedDependencies)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dependencyEdges);
        ArgumentNullException.ThrowIfNull(unresolvedDependencies);

        TrackedEntity[] entityArray = entities.ToArray();
        DependencyEdge[] edgeArray = dependencyEdges.ToArray();
        UnresolvedDependency[] unresolvedDependencyArray = unresolvedDependencies.ToArray();

        ValidateElements(entityArray, edgeArray, unresolvedDependencyArray);

        Dictionary<EntityId, GraphNode> nodes = BuildNodes(entityArray);
        ValidateUniqueEdges(edgeArray);
        ValidateUniqueUnresolvedDependencies(unresolvedDependencyArray);

        IReadOnlyList<DependencyRankingDiagnostic> unknownEntityDiagnostics =
            FindUnknownEntityDiagnostics(edgeArray, unresolvedDependencyArray, nodes);
        if (unknownEntityDiagnostics.Count > 0)
        {
            return DependencyRankingResult.Failure(unknownEntityDiagnostics);
        }

        BuildEdges(edgeArray, nodes);

        EntityOrderComparer entityOrder = new(nodes);
        IReadOnlyList<EntityId>? cyclePath = FindCycle(nodes, entityOrder);
        if (cyclePath is not null)
        {
            string cycleNames = string.Join(
                " -> ",
                cyclePath.Select(entityId => nodes[entityId].Entity.SourceName));

            return DependencyRankingResult.Failure(
            [
                new DependencyRankingDiagnostic(
                    DependencyRankingDiagnosticCode.CycleDetected,
                    $"Dependency cycle detected: {cycleNames}.",
                    cyclePath)
            ]);
        }

        IReadOnlyDictionary<EntityId, int> impactScores = CalculateImpactScores(nodes, entityOrder);
        IReadOnlyList<UnrankedEntity> unrankedEntities = CreateUnrankedEntities(
            nodes,
            unresolvedDependencyArray,
            entityOrder);
        HashSet<EntityId> unrankedEntityIds = unrankedEntities
            .Select(static entity => entity.EntityId)
            .ToHashSet();

        return DependencyRankingResult.Success(
            CreateRankings(nodes, entityOrder, impactScores, unrankedEntityIds),
            unrankedEntities);
    }

    private static void ValidateElements(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<DependencyEdge> dependencyEdges,
        IReadOnlyList<UnresolvedDependency> unresolvedDependencies)
    {
        if (entities.Any(static entity => entity is null))
        {
            throw new ArgumentException("The entity collection cannot contain null.", nameof(entities));
        }

        if (dependencyEdges.Any(static dependencyEdge => dependencyEdge is null))
        {
            throw new ArgumentException(
                "The dependency-edge collection cannot contain null.",
                nameof(dependencyEdges));
        }

        if (unresolvedDependencies.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                "The unresolved-dependency collection cannot contain null.",
                nameof(unresolvedDependencies));
        }
    }

    private static Dictionary<EntityId, GraphNode> BuildNodes(
        IEnumerable<TrackedEntity> entities)
    {
        Dictionary<EntityId, GraphNode> nodes = [];

        foreach (TrackedEntity entity in entities)
        {
            if (!nodes.TryAdd(entity.Id, new GraphNode(entity)))
            {
                throw new ArgumentException(
                    $"The entity collection contains duplicate ID '{entity.Id.Value}'.",
                    nameof(entities));
            }
        }

        return nodes;
    }

    private static void ValidateUniqueEdges(IEnumerable<DependencyEdge> dependencyEdges)
    {
        HashSet<DependencyEdge> uniqueEdges = [];

        foreach (DependencyEdge dependencyEdge in dependencyEdges)
        {
            if (!uniqueEdges.Add(dependencyEdge))
            {
                throw new ArgumentException(
                    "The dependency-edge collection contains a duplicate relationship.",
                    nameof(dependencyEdges));
            }
        }
    }

    private static void ValidateUniqueUnresolvedDependencies(
        IEnumerable<UnresolvedDependency> unresolvedDependencies)
    {
        HashSet<UnresolvedDependencyKey> uniqueDependencies = [];

        foreach (UnresolvedDependency dependency in unresolvedDependencies)
        {
            UnresolvedDependencyKey key = new(
                dependency.DependentEntityId,
                dependency.DependencySourceName.ToUpperInvariant());

            if (!uniqueDependencies.Add(key))
            {
                throw new ArgumentException(
                    "The unresolved-dependency collection contains a duplicate relationship.",
                    nameof(unresolvedDependencies));
            }
        }
    }

    private static IReadOnlyList<DependencyRankingDiagnostic> FindUnknownEntityDiagnostics(
        IEnumerable<DependencyEdge> dependencyEdges,
        IEnumerable<UnresolvedDependency> unresolvedDependencies,
        IReadOnlyDictionary<EntityId, GraphNode> nodes)
    {
        List<(DependencyEdge Edge, EntityId[] UnknownIds)> invalidEdges = [];

        foreach (DependencyEdge dependencyEdge in dependencyEdges)
        {
            List<EntityId> unknownIds = [];

            if (!nodes.ContainsKey(dependencyEdge.DependentEntityId))
            {
                unknownIds.Add(dependencyEdge.DependentEntityId);
            }

            if (!nodes.ContainsKey(dependencyEdge.DependencyEntityId))
            {
                unknownIds.Add(dependencyEdge.DependencyEntityId);
            }

            if (unknownIds.Count > 0)
            {
                invalidEdges.Add((dependencyEdge, unknownIds.ToArray()));
            }
        }

        IEnumerable<DependencyRankingDiagnostic> edgeDiagnostics = invalidEdges
            .OrderBy(static item => item.Edge.DependentEntityId.Value)
            .ThenBy(static item => item.Edge.DependencyEntityId.Value)
            .Select(static item => new DependencyRankingDiagnostic(
                DependencyRankingDiagnosticCode.UnknownEntity,
                $"Dependency edge references unknown entity ID(s): " +
                $"{string.Join(", ", item.UnknownIds.Select(static id => id.Value))}.",
                item.UnknownIds))
            .ToArray();

        IEnumerable<DependencyRankingDiagnostic> unresolvedDiagnostics =
            unresolvedDependencies
                .Where(dependency => !nodes.ContainsKey(dependency.DependentEntityId))
                .OrderBy(static dependency => dependency.DependentEntityId.Value)
                .ThenBy(
                    static dependency => dependency.DependencySourceName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static dependency => new DependencyRankingDiagnostic(
                    DependencyRankingDiagnosticCode.UnknownEntity,
                    $"Unresolved dependency '{dependency.DependencySourceName}' references " +
                    $"unknown dependent entity ID '{dependency.DependentEntityId.Value}'.",
                    [dependency.DependentEntityId]));

        return edgeDiagnostics.Concat(unresolvedDiagnostics).ToArray();
    }

    private static void BuildEdges(
        IEnumerable<DependencyEdge> dependencyEdges,
        IReadOnlyDictionary<EntityId, GraphNode> nodes)
    {
        foreach (DependencyEdge dependencyEdge in dependencyEdges)
        {
            nodes[dependencyEdge.DependentEntityId].DirectDependencies.Add(
                dependencyEdge.DependencyEntityId);
            nodes[dependencyEdge.DependencyEntityId].DirectDependents.Add(
                dependencyEdge.DependentEntityId);
        }
    }

    private static IReadOnlyList<EntityId>? FindCycle(
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        IComparer<EntityId> entityOrder)
    {
        Dictionary<EntityId, VisitState> states = nodes.Keys.ToDictionary(
            static entityId => entityId,
            static _ => VisitState.NotVisited);
        List<EntityId> currentPath = [];
        Dictionary<EntityId, int> pathPositions = [];

        foreach (EntityId entityId in nodes.Keys.OrderBy(static id => id, entityOrder))
        {
            if (states[entityId] == VisitState.NotVisited &&
                TryFindCycle(
                    entityId,
                    nodes,
                    entityOrder,
                    states,
                    currentPath,
                    pathPositions,
                    out IReadOnlyList<EntityId>? cyclePath))
            {
                return cyclePath;
            }
        }

        return null;
    }

    private static bool TryFindCycle(
        EntityId entityId,
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        IComparer<EntityId> entityOrder,
        IDictionary<EntityId, VisitState> states,
        IList<EntityId> currentPath,
        IDictionary<EntityId, int> pathPositions,
        out IReadOnlyList<EntityId>? cyclePath)
    {
        states[entityId] = VisitState.Visiting;
        pathPositions[entityId] = currentPath.Count;
        currentPath.Add(entityId);

        foreach (EntityId dependencyId in nodes[entityId].DirectDependencies
                     .OrderBy(static id => id, entityOrder))
        {
            if (states[dependencyId] == VisitState.Visiting)
            {
                int cycleStart = pathPositions[dependencyId];
                cyclePath = currentPath
                    .Skip(cycleStart)
                    .Append(dependencyId)
                    .ToArray();
                return true;
            }

            if (states[dependencyId] == VisitState.NotVisited &&
                TryFindCycle(
                    dependencyId,
                    nodes,
                    entityOrder,
                    states,
                    currentPath,
                    pathPositions,
                    out cyclePath))
            {
                return true;
            }
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        pathPositions.Remove(entityId);
        states[entityId] = VisitState.Visited;
        cyclePath = null;
        return false;
    }

    private static IReadOnlyDictionary<EntityId, int> CalculateImpactScores(
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        IComparer<EntityId> entityOrder)
    {
        Dictionary<EntityId, HashSet<EntityId>> downstreamEntities = [];

        foreach (EntityId entityId in nodes.Keys.OrderBy(static id => id, entityOrder))
        {
            FindDownstreamEntities(entityId, nodes, entityOrder, downstreamEntities);
        }

        return downstreamEntities.ToDictionary(
            static item => item.Key,
            static item => item.Value.Count);
    }

    private static HashSet<EntityId> FindDownstreamEntities(
        EntityId entityId,
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        IComparer<EntityId> entityOrder,
        IDictionary<EntityId, HashSet<EntityId>> downstreamEntities)
    {
        if (downstreamEntities.TryGetValue(entityId, out HashSet<EntityId>? existing))
        {
            return existing;
        }

        HashSet<EntityId> result = [];

        foreach (EntityId dependentId in nodes[entityId].DirectDependents
                     .OrderBy(static id => id, entityOrder))
        {
            result.Add(dependentId);
            result.UnionWith(
                FindDownstreamEntities(dependentId, nodes, entityOrder, downstreamEntities));
        }

        downstreamEntities.Add(entityId, result);
        return result;
    }

    private static IReadOnlyList<UnrankedEntity> CreateUnrankedEntities(
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        IEnumerable<UnresolvedDependency> unresolvedDependencies,
        IComparer<EntityId> entityOrder)
    {
        Dictionary<EntityId, HashSet<string>> missingNamesByEntity = [];
        HashSet<EntityId> directlyUnresolvedEntityIds = [];

        foreach (UnresolvedDependency dependency in unresolvedDependencies)
        {
            directlyUnresolvedEntityIds.Add(dependency.DependentEntityId);

            if (!missingNamesByEntity.TryGetValue(
                    dependency.DependentEntityId,
                    out HashSet<string>? missingNames))
            {
                missingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                missingNamesByEntity.Add(dependency.DependentEntityId, missingNames);
            }

            missingNames.Add(dependency.DependencySourceName);
        }

        Queue<EntityId> pendingEntityIds = new(
            directlyUnresolvedEntityIds.OrderBy(static id => id, entityOrder));

        while (pendingEntityIds.Count > 0)
        {
            EntityId blockedEntityId = pendingEntityIds.Dequeue();
            HashSet<string> missingNames = missingNamesByEntity[blockedEntityId];

            foreach (EntityId dependentEntityId in nodes[blockedEntityId].DirectDependents
                         .OrderBy(static id => id, entityOrder))
            {
                if (!missingNamesByEntity.TryGetValue(
                        dependentEntityId,
                        out HashSet<string>? dependentMissingNames))
                {
                    dependentMissingNames = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    missingNamesByEntity.Add(dependentEntityId, dependentMissingNames);
                }

                int previousCount = dependentMissingNames.Count;
                dependentMissingNames.UnionWith(missingNames);
                if (dependentMissingNames.Count > previousCount)
                {
                    pendingEntityIds.Enqueue(dependentEntityId);
                }
            }
        }

        return missingNamesByEntity.Keys
            .OrderBy(static id => id, entityOrder)
            .Select(entityId => new UnrankedEntity(
                entityId,
                directlyUnresolvedEntityIds.Contains(entityId)
                    ? DependencyResolutionState.Unresolved
                    : DependencyResolutionState.Blocked,
                missingNamesByEntity[entityId]
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static name => name, StringComparer.Ordinal)))
            .ToArray();
    }

    private static IEnumerable<EntityRanking> CreateRankings(
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        EntityOrderComparer entityOrder,
        IReadOnlyDictionary<EntityId, int> impactScores,
        IReadOnlySet<EntityId> unrankedEntityIds)
    {
        Dictionary<EntityId, int> remainingDependencyCounts = nodes
            .Where(item => !unrankedEntityIds.Contains(item.Key))
            .ToDictionary(
                static item => item.Key,
                item => item.Value.DirectDependencies.Count(
                    dependencyId => !unrankedEntityIds.Contains(dependencyId)));
        SortedSet<EntityId> eligibleEntities = new(
            new EligibleEntityComparer(impactScores, entityOrder));

        foreach (KeyValuePair<EntityId, int> item in remainingDependencyCounts)
        {
            if (item.Value == 0)
            {
                eligibleEntities.Add(item.Key);
            }
        }

        List<EntityRanking> rankings = new(remainingDependencyCounts.Count);

        while (eligibleEntities.Count > 0)
        {
            EntityId entityId = eligibleEntities.Min!;
            eligibleEntities.Remove(entityId);

            GraphNode node = nodes[entityId];
            rankings.Add(new EntityRanking(
                entityId,
                rankings.Count + 1,
                impactScores[entityId],
                node.DirectDependencies.OrderBy(static id => id, entityOrder),
                node.DirectDependents.OrderBy(static id => id, entityOrder)));

            foreach (EntityId dependentId in node.DirectDependents
                         .Where(dependentId => !unrankedEntityIds.Contains(dependentId))
                         .OrderBy(static id => id, entityOrder))
            {
                remainingDependencyCounts[dependentId]--;
                if (remainingDependencyCounts[dependentId] == 0)
                {
                    eligibleEntities.Add(dependentId);
                }
            }
        }

        return rankings;
    }

    private sealed class GraphNode(TrackedEntity entity)
    {
        public TrackedEntity Entity { get; } = entity;

        public List<EntityId> DirectDependencies { get; } = [];

        public List<EntityId> DirectDependents { get; } = [];
    }

    private sealed class EntityOrderComparer(
        IReadOnlyDictionary<EntityId, GraphNode> nodes) : IComparer<EntityId>
    {
        public int Compare(EntityId? x, EntityId? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(
                nodes[x].Entity.SourceName,
                nodes[y].Entity.SourceName);

            return nameComparison != 0
                ? nameComparison
                : x.Value.CompareTo(y.Value);
        }
    }

    private sealed class EligibleEntityComparer(
        IReadOnlyDictionary<EntityId, int> impactScores,
        IComparer<EntityId> entityOrder) : IComparer<EntityId>
    {
        public int Compare(EntityId? x, EntityId? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int impactComparison = impactScores[y].CompareTo(impactScores[x]);
            return impactComparison != 0
                ? impactComparison
                : entityOrder.Compare(x, y);
        }
    }

    private sealed record UnresolvedDependencyKey(
        EntityId DependentEntityId,
        string DependencySourceKey);

    private enum VisitState
    {
        NotVisited,
        Visiting,
        Visited
    }
}
