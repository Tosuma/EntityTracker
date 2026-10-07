using System.Text.Json;

using EntityTracker.Application.Overview;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Reporting;
using EntityTracker.Reporting.ProjectReports;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using Jint;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyGraphReportSectionProviderTests
{
    // country <- customer <- invoice, invoice also names country directly (implied, so not drawn),
    // order uses customer and the missing product_catalog, order_line uses order and invoice.
    private static readonly EntityOverviewRow[] Rows =
    [
        Row(1, "country"),
        Row(2, "customer", "country"),
        Row(3, "invoice", "customer", "country"),
        Row(4, "order", "customer", "product_catalog"),
        Row(5, "order_line", "order", "invoice"),
        Row(6, "currency")
    ];

    [Fact]
    public void TheGraphHoldsEveryEntityItsLinksAndMissingDependencies()
    {
        ReportGraph graph = DependencyGraphReportSectionProvider.Graph(Rows);

        Assert.Equal(["country", "currency", "customer", "invoice", "order", "order_line", "product_catalog"],
            graph.Nodes.Select(node => node.Name).Order(StringComparer.Ordinal));
        ReportGraphNode missing = Assert.Single(graph.Nodes, node => node.Missing);
        Assert.Equal("product_catalog", missing.Name);
        Assert.Equal(string.Empty, missing.Status);
        Assert.Equal(7, graph.Links.Count);
        ReportGraphLink implied = Assert.Single(graph.Links, link => !link.Essential);
        Assert.Equal(("country", "invoice"), (graph.Nodes[implied.From].Name, graph.Nodes[implied.To].Name));
        Assert.Null(implied.Route);
        Assert.All(graph.Links.Where(link => link.Essential), link => Assert.True(link.Route!.Count >= 4));
        // Every box has its own place in the tree.
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(node => (node.TreeX, node.TreeY)).Distinct().Count());
        ReportGraphNode customer = graph.Nodes.Single(node => node.Name == "customer");
        Assert.Equal(("Not started", "Ready"), (customer.Status, customer.Work));
    }

    [Fact]
    public void TheGraphCarriesOnlyWhatAClientMaySee()
    {
        EntityOverviewRow secret = Row(1, "customer") with
        {
            Notes = "Internal: vendor contract expires",
            ResponsibleDeveloper = "Alice Brown",
            Provenance = "Manual"
        };

        string json = JsonSerializer.Serialize(DependencyGraphReportSectionProvider.Graph([secret]));

        Assert.DoesNotContain("vendor contract", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EachTrackerWithEntitiesGetsItsOwnGraph()
    {
        GraphSection section = Assert.IsType<GraphSection>(DependencyGraphReportSectionProvider.Section(
        [
            ("core", Rows),
            ("release", [Row(7, "shipment")]),
            ("empty", [])
        ]));

        Assert.Equal(["core", "release"], section.ByScope.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("shipment", Assert.Single(section.ByScope["release"].Nodes).Name);
        Assert.Equal(ReportVisibility.Everyone, section.Visibility);
    }

    [Fact]
    public void AReportWithoutEntitiesHasNoGraph()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Tracker tracker = new(TrackerId.New(), ProjectId.New(), "Core schema", now, now);

        Assert.Null(new DependencyGraphReportSectionProvider().Build(Context([tracker])));
    }

    [Fact]
    public void TheAppsReportsPutTheGraphAfterTheChartsAndBeforeTheEntityTable()
    {
        IReadOnlyList<IReportSectionProvider> sections = DependencyGraphReportSectionProvider.AppSections;

        Assert.IsType<EntityTableSectionProvider>(sections[^1]);
        Assert.IsType<DependencyGraphReportSectionProvider>(sections[^2]);
        Assert.Equal(ProjectReportBuilder.DefaultProviders.Count + 1, sections.Count);
    }

    public static TheoryData<DependencyHighlightMode, string[]> HighlightCases => new()
    {
        { DependencyHighlightMode.Dependencies, ["order_line"] },
        { DependencyHighlightMode.Dependents, ["country"] },
        { DependencyHighlightMode.DirectLinks, ["customer"] },
        { DependencyHighlightMode.Dependencies, ["invoice", "order"] },
        { DependencyHighlightMode.Dependents, ["product_catalog", "currency"] },
        { DependencyHighlightMode.DirectLinks, ["invoice", "product_catalog"] }
    };

    /// <summary>The report highlights exactly what the app's graph highlights for the same selection.</summary>
    [Theory]
    [MemberData(nameof(HighlightCases))]
    public void TheReportHighlightsLikeTheApp(DependencyHighlightMode mode, string[] selected)
    {
        DependencyGraphViewModel app = new(_ => true);
        app.Rebuild(Rows);
        app.SolarHighlightMode = mode;
        app.SelectedNode = app.Model.Nodes.Single(node => node.Label == selected[0]);
        foreach (string name in selected.Skip(1)) app.ToggleSelection(app.Model.Nodes.Single(node => node.Label == name));

        ReportGraph graph = DependencyGraphReportSectionProvider.Graph(Rows);
        Engine engine = new();
        engine.Execute(ProjectReportHtmlWriter.Asset("report-graph.js"));
        engine.SetValue("links", JsonSerializer.Serialize(graph.Links.Select(link =>
            new { from = link.From, to = link.To, essential = link.Essential })));
        engine.SetValue("selected", JsonSerializer.Serialize(selected.Select(name =>
            graph.Nodes.ToList().FindIndex(node => node.Name == name))));
        engine.SetValue("mode", mode switch
        {
            DependencyHighlightMode.Dependencies => "dependencies",
            DependencyHighlightMode.Dependents => "dependents",
            _ => "direct"
        });
        using JsonDocument report = JsonDocument.Parse(engine.Evaluate(
            "JSON.stringify(EntityTrackerGraph.highlight(JSON.parse(links), JSON.parse(selected), mode))").AsString());

        string[] reportNodes = report.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(index => graph.Nodes[index.GetInt32()].Name).Order(StringComparer.Ordinal).ToArray();
        string[] reportLinks = report.RootElement.GetProperty("links").EnumerateArray()
            .Select(index => graph.Links[index.GetInt32()])
            .Select(link => graph.Nodes[link.From].Name + ">" + graph.Nodes[link.To].Name)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(app.HighlightedNodes.Select(node => node.Label).Order(StringComparer.Ordinal), reportNodes);
        Assert.Equal(app.HighlightedEdges.Select(edge => edge.From.Label + ">" + edge.To.Label).Order(StringComparer.Ordinal),
            reportLinks);
    }

    private static ReportContext Context(Tracker[] trackers)
    {
        ProgressDashboardReport empty = new(ProgressManagerSummary.Empty, [], [], [], [], null, null);
        return new ReportContext(
            new ProjectReportRequest(trackers[0].ProjectId, trackers.Select(tracker => tracker.Id).ToArray(),
                ReportAudience.Client, ProgressDateRange.AllHistory),
            "Commerce modernization",
            trackers,
            empty,
            trackers.ToDictionary(tracker => tracker.Id, _ => empty),
            trackers.ToDictionary(tracker => tracker.Id, _ => (IReadOnlyList<EntityOverviewItem>)[]));
    }

    private static EntityOverviewRow Row(int id, string name, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", name, "", "", "CSV", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}
