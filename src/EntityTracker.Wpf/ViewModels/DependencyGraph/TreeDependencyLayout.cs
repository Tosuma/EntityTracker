using System.Windows;

namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>
/// A classical top-to-bottom dependency tree: an entity sits one row below its deepest dependency.
/// Missing dependencies form the top row, and entities without links get their own section below
/// the tree. Links spanning several rows bend through invisible waypoints so they pass between
/// boxes, and each row is ordered to reduce crossing lines.
/// </summary>
public sealed class TreeDependencyLayout
{
    public const double BoxWidth = 150;
    public const double BoxHeight = 81;
    public const double NameHeight = 54;
    public const double HorizontalGap = 28;
    public const double RowGap = 112;
    private const double WaypointWidth = 12;
    private const double UnconnectedGap = 96;

    private readonly Dictionary<DependencyGraphNode, Rect> _boxes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DependencyGraphEdge, IReadOnlyList<Point>> _routes = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<Item>> _rows = [];

    public TreeDependencyLayout(DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        BuildRows(model, out List<DependencyGraphNode> unconnected);
        OrderRows();
        AssignPositions();
        Record(model);
        PlaceUnconnected(unconnected);
        Bounds = _boxes.Count == 0 ? Rect.Empty : _boxes.Values.Aggregate(Rect.Union);
    }

    /// <summary>Gets the area every box fits in.</summary>
    public Rect Bounds { get; }

    public IReadOnlyDictionary<DependencyGraphNode, Rect> Boxes => _boxes;

    /// <summary>Gets the points each drawn link passes: start, any waypoints, end.</summary>
    public IReadOnlyDictionary<DependencyGraphEdge, IReadOnlyList<Point>> Routes => _routes;

    public Rect BoxOf(DependencyGraphNode node) => _boxes.GetValueOrDefault(node, Rect.Empty);

    public Point CenterOf(DependencyGraphNode node)
    {
        Rect box = BoxOf(node);
        return box.IsEmpty ? default : new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>Counts link crossings between neighbouring rows; used to check the ordering.</summary>
    internal int CrossingCount()
    {
        int crossings = 0;
        for (int row = 0; row + 1 < _rows.Count; row++)
        {
            List<(double Top, double Bottom)> links = [];
            foreach (Item item in _rows[row])
                foreach (Item below in item.Below)
                    links.Add((item.X, below.X));
            for (int a = 0; a < links.Count; a++)
                for (int b = a + 1; b < links.Count; b++)
                    if ((links[a].Top - links[b].Top) * (links[a].Bottom - links[b].Bottom) < 0)
                        crossings++;
        }

        return crossings;
    }

    private void BuildRows(DependencyGraphModel model, out List<DependencyGraphNode> unconnected)
    {
        unconnected = model.Nodes.Where(static node => node.Level == DependencyGraphNode.UnconnectedLevel)
            .OrderBy(static node => node.Rank ?? int.MaxValue)
            .ThenBy(static node => node.Label, StringComparer.Ordinal).ToList();
        bool hasMissing = model.Nodes.Any(static node => node.Level == DependencyGraphNode.MissingLevel);
        int Row(DependencyGraphNode node) => node.Level == DependencyGraphNode.MissingLevel
            ? 0
            : node.Level + (hasMissing ? 1 : 0);

        Dictionary<DependencyGraphNode, Item> items = new(ReferenceEqualityComparer.Instance);
        foreach (DependencyGraphNode node in model.Nodes
                     .Where(static node => node.Level != DependencyGraphNode.UnconnectedLevel)
                     .OrderBy(static node => node.Rank ?? int.MaxValue)
                     .ThenBy(static node => node.Label, StringComparer.Ordinal))
        {
            Item item = new(node, null, Row(node));
            items[node] = item;
            RowList(item.Row).Add(item);
        }

        foreach (DependencyGraphEdge edge in model.EssentialEdges)
        {
            if (!items.TryGetValue(edge.From, out Item? from) || !items.TryGetValue(edge.To, out Item? to)) continue;
            if (to.Row <= from.Row) continue; // only a link closing a cycle can point upward; it is drawn straight
            Item upper = from;
            for (int row = from.Row + 1; row < to.Row; row++)
            {
                Item waypoint = new(null, edge, row);
                RowList(row).Add(waypoint);
                Link(upper, waypoint);
                upper = waypoint;
            }

            Link(upper, to);
        }
    }

    private List<Item> RowList(int row)
    {
        while (_rows.Count <= row) _rows.Add([]);
        return _rows[row];
    }

    private static void Link(Item upper, Item lower)
    {
        upper.Below.Add(lower);
        lower.Above.Add(upper);
    }

    /// <summary>
    /// Orders every row by the average position of its linked items, sweeping down and up a few
    /// times, and keeps the ordering with the fewest crossings.
    /// </summary>
    private void OrderRows()
    {
        Renumber();
        int best = CrossingsByOrder();
        List<List<Item>> bestOrder = Snapshot();
        for (int pass = 0; pass < 8; pass++)
        {
            bool down = pass % 2 == 0;
            IEnumerable<int> rows = down
                ? Enumerable.Range(1, Math.Max(_rows.Count - 1, 0))
                : Enumerable.Range(0, Math.Max(_rows.Count - 1, 0)).Reverse();
            foreach (int row in rows)
            {
                List<Item> items = _rows[row];
                foreach (Item item in items)
                {
                    List<Item> neighbours = down ? item.Above : item.Below;
                    item.Key = neighbours.Count == 0 ? item.Order : neighbours.Average(static other => other.Order);
                }

                items.Sort(static (a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Order.CompareTo(b.Order));
                for (int index = 0; index < items.Count; index++) items[index].Order = index;
            }

            int crossings = CrossingsByOrder();
            if (crossings < best)
            {
                best = crossings;
                bestOrder = Snapshot();
            }
        }

        for (int row = 0; row < _rows.Count; row++) _rows[row] = bestOrder[row];
        Renumber();
    }

    private void Renumber()
    {
        foreach (List<Item> row in _rows)
            for (int index = 0; index < row.Count; index++)
                row[index].Order = index;
    }

    private List<List<Item>> Snapshot() => _rows.Select(static row => row.ToList()).ToList();

    private int CrossingsByOrder()
    {
        int crossings = 0;
        for (int row = 0; row + 1 < _rows.Count; row++)
        {
            List<(int Top, int Bottom)> links = [];
            foreach (Item item in _rows[row])
                foreach (Item below in item.Below)
                    links.Add((item.Order, below.Order));
            for (int a = 0; a < links.Count; a++)
                for (int b = a + 1; b < links.Count; b++)
                    if ((links[a].Top - links[b].Top) * (links[a].Bottom - links[b].Bottom) < 0)
                        crossings++;
        }

        return crossings;
    }

    /// <summary>
    /// Packs each row centred on zero, then pulls every item toward the middle of its linked items
    /// while keeping the order and a minimum gap, so dependency chains line up vertically.
    /// </summary>
    private void AssignPositions()
    {
        foreach (List<Item> row in _rows)
        {
            double cursor = -row.Sum(static item => item.Width + HorizontalGap) / 2;
            foreach (Item item in row)
            {
                item.X = cursor + item.Width / 2;
                cursor += item.Width + HorizontalGap;
            }
        }

        for (int pass = 0; pass < 12; pass++)
        {
            bool down = pass % 2 == 0;
            IEnumerable<int> rows = down
                ? Enumerable.Range(0, _rows.Count)
                : Enumerable.Range(0, _rows.Count).Reverse();
            foreach (int index in rows)
            {
                List<Item> row = _rows[index];
                double[] desired = row.Select(item =>
                {
                    List<Item> neighbours = item.Above.Concat(item.Below).ToList();
                    return neighbours.Count == 0 ? item.X : neighbours.Average(static other => other.X);
                }).ToArray();
                Spread(row, desired);
            }
        }
    }

    /// <summary>Moves a row as close to the desired positions as its order and gaps allow.</summary>
    private static void Spread(List<Item> row, double[] desired)
    {
        int count = row.Count;
        if (count == 0) return;
        double[] left = new double[count];
        double[] right = new double[count];
        for (int index = 0; index < count; index++)
        {
            left[index] = index == 0 ? desired[0]
                : Math.Max(desired[index], left[index - 1] + Separation(row[index - 1], row[index]));
        }

        for (int index = count - 1; index >= 0; index--)
        {
            right[index] = index == count - 1 ? desired[index]
                : Math.Min(desired[index], right[index + 1] - Separation(row[index], row[index + 1]));
        }

        // Average the left-packed and right-packed solutions, then repair any remaining overlap.
        for (int index = 0; index < count; index++) row[index].X = (left[index] + right[index]) / 2;
        for (int index = 1; index < count; index++)
            row[index].X = Math.Max(row[index].X, row[index - 1].X + Separation(row[index - 1], row[index]));
    }

    private static double Separation(Item a, Item b) =>
        a.Width / 2 + b.Width / 2 + (a.Node is null || b.Node is null ? HorizontalGap / 2 : HorizontalGap);

    private static double RowTop(int row) => row * (BoxHeight + RowGap);

    private void Record(DependencyGraphModel model)
    {
        Dictionary<DependencyGraphNode, Item> byNode = new(ReferenceEqualityComparer.Instance);
        foreach (List<Item> row in _rows)
        {
            foreach (Item item in row)
            {
                if (item.Node is null) continue;
                byNode[item.Node] = item;
                _boxes[item.Node] = new Rect(item.X - BoxWidth / 2, RowTop(item.Row), BoxWidth, BoxHeight);
            }
        }

        foreach (DependencyGraphEdge edge in model.EssentialEdges)
        {
            if (!byNode.TryGetValue(edge.From, out Item? from) || !byNode.TryGetValue(edge.To, out Item? to)) continue;
            List<Point> route = [new Point(from.X, RowTop(from.Row) + BoxHeight)];
            if (to.Row > from.Row)
            {
                Item current = from;
                for (int row = from.Row + 1; row < to.Row; row++)
                {
                    current = current.Below.First(item => ReferenceEquals(item.Edge, edge));
                    route.Add(new Point(current.X, RowTop(row) + BoxHeight / 2));
                }
            }

            route.Add(new Point(to.X, RowTop(to.Row)));
            _routes[edge] = route;
        }
    }

    /// <summary>Lays out entities without links in a grid below the tree, about as wide as the tree.</summary>
    private void PlaceUnconnected(List<DependencyGraphNode> unconnected)
    {
        if (unconnected.Count == 0) return;
        double treeWidth = _boxes.Count == 0 ? 0 : _boxes.Values.Aggregate(Rect.Union).Width;
        int columns = Math.Clamp((int)((treeWidth + HorizontalGap) / (BoxWidth + HorizontalGap)), 6, Math.Max(6, unconnected.Count));
        columns = Math.Min(columns, unconnected.Count);
        double top = _rows.Count == 0 ? 0 : RowTop(_rows.Count - 1) + BoxHeight + UnconnectedGap;
        double rowWidth = columns * BoxWidth + (columns - 1) * HorizontalGap;
        for (int index = 0; index < unconnected.Count; index++)
        {
            int column = index % columns;
            int line = index / columns;
            _boxes[unconnected[index]] = new Rect(-rowWidth / 2 + column * (BoxWidth + HorizontalGap),
                top + line * (BoxHeight + HorizontalGap), BoxWidth, BoxHeight);
        }
    }

    private sealed class Item(DependencyGraphNode? node, DependencyGraphEdge? edge, int row)
    {
        public DependencyGraphNode? Node { get; } = node;
        public DependencyGraphEdge? Edge { get; } = edge;
        public int Row { get; } = row;
        public double Width => Node is null ? WaypointWidth : BoxWidth;
        public List<Item> Above { get; } = [];
        public List<Item> Below { get; } = [];
        public int Order { get; set; }
        public double Key { get; set; }
        public double X { get; set; }
    }
}
