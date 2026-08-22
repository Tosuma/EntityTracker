using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

/// <summary>
/// Computes a complete dependency-safe ordering without retaining or mutating graph state.
/// </summary>
public sealed class DependencyRanker
{
    public DependencyRankingResult Rank(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<DependencyEdge> dependencyEdges)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dependencyEdges);

        TrackedEntity[] entityArray = entities.ToArray();
        DependencyEdge[] edgeArray = dependencyEdges.ToArray();

        ValidateElements(entityArray, edgeArray);

        Dictionary<EntityId, GraphNode> nodes = BuildNodes(entityArray);
        ValidateUniqueEdges(edgeArray);

        IReadOnlyList<DependencyRankingDiagnostic> unknownEntityDiagnostics =
            FindUnknownEntityDiagnostics(edgeArray, nodes);
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
        return DependencyRankingResult.Success(
            CreateRankings(nodes, entityOrder, impactScores));
    }

    private static void ValidateElements(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<DependencyEdge> dependencyEdges)
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

    private static IReadOnlyList<DependencyRankingDiagnostic> FindUnknownEntityDiagnostics(
        IEnumerable<DependencyEdge> dependencyEdges,
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

        return invalidEdges
            .OrderBy(static item => item.Edge.DependentEntityId.Value)
            .ThenBy(static item => item.Edge.DependencyEntityId.Value)
            .Select(static item => new DependencyRankingDiagnostic(
                DependencyRankingDiagnosticCode.UnknownEntity,
                $"Dependency edge references unknown entity ID(s): " +
                $"{string.Join(", ", item.UnknownIds.Select(static id => id.Value))}.",
                item.UnknownIds))
            .ToArray();
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

    private static IEnumerable<EntityRanking> CreateRankings(
        IReadOnlyDictionary<EntityId, GraphNode> nodes,
        EntityOrderComparer entityOrder,
        IReadOnlyDictionary<EntityId, int> impactScores)
    {
        Dictionary<EntityId, int> remainingDependencyCounts = nodes.ToDictionary(
            static item => item.Key,
            static item => item.Value.DirectDependencies.Count);
        SortedSet<EntityId> eligibleEntities = new(
            new EligibleEntityComparer(impactScores, entityOrder));

        foreach (KeyValuePair<EntityId, int> item in remainingDependencyCounts)
        {
            if (item.Value == 0)
            {
                eligibleEntities.Add(item.Key);
            }
        }

        List<EntityRanking> rankings = new(nodes.Count);

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

    private enum VisitState
    {
        NotVisited,
        Visiting,
        Visited
    }
}
