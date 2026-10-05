using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyGraphSearchTests
{
    private static readonly EntityOverviewRow[] Rows =
    [
        Row(1, "customer_preference"),
        Row(2, "CustomerAddress", "customer_preference"),
        Row(3, "sales order line", "CustomerAddress"),
        Row(4, "preferenceCustomer"),
        Row(5, "customer")
    ];

    [Theory]
    [InlineData("cust pref")]
    [InlineData("CustPref")]
    [InlineData("cust_pref")]
    public void WordQueriesSuggestNamesInEveryStyle(string query)
    {
        DependencyGraphViewModel graph = Graph();

        graph.SearchText = query;

        Assert.Equal("customer_preference", Assert.Single(graph.Suggestions).Label);
        Assert.True(graph.IsSuggestionsOpen);
    }

    [Fact]
    public void SuggestionsAreRankedExactThenPrefixThenWords()
    {
        DependencyGraphViewModel graph = Graph();

        graph.SearchText = "customer";

        // Exact first, then the two prefix matches alphabetically, then the later-word match.
        Assert.Equal(["customer", "CustomerAddress", "customer_preference", "preferenceCustomer"],
            graph.Suggestions.Select(node => node.Label));
    }

    [Fact]
    public void FindSelectsAndCentresTheBestMatch()
    {
        DependencyGraphViewModel graph = Graph();
        List<DependencyGraphNode> centred = [];
        graph.CenterOnRequested += (_, node) => centred.Add(node);
        graph.SearchText = "order line";

        graph.FindCommand.Execute(null);

        Assert.Equal("sales order line", graph.SelectedNode?.Label);
        Assert.Equal([graph.SelectedNode!], centred);
        Assert.False(graph.IsSuggestionsOpen);
        Assert.False(graph.HasSearchMessage);
    }

    [Fact]
    public void FindReportsWhenNothingMatches()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SearchText = "zebra";

        graph.FindCommand.Execute(null);

        Assert.Null(graph.SelectedNode);
        Assert.Contains("zebra", graph.SearchMessage, StringComparison.Ordinal);
        Assert.False(graph.IsSuggestionsOpen);
    }

    [Fact]
    public void ChoosingASuggestionSelectsItAndClosesTheList()
    {
        DependencyGraphViewModel graph = Graph();
        List<DependencyGraphNode> centred = [];
        graph.CenterOnRequested += (_, node) => centred.Add(node);
        graph.SearchText = "cust";
        DependencyGraphNode address = graph.Suggestions.Single(node => node.Label == "CustomerAddress");

        graph.ChooseSuggestionCommand.Execute(address);

        Assert.Same(address, graph.SelectedNode);
        Assert.Equal([address], centred);
        Assert.Equal("CustomerAddress", graph.SearchText);
        Assert.False(graph.IsSuggestionsOpen);
    }

    [Fact]
    public void ClearingTheTextClosesTheListAndRefreshListsEverything()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SearchText = "cust";
        graph.SearchText = string.Empty;

        Assert.False(graph.IsSuggestionsOpen);
        Assert.Empty(graph.Suggestions);

        graph.RefreshSuggestionsCommand.Execute(null);
        Assert.Equal(graph.Model.Nodes.Count, graph.Suggestions.Count);
    }

    [Fact]
    public void TheListShowsAtMostFiftySuggestions()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Enumerable.Range(1, 80).Select(id => Row(id, $"entity_{id:000}")).ToArray());

        graph.SearchText = "entity";

        Assert.Equal(DependencyGraphViewModel.MaxSuggestions, graph.Suggestions.Count);
    }

    [Fact]
    public void ARebuildDropsStaleSuggestions()
    {
        DependencyGraphViewModel graph = Graph();
        graph.SearchText = "cust";
        DependencyGraphNode stale = graph.Suggestions[0];

        graph.Rebuild(Rows);
        Assert.Empty(graph.Suggestions);
        graph.ChooseSuggestionCommand.Execute(stale);

        Assert.Contains(graph.SelectedNode!, graph.Model.Nodes);
        Assert.Equal(stale.Key, graph.SelectedNode!.Key);
    }

    private static DependencyGraphViewModel Graph()
    {
        DependencyGraphViewModel graph = new(_ => true);
        graph.Rebuild(Rows);
        return graph;
    }

    private static EntityOverviewRow Row(int id, string name, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", name, "", "", "CSV", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}
