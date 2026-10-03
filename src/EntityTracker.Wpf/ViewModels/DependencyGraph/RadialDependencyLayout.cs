namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>
/// Deterministic radial ("solar system") layout. Foundation entities without dependencies sit in
/// the centre, with a clearly dominant one (by impact) at the origin; every entity orbits one level
/// further out than its deepest dependency, so the map reads outward in build order. Within an
/// orbit, entities earlier in the dependency-safe rank sit on the inner edge. Each entity owns a
/// slice of the circle sized by how many entities fan out behind it, so chains run outward side by
/// side instead of crossing the whole map; angles are then refined so each entity sits straight out
/// from the entities it links to. A lone foundation is the centre itself, unconnected entities form
/// an outer belt and missing dependencies orbit outside everything.
/// A light simulation settles nodes towards these
/// anchors and only nudges overlapping neighbours apart, so entities stay on their orbit.
/// </summary>
public sealed class RadialDependencyLayout
{
    internal const double RingGap = 70;
    internal const double NodeSpacing = 40;
    internal const double BandDepth = 22;
    internal const double AnchorStrength = 0.12;
    internal const double CollisionDistance = 20;
    internal const double CollisionStrength = 0.4;
    internal const double Damping = 0.7;
    internal const double MaxSpeed = 30;
    internal const double SettledEnergy = 0.01;

    private readonly DependencyGraphModel _model;
    private readonly Dictionary<DependencyGraphNode, (double X, double Y)> _anchors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DependencyGraphNode, double> _angles = new(ReferenceEqualityComparer.Instance);
    private DependencyGraphNode? _centre;

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

    /// <summary>
    /// Gets the orbit radii from the inside out: one per level (none for a lone foundation, which is
    /// the centre itself), then the unconnected belt and the missing-dependency ring when present.
    /// </summary>
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

    /// <summary>Gets the average angle between the two ends of each drawn link, in radians.</summary>
    internal double MeanLinkAngle() => MeanLinkAngle(_angles);

    /// <summary>Gets <see cref="MeanLinkAngle()"/> for the slice-midpoint angles before refinement.</summary>
    internal double StartMeanLinkAngle { get; private set; }

    private List<double> ComputeAnchors()
    {
        IReadOnlyList<DependencyGraphNode> nodes = _model.Nodes;
        DependencyGraphNode[] connected = nodes.Where(static node => node.Level >= 0).ToArray();
        DependencyGraphNode[] orphans = nodes.Where(static node => node.Level == DependencyGraphNode.UnconnectedLevel)
            .OrderBy(static node => node.Label, StringComparer.Ordinal).ToArray();
        DependencyGraphNode[] missing = nodes.Where(static node => node.Level == DependencyGraphNode.MissingLevel)
            .OrderBy(static node => node.Label, StringComparer.Ordinal).ToArray();

        // Each entity hangs off its most important dependency on the next orbit inward.
        ILookup<DependencyGraphNode, DependencyGraphNode> dependents = _model.EssentialEdges
            .ToLookup(static edge => edge.From, static edge => edge.To);
        ILookup<DependencyGraphNode, DependencyGraphNode> dependencies = _model.EssentialEdges
            .ToLookup(static edge => edge.To, static edge => edge.From);
        Dictionary<DependencyGraphNode, List<DependencyGraphNode>> children = new(ReferenceEqualityComparer.Instance);
        List<DependencyGraphNode> roots = [];
        foreach (DependencyGraphNode node in connected)
        {
            DependencyGraphNode? parent = dependencies[node]
                .Where(item => item.Level == node.Level - 1)
                .OrderByDescending(static item => item.TransitiveDependentCount)
                .ThenBy(static item => item.Rank ?? int.MaxValue)
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

        roots = roots.OrderByDescending(static node => node.TransitiveDependentCount)
            .ThenBy(static node => node.Rank ?? int.MaxValue)
            .ThenBy(static node => node.Label, StringComparer.Ordinal).ToList();
        foreach (List<DependencyGraphNode> list in children.Values)
            list.Sort(static (a, b) => string.CompareOrdinal(a.Label, b.Label));
        Dictionary<DependencyGraphNode, (double Start, double Sweep)> sectors = new(ReferenceEqualityComparer.Instance);
        AssignSectors(roots, children, width, sectors);
        for (int pass = 0; pass < 6; pass++)
        {
            ILookup<DependencyGraphNode, DependencyGraphNode> towards = pass % 2 == 0 ? dependencies : dependents;
            OrderByNeighbours(roots, -Math.PI / 2, towards, sectors);
            foreach ((DependencyGraphNode parent, List<DependencyGraphNode> list) in children)
                OrderByNeighbours(list, sectors[parent].Start, towards, sectors);
            AssignSectors(roots, children, width, sectors);
        }

        // Size each orbit so its entities fit around it. A lone foundation is the centre itself.
        int maxLevel = _model.MaxLevel;
        DependencyGraphNode[][] byLevel = Enumerable.Range(0, Math.Max(maxLevel + 1, 0))
            .Select(level => connected.Where(node => node.Level == level).ToArray()).ToArray();
        double[] radius = new double[byLevel.Length];
        for (int level = 0; level < byLevel.Length; level++)
        {
            double fit = byLevel[level].Length * NodeSpacing / (2 * Math.PI);
            radius[level] = level == 0
                ? byLevel[0].Length <= 1 ? 0 : Math.Max(NodeSpacing * 0.75, fit)
                : Math.Max(radius[level - 1] + RingGap, fit);
        }

        // A clearly dominant foundation sits in the exact centre; the others share the inner orbit.
        DependencyGraphNode? strongest = roots.MaxBy(static node => node.TransitiveDependentCount);
        int runnerUp = roots.Where(node => !ReferenceEquals(node, strongest))
            .Select(static node => node.TransitiveDependentCount).DefaultIfEmpty(0).Max();
        DependencyGraphNode? centre = roots.Count == 1 ||
            (strongest is not null && roots.Count > 1 && strongest.TransitiveDependentCount >= runnerUp * 1.5)
            ? strongest
            : null;

        _centre = centre;
        foreach (DependencyGraphNode node in connected)
            _angles[node] = sectors[node].Start + sectors[node].Sweep / 2;
        StartMeanLinkAngle = MeanLinkAngle(_angles);
        RefineAngles(byLevel, radius, centre, dependencies, dependents);

        // Within an orbit, entities earlier in the dependency-safe rank sit on the inner edge.
        Dictionary<DependencyGraphNode, double> rankShare = new(ReferenceEqualityComparer.Instance);
        foreach (DependencyGraphNode[] orbit in byLevel)
        {
            DependencyGraphNode[] ordered = orbit.OrderBy(static node => node.Rank ?? int.MaxValue)
                .ThenByDescending(static node => node.TransitiveDependentCount)
                .ThenBy(static node => node.Label, StringComparer.Ordinal).ToArray();
            for (int position = 0; position < ordered.Length; position++)
                rankShare[ordered[position]] = ordered.Length == 1 ? 0 : (double)position / (ordered.Length - 1);
        }

        List<double> rings = [];
        for (int level = 0; level < radius.Length; level++)
        {
            if (radius[level] <= 0) continue;
            rings.Add(radius[level] - Band(radius[level]) / 2);
        }

        foreach (DependencyGraphNode node in connected)
        {
            double band = Band(radius[node.Level]);
            double distance = ReferenceEquals(node, centre) ? 0 : radius[node.Level] - band * (1 - rankShare[node]);
            Place(node, distance, _angles[node]);
        }

        double outer = radius.Length == 0 ? 0 : radius[^1];
        if (orphans.Length > 0)
        {
            double belt = Math.Max(outer + RingGap, orphans.Length * NodeSpacing / (2 * Math.PI));
            rings.Add(belt);
            for (int index = 0; index < orphans.Length; index++)
                Place(orphans[index], belt, -Math.PI / 2 + 2 * Math.PI * index / orphans.Length);
            outer = belt;
        }

        // Missing dependencies orbit outside everything, straight out from the entities needing them.
        if (missing.Length > 0)
        {
            double ring = Math.Max(outer + RingGap, missing.Length * NodeSpacing / (2 * Math.PI));
            rings.Add(ring);
            Dictionary<DependencyGraphNode, double> desired = new(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < missing.Length; index++)
            {
                desired[missing[index]] = CircularMean(dependents[missing[index]]
                        .Where(item => _angles.ContainsKey(item) && !ReferenceEquals(item, centre))
                        .Select(item => _angles[item]))
                    ?? -Math.PI / 2 + 2 * Math.PI * index / missing.Length;
            }

            Spread(missing, desired, NodeSpacing / ring);
            foreach (DependencyGraphNode node in missing) Place(node, ring, desired[node]);
        }

        return rings;
    }

    /// <summary>
    /// Places every entity straight out from the entities it depends on: foundations keep their
    /// slice around the centre, and each later orbit sits at the average angle of its dependencies,
    /// with neighbours only pushed apart far enough not to overlap. Orbits therefore do not have to
    /// fill the whole circle. Finally the foundations are reordered whenever that shortens the links.
    /// </summary>
    private void RefineAngles(
        DependencyGraphNode[][] byLevel,
        double[] radius,
        DependencyGraphNode? centre,
        ILookup<DependencyGraphNode, DependencyGraphNode> dependencies,
        ILookup<DependencyGraphNode, DependencyGraphNode> dependents)
    {
        DependencyGraphNode[][] orbits = byLevel
            .Select(level => level.Where(node => !ReferenceEquals(node, centre)).ToArray()).ToArray();
        Dictionary<DependencyGraphNode, double> sliceAngle = new(_angles, ReferenceEqualityComparer.Instance);

        void SweepOutward()
        {
            for (int level = 1; level < orbits.Length; level++)
            {
                DependencyGraphNode[] orbit = orbits[level];
                if (orbit.Length == 0) continue;
                Dictionary<DependencyGraphNode, double> desired = orbit.ToDictionary(
                    node => node,
                    node => CircularMean(dependencies[node]
                            .Where(item => item.Level >= 0 && !ReferenceEquals(item, centre))
                            .Select(item => _angles[item]))
                        ?? sliceAngle[node]);
                Spread(orbit, desired, NodeSpacing / radius[level]);
                foreach (DependencyGraphNode node in orbit) _angles[node] = desired[node];
            }
        }

        SweepOutward();
        if (orbits.Length == 0) return;

        // Everything follows the foundations, so try swapping neighbouring foundations and keep
        // each swap that shortens the links across the whole map.
        DependencyGraphNode[] foundations = orbits[0];
        double best = MeanLinkAngle(_angles);
        for (int pass = 0; pass < 6; pass++)
        {
            bool improved = false;
            DependencyGraphNode[] order = foundations.OrderBy(node => Normalize(_angles[node])).ToArray();
            for (int index = 0; index < order.Length; index++)
            {
                DependencyGraphNode a = order[index];
                DependencyGraphNode b = order[(index + 1) % order.Length];
                if (ReferenceEquals(a, b)) continue;
                Dictionary<DependencyGraphNode, double> snapshot = new(_angles, ReferenceEqualityComparer.Instance);
                (_angles[a], _angles[b]) = (_angles[b], _angles[a]);
                SweepOutward();
                double cost = MeanLinkAngle(_angles);
                if (cost + 1e-9 < best)
                {
                    best = cost;
                    (order[index], order[(index + 1) % order.Length]) = (b, a);
                    improved = true;
                }
                else
                {
                    foreach ((DependencyGraphNode node, double angle) in snapshot) _angles[node] = angle;
                }
            }

            if (!improved) break;
        }
    }

    private double MeanLinkAngle(Dictionary<DependencyGraphNode, double> angles)
    {
        // Links to the centre have no meaningful angle, so they are left out.
        double[] spans = _model.EssentialEdges
            .Where(edge => angles.ContainsKey(edge.From) && angles.ContainsKey(edge.To) &&
                           !ReferenceEquals(edge.From, _centre) && !ReferenceEquals(edge.To, _centre))
            .Select(edge => AngleBetween(angles[edge.From], angles[edge.To]))
            .ToArray();
        return spans.Length == 0 ? 0 : spans.Average();
    }

    private void Place(DependencyGraphNode node, double distance, double angle) =>
        _anchors[node] = (distance * Math.Cos(angle), distance * Math.Sin(angle));

    private static double Band(double radius) => Math.Min(BandDepth, radius * 0.3);

    /// <summary>
    /// Keeps the order given by the desired angles but pushes neighbours apart until every gap is
    /// at least <paramref name="minimumGap"/> radians, wrapping around the circle.
    /// </summary>
    private static void Spread(IReadOnlyList<DependencyGraphNode> orbit,
        Dictionary<DependencyGraphNode, double> desired, double minimumGap)
    {
        int count = orbit.Count;
        if (count < 2) return;
        double gap = Math.Min(minimumGap, 2 * Math.PI / count);
        DependencyGraphNode[] order = orbit
            .OrderBy(node => Normalize(desired[node]))
            .ThenBy(static node => node.Label, StringComparer.Ordinal).ToArray();
        double[] angle = order.Select(node => Normalize(desired[node])).ToArray();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            bool moved = false;
            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                double distance = angle[next] - angle[index] + (next == 0 ? 2 * Math.PI : 0);
                if (distance >= gap - 1e-9) continue;
                double push = (gap - distance) / 2;
                angle[index] -= push;
                angle[next] += push;
                moved = true;
            }

            if (!moved) break;
        }

        for (int index = 0; index < count; index++) desired[order[index]] = angle[index];
    }

    private static double? CircularMean(IEnumerable<double> angles)
    {
        double x = 0, y = 0;
        int count = 0;
        foreach (double angle in angles)
        {
            x += Math.Cos(angle);
            y += Math.Sin(angle);
            count++;
        }

        // Neighbours spread evenly around the circle have no meaningful direction.
        return count == 0 || Math.Sqrt(x * x + y * y) < 0.1 * count ? null : Math.Atan2(y, x);
    }

    private static double AngleBetween(double a, double b)
    {
        double difference = Math.Abs(Normalize(a) - Normalize(b));
        return Math.Min(difference, 2 * Math.PI - difference);
    }

    private static double Normalize(double angle)
    {
        double result = angle % (2 * Math.PI);
        return result < 0 ? result + 2 * Math.PI : result;
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
