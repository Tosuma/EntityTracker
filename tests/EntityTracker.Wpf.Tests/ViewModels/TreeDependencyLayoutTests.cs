using System.Windows;

using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class TreeDependencyLayoutTests
{
    // customer <- order <- invoice, address <- customer, product <- order, lonely has no links,
    // and invoice also needs the missing "tax".
    private static readonly EntityOverviewRow[] Rows =
    [
        Row(1, "customer", "address"),
        Row(2, "order", "customer", "product"),
        Row(3, "invoice", "order", "tax"),
        Row(4, "address"),
        Row(5, "product"),
        Row(6, "lonely")
    ];

    [Fact]
    public void EveryDependencySitsAboveTheEntitiesUsingIt()
    {
        DependencyGraphModel model = DependencyGraphBuilder.Build(Rows);
        TreeDependencyLayout tree = new(model);

        Assert.All(model.EssentialEdges, edge =>
            Assert.True(tree.BoxOf(edge.From).Bottom < tree.BoxOf(edge.To).Top,
                $"{edge.From.Label} should be above {edge.To.Label}."));
    }

    [Fact]
    public void MissingDependenciesFormTheTopRowAndUnconnectedEntitiesSitBelowTheTree()
    {
        DependencyGraphModel model = DependencyGraphBuilder.Build(Rows);
        TreeDependencyLayout tree = new(model);
        DependencyGraphNode Node(string label) => model.Nodes.Single(node => node.Label == label);
        DependencyGraphNode[] connected = model.Nodes.Where(node => node.Level >= 0).ToArray();

        Assert.All(connected, node => Assert.True(tree.BoxOf(Node("tax")).Bottom < tree.BoxOf(node).Top));
        Assert.All(connected, node => Assert.True(tree.BoxOf(Node("lonely")).Top > tree.BoxOf(node).Bottom));
        Assert.Equal(tree.BoxOf(Node("address")).Top, tree.BoxOf(Node("product")).Top);
    }

    [Fact]
    public void BoxesNeverOverlap()
    {
        Random random = new(3);
        EntityOverviewRow[] rows = Enumerable.Range(1, 80).Select(id =>
        {
            int layer = (id - 1) / 10;
            string[] dependencies = layer == 0 ? [] : Enumerable.Range(0, random.Next(1, 4))
                .Select(_ => $"e{random.Next(1, layer * 10 + 1)}").Distinct().ToArray();
            return Row(id, $"e{id}", dependencies);
        }).ToArray();
        TreeDependencyLayout tree = new(DependencyGraphBuilder.Build(rows));

        Rect[] boxes = tree.Boxes.Values.ToArray();
        for (int a = 0; a < boxes.Length; a++)
            for (int b = a + 1; b < boxes.Length; b++)
                Assert.False(boxes[a].IntersectsWith(boxes[b]) &&
                             Rect.Intersect(boxes[a], boxes[b]) is { Width: > 0.01, Height: > 0.01 });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(42)]
    public void LinesNeverPassUnderABox(int seed)
    {
        // Regression (#112): a line could cut under a neighbouring box, or its corner, at mid-height.
        const double Clearance = 4;
        DependencyGraphModel model = DependencyGraphBuilder.Build(RandomLayeredRows(seed));
        TreeDependencyLayout tree = new(model);

        foreach ((DependencyGraphEdge edge, IReadOnlyList<Point> route) in tree.Routes)
        {
            Rect from = tree.BoxOf(edge.From);
            Rect to = tree.BoxOf(edge.To);
            foreach (Point point in SampleCurve(route))
            {
                foreach (Rect box in tree.Boxes.Values)
                {
                    // The line starts and ends on its own boxes' edges; every other box keeps a margin.
                    bool own = box == from || box == to;
                    Rect forbidden = box;
                    forbidden.Inflate(own ? -0.5 : Clearance, own ? -0.5 : Clearance);
                    Assert.False(forbidden.Contains(point),
                        $"The {edge.From.Label} -> {edge.To.Label} line passes under a box at ({point.X:0}, {point.Y:0}).");
                }
            }
        }
    }

    [Fact]
    public void LongLinksBendBetweenBoxesThroughWaypoints()
    {
        // "deep" sits three rows below "base"; its direct link to "base" crosses two rows of boxes.
        EntityOverviewRow[] rows =
        [
            Row(1, "base"),
            Row(2, "a1"),
            Row(3, "a2", "a1"),
            Row(4, "a3", "a2"),
            Row(5, "deep", "a3", "base"),
            Row(6, "b2", "base"),
            Row(7, "b3", "b2")
        ];
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);
        TreeDependencyLayout tree = new(model);
        DependencyGraphEdge longLink = model.EssentialEdges.Single(edge => edge.From.Label == "base" && edge.To.Label == "deep");

        IReadOnlyList<Point> route = tree.Routes[longLink];
        Assert.Equal(6, route.Count); // start, the top and bottom of each of the two crossed rows, end
        Point[] waypoints = route.Skip(1).SkipLast(1).ToArray();
        Assert.All(waypoints, waypoint =>
            Assert.DoesNotContain(tree.Boxes.Values, box => box.Contains(waypoint)));
        for (int pair = 0; pair < waypoints.Length; pair += 2)
        {
            // A crossed row's pair spans the row's boxes from top to bottom at one X.
            Rect row = tree.Boxes.Values.First(box => box.Top == waypoints[pair].Y);
            Assert.Equal(waypoints[pair].X, waypoints[pair + 1].X);
            Assert.Equal(row.Bottom, waypoints[pair + 1].Y);
        }
        Assert.Equal(tree.BoxOf(longLink.From).Bottom, route[0].Y);
        Assert.Equal(tree.BoxOf(longLink.To).Top, route[^1].Y);
    }

    [Fact]
    public void RowsAreOrderedToAvoidCrossingLines()
    {
        // Sorted by name the children start crossed: a_child needs B and b_child needs A.
        EntityOverviewRow[] rows =
        [
            Row(1, "A"),
            Row(2, "B"),
            Row(3, "a_child", "B"),
            Row(4, "b_child", "A")
        ];

        TreeDependencyLayout tree = new(DependencyGraphBuilder.Build(rows));

        Assert.Equal(0, tree.CrossingCount());
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        TreeDependencyLayout first = new(DependencyGraphBuilder.Build(Rows));
        TreeDependencyLayout second = new(DependencyGraphBuilder.Build(Rows));

        Assert.Equal(first.Boxes.Values.OrderBy(box => box.X).ThenBy(box => box.Y),
            second.Boxes.Values.OrderBy(box => box.X).ThenBy(box => box.Y));
    }

    [Fact]
    public void GraphReportsTreePositionsOnlyInTreeView()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Rows);
        DependencyGraphNode order = graph.Model.Find(Rows[1].EntityId)!;
        Point solar = new(order.X, order.Y);

        graph.View = EntityTracker.Infrastructure.Configuration.DependencyGraphView.Tree;
        Assert.Equal(graph.TreeLayout.CenterOf(order), graph.PositionOf(order));
        Assert.DoesNotContain(graph.Legend, item => item.Label.Contains("ring", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Level: Level 2", graph.Describe(order).Lines);

        graph.View = EntityTracker.Infrastructure.Configuration.DependencyGraphView.SolarSystem;
        Assert.Equal(solar, graph.PositionOf(order));
        Assert.Contains("Ring: Level 2", graph.Describe(order).Lines);
    }

    [Fact]
    public void SelectingInTheTreeHighlightsDirectLinksBothWaysLikeHovering()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Rows);
        DependencyGraphNode order = graph.Model.Find(Rows[1].EntityId)!;
        string[] Highlighted() => graph.HighlightedNodes.Select(node => node.Label).Order(StringComparer.Ordinal).ToArray();
        graph.SelectedNode = order;

        // The solar system follows everything the selection depends on.
        Assert.Equal(["address", "customer", "order", "product"], Highlighted());

        graph.View = EntityTracker.Infrastructure.Configuration.DependencyGraphView.Tree;
        Assert.Equal(["customer", "invoice", "order", "product"], Highlighted());
        Assert.All(graph.HighlightedEdges, edge =>
            Assert.True(ReferenceEquals(edge.From, order) || ReferenceEquals(edge.To, order)));
        Assert.Equal(3, graph.HighlightedEdges.Count);
    }

    private static EntityOverviewRow[] RandomLayeredRows(int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(1, 80).Select(id =>
        {
            int layer = (id - 1) / 10;
            string[] dependencies = layer == 0 ? [] : Enumerable.Range(0, random.Next(1, 4))
                .Select(_ => $"e{random.Next(1, layer * 10 + 1)}").Distinct().ToArray();
            return Row(id, $"e{id}", dependencies);
        }).ToArray();
    }

    /// <summary>Points along the drawn curves of a route, as the canvas and the report draw them.</summary>
    private static IEnumerable<Point> SampleCurve(IReadOnlyList<Point> route)
    {
        for (int index = 1; index < route.Count; index++)
        {
            Point start = route[index - 1];
            Point end = route[index];
            (Point first, Point second) = TreeDependencyLayout.CurveControls(start, end);
            for (int step = 0; step <= 40; step++)
            {
                double t = step / 40.0;
                double u = 1 - t;
                yield return new Point(
                    u * u * u * start.X + 3 * u * u * t * first.X + 3 * u * t * t * second.X + t * t * t * end.X,
                    u * u * u * start.Y + 3 * u * u * t * first.Y + 3 * u * t * t * second.Y + t * t * t * end.Y);
            }
        }
    }

    private static EntityOverviewRow Row(int id, string name, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", name, "", "", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}
