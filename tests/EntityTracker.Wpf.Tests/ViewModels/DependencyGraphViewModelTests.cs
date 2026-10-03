using System.IO;

using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyGraphViewModelTests
{
    // customer <- order <- invoice, address <- customer, product <- order, lonely (no links),
    // invoice also depends on the missing "tax".
    private static readonly EntityOverviewRow[] Rows =
    [
        Row(1, "customer", DevelopmentStatus.Reconciled, "address"),
        Row(2, "order", DevelopmentStatus.InProgress, "customer", "product"),
        Row(3, "invoice", DevelopmentStatus.NotStarted, "order", "tax"),
        Row(4, "address", DevelopmentStatus.Reconciled),
        Row(5, "product", DevelopmentStatus.Blocked),
        Row(6, "lonely", DevelopmentStatus.NotStarted)
    ];

    [Fact]
    public void Builder_LinksDependencyToDependentAndSharesMissingPlaceholders()
    {
        EntityOverviewRow[] rows = [.. Rows, Row(7, "credit_note", DevelopmentStatus.NotStarted, "tax", "tax")];

        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);

        Assert.Equal(8, model.Nodes.Count);
        Assert.Contains(model.Edges, edge => edge.From.Label == "customer" && edge.To.Label == "order");
        Assert.DoesNotContain(model.Edges, edge => edge.From.Label == "order" && edge.To.Label == "customer");
        DependencyGraphNode tax = Assert.Single(model.Nodes, node => node.IsPlaceholder);
        Assert.Equal("tax", tax.Label);
        Assert.Equal(2, tax.DependentCount);
        Assert.Equal(2, model.Edges.Count(edge => ReferenceEquals(edge.From, tax)));
        Assert.False(Assert.Single(model.Nodes, node => node.Label == "lonely").IsConnected);
        Assert.Equal(DevelopmentStatus.Blocked, model.Find(Rows[4].EntityId)?.Status);
    }

    [Fact]
    public void Builder_KeepsPositionsOfExistingNodesAcrossRebuilds()
    {
        DependencyGraphModel first = DependencyGraphBuilder.Build(Rows);
        new RadialDependencyLayout(first).Settle();
        DependencyGraphNode order = first.Find(Rows[1].EntityId)!;

        DependencyGraphModel second = DependencyGraphBuilder.Build(Rows, first);
        new RadialDependencyLayout(second);

        DependencyGraphNode rebuilt = second.Find(Rows[1].EntityId)!;
        Assert.NotSame(order, rebuilt);
        Assert.Equal(order.X, rebuilt.X);
        Assert.Equal(order.Y, rebuilt.Y);
    }

    [Fact]
    public void Layout_IsDeterministicFiniteAndPullsLinkedNodesTogether()
    {
        DependencyGraphModel first = DependencyGraphBuilder.Build(Rows);
        DependencyGraphModel second = DependencyGraphBuilder.Build(Rows);
        RadialDependencyLayout layout = new(first);
        layout.Settle();
        new RadialDependencyLayout(second).Settle();

        Assert.True(layout.IsSettled);
        Assert.Equal(first.Nodes.Select(node => (node.X, node.Y)), second.Nodes.Select(node => (node.X, node.Y)));
        Assert.All(first.Nodes, node => Assert.True(double.IsFinite(node.X) && double.IsFinite(node.Y)));
        double linked = first.Edges.Average(edge => Distance(edge.From, edge.To));
        double allPairs = first.Nodes.SelectMany((a, i) => first.Nodes.Skip(i + 1), Distance).Average();
        Assert.True(linked < allPairs, $"Linked {linked:0.0} should be shorter than average {allPairs:0.0}.");
    }

    [Fact]
    public void ImpliedLinks_AreHiddenButStillPartOfTheHighlightedChain()
    {
        EntityOverviewRow[] rows =
        [
            Row(1, "customer", DevelopmentStatus.Reconciled),
            Row(2, "order", DevelopmentStatus.InProgress, "customer"),
            Row(3, "invoice", DevelopmentStatus.NotStarted, "order", "customer")
        ];
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(rows);
        DependencyGraphEdge implied = graph.Model.Edges.Single(edge =>
            edge.From.Label == "customer" && edge.To.Label == "invoice");

        Assert.False(implied.IsEssential);
        Assert.False(graph.IsVisible(implied));
        Assert.Equal(2, graph.Model.EssentialEdges.Count);
        Assert.StartsWith("3 entities · 2 links", graph.Summary);
        Assert.Contains("1 link is hidden", graph.HiddenLinksDescription);

        graph.SelectedNode = graph.Model.Find(rows[2].EntityId);
        Assert.Contains(graph.HighlightedNodes, node => node.Label == "customer");
        Assert.DoesNotContain(implied, graph.HighlightedEdges);
        Assert.Equal(2, graph.HighlightedEdges.Count);
    }

    [Fact]
    public void Levels_PlaceFoundationsInTheCentreAndDependentsFurtherOut()
    {
        DependencyGraphModel model = DependencyGraphBuilder.Build(Rows);

        Assert.Equal(0, model.Find(Rows[3].EntityId)!.Level); // address: no dependencies
        Assert.Equal(0, model.Find(Rows[4].EntityId)!.Level); // product
        Assert.Equal(0, model.Nodes.Single(node => node.Label == "tax").Level); // missing placeholder
        Assert.Equal(1, model.Find(Rows[0].EntityId)!.Level); // customer
        Assert.Equal(2, model.Find(Rows[1].EntityId)!.Level); // order: customer is its deepest dependency
        Assert.Equal(3, model.Find(Rows[2].EntityId)!.Level); // invoice
        Assert.Equal(-1, model.Nodes.Single(node => node.Label == "lonely").Level);
        Assert.Equal(3, model.Find(Rows[3].EntityId)!.TransitiveDependentCount); // customer, order, invoice
        Assert.Equal(0, model.Find(Rows[2].EntityId)!.TransitiveDependentCount);
        Assert.All(model.EssentialEdges, edge => Assert.True(edge.From.Level < edge.To.Level));
    }

    [Fact]
    public void RadialLayout_PlacesEarlierRankedEntitiesOnTheInnerEdgeOfTheirOrbit()
    {
        EntityOverviewRow[] rows =
        [
            Row(1, "base", DevelopmentStatus.NotStarted),
            Row(2, "late", DevelopmentStatus.NotStarted, "base") with { Rank = "3" },
            Row(3, "early", DevelopmentStatus.NotStarted, "base") with { Rank = "2" }
        ];
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);
        new RadialDependencyLayout(model).Settle(2000);

        DependencyGraphNode Node(string label) => model.Nodes.Single(node => node.Label == label);
        Assert.Equal(2, Node("early").Rank);
        Assert.True(Radius(Node("early")) < Radius(Node("late")));
        Assert.True(Radius(Node("base")) < Radius(Node("early")));
    }

    [Fact]
    public void CyclicInput_StillGetsFiniteLevelsAndLayout()
    {
        EntityOverviewRow[] rows =
        [
            Row(1, "a", DevelopmentStatus.NotStarted, "b"),
            Row(2, "b", DevelopmentStatus.NotStarted, "c"),
            Row(3, "c", DevelopmentStatus.NotStarted, "a")
        ];

        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);
        new RadialDependencyLayout(model).Settle();

        Assert.All(model.Nodes, node => Assert.InRange(node.Level, 0, 3));
        Assert.All(model.Nodes, node => Assert.True(double.IsFinite(node.X) && double.IsFinite(node.Y)));
    }

    [Fact]
    public void RadialLayout_OrdersOrbitsOutwardWithOrphansOutside()
    {
        DependencyGraphModel model = DependencyGraphBuilder.Build(Rows);
        RadialDependencyLayout layout = new(model);
        layout.Settle(2000);

        double[] averageRadius = Enumerable.Range(0, model.MaxLevel + 1)
            .Select(level => model.Nodes.Where(node => node.Level == level).Average(Radius))
            .ToArray();
        for (int level = 1; level < averageRadius.Length; level++)
            Assert.True(averageRadius[level] > averageRadius[level - 1],
                $"Level {level} should orbit outside level {level - 1}.");
        DependencyGraphNode hub = model.Nodes.MaxBy(node => node.TransitiveDependentCount)!; // address
        Assert.Equal(hub, model.Nodes.MinBy(Radius));
        double lastRing = model.Nodes.Where(node => node.Level >= 0).Max(Radius);
        Assert.True(Radius(model.Nodes.Single(node => node.Level < 0)) > lastRing);
        Assert.Equal(model.MaxLevel + 2, layout.RingRadii.Count); // orbits 0..max plus the orphan belt
    }

    [Fact]
    public void RadialLayout_GivesEachChainItsOwnSliceAndKeepsDroppedNodes()
    {
        // Two equally weighted hubs: their dependencies must stay on their own side of the map.
        EntityOverviewRow[] rows =
        [
            Row(1, "left_hub", DevelopmentStatus.NotStarted, "left_a", "left_b"),
            Row(2, "left_a", DevelopmentStatus.NotStarted),
            Row(3, "left_b", DevelopmentStatus.NotStarted),
            Row(4, "right_hub", DevelopmentStatus.NotStarted, "right_a", "right_b"),
            Row(5, "right_a", DevelopmentStatus.NotStarted),
            Row(6, "right_b", DevelopmentStatus.NotStarted)
        ];
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);
        RadialDependencyLayout layout = new(model);
        layout.Settle(2000);

        DependencyGraphNode Node(string label) => model.Nodes.Single(node => node.Label == label);
        double AngleBetween(DependencyGraphNode a, DependencyGraphNode b)
        {
            double difference = Math.Abs(Math.Atan2(a.Y, a.X) - Math.Atan2(b.Y, b.X));
            return Math.Min(difference, 2 * Math.PI - difference);
        }

        Assert.True(AngleBetween(Node("right_a"), Node("right_hub")) <
                    AngleBetween(Node("right_a"), Node("left_hub")));
        Assert.True(AngleBetween(Node("left_a"), Node("left_b")) <
                    AngleBetween(Node("left_a"), Node("right_a")));

        DependencyGraphNode dropped = Node("left_a");
        dropped.X = 500;
        dropped.Y = -500;
        layout.MoveAnchor(dropped);
        layout.Reheat();
        layout.Settle(2000);
        Assert.InRange(dropped.X, 495, 505);
        Assert.InRange(dropped.Y, -505, -495);
    }

    [Fact]
    public void Selection_HighlightsTransitiveDependenciesButNotDependents()
    {
        DependencyGraphViewModel graph = CreateGraph();
        graph.SelectedNode = graph.Model.Find(Rows[1].EntityId); // order

        Assert.Equal(["address", "customer", "order", "product"],
            graph.HighlightedNodes.Select(node => node.Label).Order());
        Assert.Equal(3, graph.HighlightedEdges.Count);
        Assert.DoesNotContain(graph.HighlightedNodes, node => node.Label == "invoice");
        Assert.Contains("3 entities", graph.SelectionDescription);

        graph.SelectedNode = graph.Model.Find(Rows[2].EntityId); // invoice
        Assert.Contains(graph.HighlightedNodes, node => node.Label == "tax");
        Assert.Equal(6, graph.HighlightedNodes.Count);
    }

    [Fact]
    public void FocusAndHideUnconnected_FilterVisibleNodes()
    {
        DependencyGraphViewModel graph = CreateGraph();
        DependencyGraphNode lonely = graph.Model.Nodes.Single(node => node.Label == "lonely");
        DependencyGraphNode invoice = graph.Model.Find(Rows[2].EntityId)!;

        graph.HideUnconnected = true;
        Assert.False(graph.IsVisible(lonely));
        Assert.True(graph.IsVisible(invoice));

        graph.SelectedNode = graph.Model.Find(Rows[1].EntityId);
        graph.IsFocusMode = true;
        Assert.False(graph.IsVisible(invoice));
        Assert.Equal(4, graph.Model.Nodes.Count(graph.IsVisible));
        Assert.All(graph.Model.Edges.Where(graph.IsVisible), edge => Assert.Contains(edge, graph.HighlightedEdges));

        graph.SelectedNode = null;
        Assert.True(graph.IsVisible(invoice));
    }

    [Fact]
    public void Find_SelectsMatchAndRequestsCentering()
    {
        DependencyGraphViewModel graph = CreateGraph();
        DependencyGraphNode? centered = null;
        graph.CenterOnRequested += (_, node) => centered = node;
        graph.HideUnconnected = true;

        graph.SearchText = "LONE";
        graph.FindCommand.Execute(null);

        Assert.Equal("lonely", graph.SelectedNode?.Label);
        Assert.Same(graph.SelectedNode, centered);
        Assert.False(graph.HideUnconnected);

        graph.SearchText = "nothing";
        graph.FindCommand.Execute(null);
        Assert.True(graph.HasSearchMessage);
        Assert.Equal("lonely", graph.SelectedNode?.Label);
    }

    [Fact]
    public void OpenDetails_OpensEntitiesButNotPlaceholders()
    {
        List<EntityId> opened = [];
        DependencyGraphViewModel graph = new(id => { opened.Add(id); return true; });
        graph.Rebuild(Rows);

        Assert.True(graph.OpenDetails(graph.Model.Find(Rows[0].EntityId)!));
        Assert.False(graph.OpenDetails(graph.Model.Nodes.Single(node => node.IsPlaceholder)));
        Assert.Equal([Rows[0].EntityId], opened);
    }

    [Fact]
    public void Rebuild_KeepsSelectionAndExportWritesThroughControl()
    {
        string path = Path.Combine(Path.GetTempPath(), $"graph-{Guid.NewGuid():N}.png");
        NotificationCenter notifications = new();
        DependencyGraphViewModel graph = new(_ => true, new StubPngPicker(path), notifications);
        graph.Rebuild(Rows);
        graph.SelectedNode = graph.Model.Find(Rows[1].EntityId);
        Assert.False(graph.ExportPngCommand.CanExecute(null));
        string? written = null;
        graph.PngWriter = target => written = target;

        graph.Rebuild(Rows);
        graph.ExportPngCommand.Execute(null);

        Assert.Equal("order", graph.SelectedNode?.Label);
        Assert.Equal(4, graph.HighlightedNodes.Count);
        Assert.Equal(path, written);
        Assert.Contains(notifications.Items, item => item.Kind == NotificationKind.Success);
    }

    private static DependencyGraphViewModel CreateGraph()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Rows);
        return graph;
    }

    private static double Radius(DependencyGraphNode node) => Math.Sqrt(node.X * node.X + node.Y * node.Y);

    private static double Distance(DependencyGraphNode a, DependencyGraphNode b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static EntityOverviewRow Row(int id, string name, DevelopmentStatus status,
        params string[] dependencies) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            EntityLifecycleState.Active,
            status,
            EntityWorkflowState.Ready,
            DependencyResolutionState.Resolved,
            "—",
            "—",
            name,
            string.Empty,
            string.Empty,
            "CSV",
            status.ToString(),
            EntityWorkflowState.Ready.ToString(),
            dependencies.Length.ToString(),
            dependencies,
            [],
            string.Empty,
            string.Empty,
            string.Empty,
            "—",
            string.Empty,
            "Edit entity");

    private sealed class StubPngPicker(string path) : IProgressChartFilePicker
    {
        public string? SelectPngPath(string suggestedFileName) => path;
    }
}
