using System.Text.Json;
using System.Text.RegularExpressions;

using EntityTracker.Domain;
using EntityTracker.Reporting.ProjectReports;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The report's dependency graph data.</summary>
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
                        new ReportGraphNode("customer", "Not started", "Ready", "", false, 0, 0),
                        new ReportGraphNode("invoice", "In progress", "In progress", "customer", false, 0, 193)
                    ],
                    [new ReportGraphLink(0, 1, true, [75, 81, 75, 193])])
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
