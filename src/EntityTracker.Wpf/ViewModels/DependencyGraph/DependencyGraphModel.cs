using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>An entity, or a missing dependency placeholder, positioned on the dependency map.</summary>
public sealed class DependencyGraphNode
{
    internal DependencyGraphNode(string key, string label, EntityId? entityId, DevelopmentStatus? status,
        int? rank = null)
    {
        Key = key;
        Label = label;
        EntityId = entityId;
        Status = status;
        Rank = rank;
    }

    public string Key { get; }
    public string Label { get; }
    public EntityId? EntityId { get; }
    public DevelopmentStatus? Status { get; }
    public bool IsPlaceholder => EntityId is null;

    /// <summary>Gets the dependency-safe rank shown in Overview, or null when the entity is unranked.</summary>
    public int? Rank { get; }
    public int DependencyCount { get; internal set; }
    public int DependentCount { get; internal set; }
    public bool IsConnected => DependencyCount + DependentCount > 0;

    /// <summary>Level of entities without any links; they orbit on their own belt.</summary>
    public const int UnconnectedLevel = -1;

    /// <summary>Level of missing dependencies; they orbit outside everything else.</summary>
    public const int MissingLevel = -2;

    /// <summary>
    /// Gets the orbit: 0 for foundation entities without (resolved) dependencies, increasing by one
    /// for each layer of dependencies beneath an entity; <see cref="UnconnectedLevel"/> for
    /// entities without any links and <see cref="MissingLevel"/> for missing dependencies.
    /// </summary>
    public int Level { get; internal set; }

    /// <summary>Gets the impact: how many entities depend on this one directly or transitively.</summary>
    public int TransitiveDependentCount { get; internal set; }

    /// <summary>Gets the drawing radius; entities that more entities refer to directly are drawn larger.</summary>
    public double Radius => 6 + 2.2 * Math.Sqrt(DependentCount);

    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public bool IsPinned { get; set; }
    internal bool HasPosition { get; set; }
}

/// <summary>A link pointing from a dependency to the entity that depends on it.</summary>
public sealed record DependencyGraphEdge(DependencyGraphNode From, DependencyGraphNode To)
{
    /// <summary>Gets whether the link is drawn; links implied by a longer chain are not.</summary>
    public bool IsEssential { get; internal set; } = true;
}

public sealed class DependencyGraphModel
{
    private readonly Dictionary<string, DependencyGraphNode> _nodesByKey;

    internal DependencyGraphModel(
        IReadOnlyList<DependencyGraphNode> nodes,
        IReadOnlyList<DependencyGraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
        EssentialEdges = edges.Where(static edge => edge.IsEssential).ToArray();
        MaxLevel = nodes.Count == 0 ? -1 : nodes.Max(static node => node.Level);
        _nodesByKey = nodes.ToDictionary(static node => node.Key, StringComparer.Ordinal);
    }

    public static DependencyGraphModel Empty { get; } = new([], []);

    public IReadOnlyList<DependencyGraphNode> Nodes { get; }
    public IReadOnlyList<DependencyGraphEdge> Edges { get; }
    public IReadOnlyList<DependencyGraphEdge> EssentialEdges { get; }
    public int MaxLevel { get; }

    public DependencyGraphNode? Find(string key) =>
        _nodesByKey.GetValueOrDefault(key);

    public DependencyGraphNode? Find(EntityId entityId) => Find(EntityKey(entityId));

    internal static string EntityKey(EntityId entityId) => $"entity:{entityId.Value:N}";

    internal static string PlaceholderKey(string name) => $"missing:{name}";
}

public static class DependencyGraphBuilder
{
    /// <summary>
    /// Builds the map from the active overview rows. Dependency names are matched to entity
    /// source names with the same ordinal comparison the dependency resolver uses; names
    /// without an active entity become one shared placeholder each.
    /// </summary>
    public static DependencyGraphModel Build(
        IReadOnlyList<EntityOverviewRow> rows,
        DependencyGraphModel? previous = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<DependencyGraphNode> nodes = [];
        Dictionary<string, DependencyGraphNode> byName = new(StringComparer.Ordinal);
        foreach (EntityOverviewRow row in rows)
        {
            if (byName.ContainsKey(row.SourceName)) continue;
            DependencyGraphNode node = new(DependencyGraphModel.EntityKey(row.EntityId),
                row.SourceName, row.EntityId, row.DevelopmentStatus,
                int.TryParse(row.Rank, out int rank) ? rank : null);
            byName.Add(row.SourceName, node);
            nodes.Add(node);
        }

        Dictionary<string, DependencyGraphNode> placeholders = new(StringComparer.Ordinal);
        HashSet<(DependencyGraphNode, DependencyGraphNode)> seen = [];
        List<DependencyGraphEdge> edges = [];
        foreach (EntityOverviewRow row in rows)
        {
            DependencyGraphNode dependent = byName[row.SourceName];
            if (dependent.EntityId != row.EntityId) continue;
            foreach (string name in row.DependencyNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!byName.TryGetValue(name, out DependencyGraphNode? dependency) &&
                    !placeholders.TryGetValue(name, out dependency))
                {
                    dependency = new DependencyGraphNode(
                        DependencyGraphModel.PlaceholderKey(name), name, null, null);
                    placeholders.Add(name, dependency);
                    nodes.Add(dependency);
                }

                if (ReferenceEquals(dependency, dependent) || !seen.Add((dependency, dependent)))
                    continue;
                edges.Add(new DependencyGraphEdge(dependency, dependent));
                dependency.DependentCount++;
                dependent.DependencyCount++;
            }
        }

        if (previous is not null)
        {
            foreach (DependencyGraphNode node in nodes)
            {
                if (previous.Find(node.Key) is not { HasPosition: true } old) continue;
                node.X = old.X;
                node.Y = old.Y;
                node.HasPosition = true;
            }
        }

        DependencyGraphAnalysis.Analyze(nodes, edges);
        return new DependencyGraphModel(nodes, edges);
    }
}
