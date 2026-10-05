using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyHighlightTests
{
    // address <- customer <- order <- invoice, product <- order, tax is missing, and invoice also
    // names customer directly: a link the chain through order already implies, so it is not drawn.
    private static readonly EntityOverviewRow[] Rows =
    [
        Row(1, "address"),
        Row(2, "customer", "address"),
        Row(3, "product"),
        Row(4, "order", "customer", "product"),
        Row(5, "invoice", "order", "tax", "customer"),
        Row(6, "lonely")
    ];

    [Theory]
    [InlineData(DependencyHighlightMode.Dependencies, new[] { "address", "customer", "order", "product" })]
    [InlineData(DependencyHighlightMode.Dependents, new[] { "invoice", "order" })]
    [InlineData(DependencyHighlightMode.DirectLinks, new[] { "customer", "invoice", "order", "product" })]
    public void EachModeHighlightsItsOwnPartOfTheGraph(DependencyHighlightMode mode, string[] expected)
    {
        DependencyGraphViewModel graph = Graph();
        graph.SolarHighlightMode = mode;

        graph.SelectedNode = Node(graph, "order");

        Assert.Equal(expected, Highlighted(graph));
        Assert.All(graph.HighlightedEdges, edge => Assert.True(edge.IsEssential));
    }

    [Fact]
    public void ImpliedLinksStillReachEntitiesButAreNotHighlighted()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SolarHighlightMode = DependencyHighlightMode.Dependents;

        graph.SelectedNode = Node(graph, "customer");

        Assert.Equal(["customer", "invoice", "order"], Highlighted(graph));
        Assert.DoesNotContain(graph.HighlightedEdges, edge => edge.From.Label == "customer" && edge.To.Label == "invoice");
    }

    [Fact]
    public void SeveralSelectedEntitiesHighlightTheUnion()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SolarHighlightMode = DependencyHighlightMode.Dependencies;

        graph.SelectedNode = Node(graph, "customer");
        graph.ToggleSelection(Node(graph, "product"));

        Assert.Equal(["address", "customer", "product"], Highlighted(graph));
        Assert.Equal(["customer", "product"], graph.SelectedNodes.Select(node => node.Label));
    }

    [Fact]
    public void ToggleSelectionAddsAndRemovesAndTheLatestStaysPrimary()
    {
        DependencyGraphViewModel graph = Graph();
        DependencyGraphNode order = Node(graph, "order");
        DependencyGraphNode invoice = Node(graph, "invoice");

        graph.ToggleSelection(order);
        graph.ToggleSelection(invoice);
        Assert.Same(invoice, graph.SelectedNode);
        Assert.True(graph.IsSelected(order) && graph.IsSelected(invoice));

        graph.ToggleSelection(invoice);
        Assert.Same(order, graph.SelectedNode);
        Assert.False(graph.IsSelected(invoice));

        graph.ToggleSelection(invoice);
        graph.SelectedNode = order;
        Assert.Equal([order], graph.SelectedNodes);

        graph.ClearSelectionCommand.Execute(null);
        Assert.False(graph.HasSelection);
        Assert.Empty(graph.HighlightedNodes);
    }

    [Fact]
    public void EachViewKeepsItsOwnHighlightMode()
    {
        DependencyGraphViewModel graph = Graph();
        List<string?> changed = [];
        graph.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(DependencyHighlightMode.Dependencies, graph.HighlightMode);

        graph.HighlightMode = DependencyHighlightMode.Dependents;
        graph.View = DependencyGraphView.Tree;
        Assert.Equal(DependencyHighlightMode.DirectLinks, graph.HighlightMode);
        graph.HighlightMode = DependencyHighlightMode.Dependencies;
        graph.View = DependencyGraphView.SolarSystem;

        Assert.Equal(DependencyHighlightMode.Dependents, graph.HighlightMode);
        Assert.Equal(DependencyHighlightMode.Dependencies, graph.TreeHighlightMode);
        Assert.Contains(nameof(DependencyGraphViewModel.HighlightMode), changed);
    }

    [Fact]
    public void ChangingTheModeOfTheOtherViewLeavesTheCurrentHighlightAlone()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SelectedNode = Node(graph, "order");
        string[] before = Highlighted(graph);

        graph.TreeHighlightMode = DependencyHighlightMode.Dependents;

        Assert.Equal(before, Highlighted(graph));
    }

    [Fact]
    public void RebuildKeepsEverySelectedEntity()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SelectedNode = Node(graph, "order");
        graph.ToggleSelection(Node(graph, "address"));

        graph.Rebuild(Rows);

        Assert.Equal(["order", "address"], graph.SelectedNodes.Select(node => node.Label));
        Assert.All(graph.SelectedNodes, node => Assert.Contains(node, graph.Model.Nodes));
    }

    private static DependencyGraphViewModel Graph()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Rows);
        return graph;
    }

    private static DependencyGraphNode Node(DependencyGraphViewModel graph, string label) =>
        graph.Model.Nodes.Single(node => node.Label == label);

    private static string[] Highlighted(DependencyGraphViewModel graph) =>
        graph.HighlightedNodes.Select(node => node.Label).Order(StringComparer.Ordinal).ToArray();

    private static EntityOverviewRow Row(int id, string name, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", name, "", "", "CSV", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}
