namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>
/// Deterministic radial ("solar system") layout. Entities nothing depends on sit in the centre,
/// with the most dependent hub at the origin; every dependency orbits one level further out than
/// the entity using it. Each entity owns a slice of the circle sized by how many entities fan out
/// behind it, so dependency chains run outward side by side instead of crossing the whole map.
/// Unconnected entities form an outer belt. A light simulation settles nodes towards these
/// anchors and only nudges overlapping neighbours apart, so entities stay on their orbit.
/// </summary>
public sealed class RadialDependencyLayout
{
    internal const double RingGap = 70;
    internal const double NodeSpacing = 30;
    internal const double BandDepth = 22;
    internal const double AnchorStrength = 0.12;
    internal const double CollisionDistance = 20;
    internal const double CollisionStrength = 0.4;
    internal const double Damping = 0.7;
    internal const double MaxSpeed = 30;
    internal const double SettledEnergy = 0.01;

    private readonly DependencyGraphModel _model;
    private readonly Dictionary<DependencyGraphNode, (double X, double Y)> _anchors = new(ReferenceEqualityComparer.Instance);

    public RadialDependencyLayout(DependencyGraphModel model, int seed = 42)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        RingRadii = ComputeAnchors();
        Random random = new(seed);
        foreach (DependencyGraphNode node in model.Nodes)
        {
            if (node.HasPosition) continue;
            (double x, double y) = _anchors[node];
            node.X = x + random.NextDouble() - 0.5;
            node.Y = y + random.NextDouble() - 0.5;
            node.HasPosition = true;
        }
    }

    public double Energy { get; private set; } = double.MaxValue;
    public bool IsSettled => Energy <= SettledEnergy;

    /// <summary>Gets the orbit radius for each level from 1 outward, followed by the orphan belt when present.</summary>
    public IReadOnlyList<double> RingRadii { get; private set; }

    public (double X, double Y) Anchor(DependencyGraphNode node) => _anchors.GetValueOrDefault(node);

    /// <summary>Keeps a dragged entity where it was dropped.</summary>
    public void MoveAnchor(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _anchors[node] = (node.X, node.Y);
    }

    /// <summary>Runs synchronous steps until settled.</summary>
    public int Settle(int maxIterations = 400)
    {
        for (int index = 0; index < maxIterations; index++)
        {
            if (Step() <= SettledEnergy) return index + 1;
        }

        return maxIterations;
    }

    /// <summary>Advances the simulation one step and returns the mean kinetic energy.</summary>
    public double Step()
    {
        IReadOnlyList<DependencyGraphNode> nodes = _model.Nodes;
        int count = nodes.Count;
        if (count == 0) return Energy = 0;
        double[] forceX = new double[count];
        double[] forceY = new double[count];
        for (int i = 0; i < count; i++)
        {
            DependencyGraphNode a = nodes[i];
            (double anchorX, double anchorY) = _anchors[a];
            forceX[i] += AnchorStrength * (anchorX - a.X);
            forceY[i] += AnchorStrength * (anchorY - a.Y);
            for (int j = i + 1; j < count; j++)
            {
                DependencyGraphNode b = nodes[j];
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                if (Math.Abs(dx) > CollisionDistance || Math.Abs(dy) > CollisionDistance) continue;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > CollisionDistance * CollisionDistance) continue;
                if (distanceSquared < 0.01)
                {
                    // Separate coincident nodes in a stable direction.
                    dx = Math.Cos(i + j);
                    dy = Math.Sin(i + j);
                    distanceSquared = 1;
                }

                double distance = Math.Sqrt(distanceSquared);
                double force = CollisionStrength * (CollisionDistance - distance) / 2;
                double fx = force * dx / distance;
                double fy = force * dy / distance;
                forceX[i] += fx;
                forceY[i] += fy;
                forceX[j] -= fx;
                forceY[j] -= fy;
            }
        }

        double energy = 0;
        for (int i = 0; i < count; i++)
        {
            DependencyGraphNode node = nodes[i];
            if (node.IsPinned)
            {
                node.VelocityX = node.VelocityY = 0;
                continue;
            }

            double vx = (node.VelocityX + forceX[i]) * Damping;
            double vy = (node.VelocityY + forceY[i]) * Damping;
            double speed = Math.Sqrt(vx * vx + vy * vy);
            if (speed > MaxSpeed)
            {
                vx *= MaxSpeed / speed;
                vy *= MaxSpeed / speed;
            }

            node.VelocityX = vx;
            node.VelocityY = vy;
            node.X += vx;
            node.Y += vy;
            energy += vx * vx + vy * vy;
        }

        return Energy = energy / count;
    }

    /// <summary>Wakes the simulation, for example after a node was dragged.</summary>
    public void Reheat() => Energy = double.MaxValue;

    private List<double> ComputeAnchors()
    {
        IReadOnlyList<DependencyGraphNode> nodes = _model.Nodes;
        DependencyGraphNode[] connected = nodes.Where(static node => node.Level >= 0).ToArray();
        DependencyGraphNode[] orphans = nodes.Where(static node => node.Level < 0)
            .OrderBy(static node => node.Label, StringComparer.Ordinal).ToArray();

        // Each entity hangs below its heaviest dependent on the next orbit inward.
        ILookup<DependencyGraphNode, DependencyGraphNode> dependents = _model.EssentialEdges
            .ToLookup(static edge => edge.From, static edge => edge.To);
        Dictionary<DependencyGraphNode, List<DependencyGraphNode>> children = new(ReferenceEqualityComparer.Instance);
        List<DependencyGraphNode> roots = [];
        foreach (DependencyGraphNode node in connected)
        {
            DependencyGraphNode? parent = dependents[node]
                .Where(item => item.Level == node.Level - 1)
                .OrderByDescending(static item => item.TransitiveDependencyCount)
                .ThenBy(static item => item.Label, StringComparer.Ordinal)
                .FirstOrDefault();
            if (parent is null)
            {
                roots.Add(node);
                continue;
            }

            if (!children.TryGetValue(parent, out List<DependencyGraphNode>? list))
                children[parent] = list = [];
            list.Add(node);
        }

        Dictionary<DependencyGraphNode, double> width = new(ReferenceEqualityComparer.Instance);
        foreach (DependencyGraphNode node in connected.OrderByDescending(static node => node.Level))
        {
            width[node] = children.TryGetValue(node, out List<DependencyGraphNode>? list)
                ? Math.Max(1, list.Sum(child => width[child]))
                : 1;
        }

        roots = roots.OrderByDescending(static node => node.TransitiveDependencyCount)
            .ThenBy(static node => node.Label, StringComparer.Ordinal).ToList();
        foreach (List<DependencyGraphNode> list in children.Values)
            list.Sort(static (a, b) => string.CompareOrdinal(a.Label, b.Label));
        Dictionary<DependencyGraphNode, (double Start, double Sweep)> sectors = new(ReferenceEqualityComparer.Instance);
        AssignSectors(roots, children, width, sectors);

        // Reduce crossings: order every slice by where each entity's other links point,
        // alternating outward (towards dependents) and inward (towards dependencies).
        ILookup<DependencyGraphNode, DependencyGraphNode> dependencies = _model.EssentialEdges
            .ToLookup(static edge => edge.To, static edge => edge.From);
        for (int pass = 0; pass < 6; pass++)
        {
            ILookup<DependencyGraphNode, DependencyGraphNode> towards = pass % 2 == 0 ? dependents : dependencies;
            OrderByNeighbours(roots, -Math.PI / 2, towards, sectors);
            foreach ((DependencyGraphNode parent, List<DependencyGraphNode> list) in children)
                OrderByNeighbours(list, sectors[parent].Start, towards, sectors);
            AssignSectors(roots, children, width, sectors);
        }

        // Size each orbit so its narrowest slice still fits a node.
        int maxLevel = _model.MaxLevel;
        double[] radius = new double[Math.Max(maxLevel + 1, 1)];
        int heaviestRoot = roots.Count == 0 ? 0 : roots.Max(static node => node.TransitiveDependencyCount);
        radius[0] = Math.Max(RingGap, roots.Count * NodeSpacing / (2 * Math.PI) + BandDepth);
        for (int level = 1; level <= maxLevel; level++)
        {
            double narrowest = connected.Where(node => node.Level == level)
                .Select(node => sectors[node].Sweep).DefaultIfEmpty(2 * Math.PI).Min();
            radius[level] = Math.Max(radius[level - 1] + RingGap, NodeSpacing / Math.Max(narrowest, 1e-3));
        }

        List<double> rings = [];
        for (int level = 0; level <= maxLevel; level++) rings.Add(radius[level] - BandDepth / 2);
        // A clearly dominant hub sits in the exact centre; other top-level entities share the inner orbit.
        int runnerUp = roots.Count < 2 ? 0 : roots[1].TransitiveDependencyCount;
        DependencyGraphNode? centre = roots.Count == 1 || (roots.Count > 1 && heaviestRoot >= runnerUp * 1.5)
            ? roots[0]
            : null;
        foreach (DependencyGraphNode node in connected)
        {
            (double start, double sweep) = sectors[node];
            double distance = ReferenceEquals(node, centre)
                ? 0
                : radius[node.Level] - BandDepth * Math.Min(1, node.TransitiveDependencyCount / 10.0);
            double theta = start + sweep / 2;
            _anchors[node] = (distance * Math.Cos(theta), distance * Math.Sin(theta));
        }

        if (orphans.Length > 0)
        {
            double outer = maxLevel >= 0 ? radius[Math.Max(maxLevel, 0)] : 0;
            double belt = Math.Max(outer + RingGap, orphans.Length * NodeSpacing / (2 * Math.PI));
            rings.Add(belt);
            for (int index = 0; index < orphans.Length; index++)
            {
                double theta = -Math.PI / 2 + 2 * Math.PI * index / orphans.Length;
                _anchors[orphans[index]] = (belt * Math.Cos(theta), belt * Math.Sin(theta));
            }
        }

        return rings;
    }

    private static void AssignSectors(
        List<DependencyGraphNode> roots,
        Dictionary<DependencyGraphNode, List<DependencyGraphNode>> children,
        Dictionary<DependencyGraphNode, double> width,
        Dictionary<DependencyGraphNode, (double Start, double Sweep)> sectors)
    {
        double total = roots.Sum(node => width[node]);
        double angle = -Math.PI / 2;
        Queue<DependencyGraphNode> pending = new();
        foreach (DependencyGraphNode root in roots)
        {
            double sweep = 2 * Math.PI * width[root] / total;
            sectors[root] = (angle, sweep);
            angle += sweep;
            pending.Enqueue(root);
        }

        while (pending.TryDequeue(out DependencyGraphNode? node))
        {
            if (!children.TryGetValue(node, out List<DependencyGraphNode>? list)) continue;
            (double cursor, double sweep) = sectors[node];
            foreach (DependencyGraphNode child in list)
            {
                double share = sweep * width[child] / width[node];
                sectors[child] = (cursor, share);
                cursor += share;
                pending.Enqueue(child);
            }
        }
    }

    /// <summary>Sorts siblings by the circular mean angle of their neighbours, measured from the slice start.</summary>
    private static void OrderByNeighbours(
        List<DependencyGraphNode> siblings,
        double sliceStart,
        ILookup<DependencyGraphNode, DependencyGraphNode> neighbours,
        Dictionary<DependencyGraphNode, (double Start, double Sweep)> sectors)
    {
        if (siblings.Count < 2) return;
        double Key(DependencyGraphNode node)
        {
            (double start, double sweep) = sectors[node];
            double x = 0, y = 0;
            foreach (DependencyGraphNode neighbour in neighbours[node])
            {
                if (!sectors.TryGetValue(neighbour, out (double Start, double Sweep) sector)) continue;
                double theta = sector.Start + sector.Sweep / 2;
                x += Math.Cos(theta);
                y += Math.Sin(theta);
            }

            double angle = Math.Abs(x) + Math.Abs(y) < 1e-9 ? start + sweep / 2 : Math.Atan2(y, x);
            double relative = (angle - sliceStart) % (2 * Math.PI);
            return relative < 0 ? relative + 2 * Math.PI : relative;
        }

        Dictionary<DependencyGraphNode, double> keys = siblings.ToDictionary(node => node, Key);
        siblings.Sort((a, b) =>
        {
            int order = keys[a].CompareTo(keys[b]);
            return order != 0 ? order : string.CompareOrdinal(a.Label, b.Label);
        });
    }
}
