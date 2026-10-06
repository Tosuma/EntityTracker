using System.Text.Json;
using System.Text.RegularExpressions;

using EntityTracker.Domain;
using EntityTracker.Reporting.ProjectReports;

using Jint;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The report's dependency graph data and its camera helpers.</summary>
public sealed class ReportGraphTests
{
    [Fact]
    public void TheGraphIsWrittenAsItsOwnKindOfSectionAndReachesTheClient()
    {
        GraphSection graph = new("dependency-graph", "Dependency graph", ReportVisibility.Everyone,
            new Dictionary<string, ReportGraph>
            {
                [ReportScope.AllKey] = new(
                    [
                        new ReportGraphNode("customer", "Not started", "Ready", "", false, true, 6, 0, 0, 0, 0, ["customer"]),
                        new ReportGraphNode("invoice", "In progress", "In progress", "customer", false, false, 6, 70, 0, 0, 193, ["invoice"])
                    ],
                    [new ReportGraphLink(0, 1, true, [75, 81, 75, 193])],
                    [70])
            });
        ProjectReport report = new("Commerce modernization", ReportAudience.Client, DateTimeOffset.UnixEpoch, null, null,
            [new ReportScope(ReportScope.AllKey, "Core schema")], [graph]);

        Assert.Same(graph, ProjectReportBuilder.ForAudience(graph, ReportAudience.Client));
        string html = ProjectReportHtmlWriter.Write(report);
        string data = Regex.Match(html, "<script id=\"report-data\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Singleline).Groups[1].Value;
        JsonElement section = JsonDocument.Parse(data).RootElement.GetProperty("sections")[0];

        Assert.Equal("graph", section.GetProperty("kind").GetString());
        JsonElement scope = section.GetProperty("byScope").GetProperty(ReportScope.AllKey);
        Assert.Equal("invoice", scope.GetProperty("nodes")[1].GetProperty("name").GetString());
        Assert.Equal(193, scope.GetProperty("nodes")[1].GetProperty("treeY").GetDouble());
        Assert.Equal(4, scope.GetProperty("links")[0].GetProperty("route").GetArrayLength());
        Assert.Contains("EntityTrackerGraph", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrackersOwnScopeIsAllWhenItIsTheOnlyTracker()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Tracker first = new(TrackerId.New(), ProjectId.New(), "Core schema", now, now);
        Tracker second = new(TrackerId.New(), first.ProjectId, "Release readiness", now, now);

        Assert.Equal(ReportScope.AllKey, Context([first]).TrackerScopeKey(first));
        Assert.Equal(ReportContext.ScopeKey(second.Id), Context([first, second]).TrackerScopeKey(second));
    }

    [Fact]
    public void SectionsFromOutsideComeAfterTheChartsAndBeforeTheEntityTable()
    {
        IReportSectionProvider extra = new SummarySectionProvider();

        IReadOnlyList<IReportSectionProvider> providers = ProjectReportBuilder.DefaultProvidersWith(extra);

        Assert.Equal(ProjectReportBuilder.DefaultProviders.Count + 1, providers.Count);
        Assert.Same(extra, providers[^2]);
        Assert.IsType<EntityTableSectionProvider>(providers[^1]);
    }

    [Fact]
    public void FitCentresTheGraphAndKeepsItWithinTheMaximumScale()
    {
        Engine engine = Graph();

        string wide = engine.Evaluate("JSON.stringify(EntityTrackerGraph.fit({ x: 0, y: 0, width: 2000, height: 500 }, 1000, 600, 20, 2))").AsString();
        string tiny = engine.Evaluate("JSON.stringify(EntityTrackerGraph.fit({ x: 10, y: 10, width: 20, height: 20 }, 1000, 600, 20, 2))").AsString();

        Assert.Equal("""{"scale":0.48,"x":20,"y":180}""", wide);
        Assert.Equal("""{"scale":2,"x":460,"y":260}""", tiny);
    }

    [Fact]
    public void ZoomingKeepsThePointUnderThePointerStillAndStaysWithinLimits()
    {
        Engine engine = Graph();

        string zoomed = engine.Evaluate("JSON.stringify(EntityTrackerGraph.zoomAt({ scale: 1, x: 100, y: 50 }, 2, 300, 250, 0.1, 4))").AsString();
        string limited = engine.Evaluate("EntityTrackerGraph.zoomAt({ scale: 3, x: 0, y: 0 }, 10, 0, 0, 0.1, 4).scale").ToString();
        string centred = engine.Evaluate("JSON.stringify(EntityTrackerGraph.centreOn(50, 20, 800, 600, 2))").AsString();

        // The graph point under (300, 250) was (200, 200) and still is: 200 * 2 - 100 = 300.
        Assert.Equal("""{"scale":2,"x":-100,"y":-150}""", zoomed);
        Assert.Equal("4", limited);
        Assert.Equal("""{"scale":2,"x":300,"y":260}""", centred);
    }

    private static Engine Graph()
    {
        Engine engine = new();
        engine.Execute(ProjectReportHtmlWriter.Asset("report-graph.js"));
        return engine;
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
            trackers.ToDictionary(tracker => tracker.Id, _ => (IReadOnlyList<Application.Overview.EntityOverviewItem>)[]));
    }
}
