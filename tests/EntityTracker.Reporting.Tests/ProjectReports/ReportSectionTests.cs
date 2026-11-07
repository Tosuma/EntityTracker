using EntityTracker.Domain;
using EntityTracker.Reporting.ProjectReports;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The report's sections: which there are, their scopes and what they explain.</summary>
public sealed class ReportSectionTests
{
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
    public void TheReportLeavesOutTheGraphAndTheReadinessChart()
    {
        // #178: clients found both hard to read; the app still shows them.
        ProgressChartKind[] charts = ProjectReportBuilder.DefaultProviders
            .Select(provider => provider.Build(Context([Tracker()])))
            .OfType<ChartSection>()
            .Select(section => Enum.GetValues<ProgressChartKind>()
                .Single(kind => ProgressChartPresentationBuilder.GetFileNameSegment(kind) == section.Key))
            .ToArray();

        Assert.Equal(
            [ProgressChartKind.CurrentStatus, ProgressChartKind.ImplementedOverTime, ProgressChartKind.WeeklyNetImplementedChange],
            charts);
        Assert.DoesNotContain("graph", ProjectReportHtmlWriter.Asset("report.js"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSummarySaysItCoversTheTechnicalImplementationOnly()
    {
        SummarySection section = Assert.IsType<SummarySection>(new SummarySectionProvider().Build(Context([Tracker()])));

        ReportNote note = Assert.Single(section.Notes!);
        Assert.Contains("technical implementation only", note.Text, StringComparison.Ordinal);
        Assert.Contains("workshops, mapping meetings", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesAreShownUnderTheirFigureAndPrinted()
    {
        string script = ProjectReportHtmlWriter.Asset("report.js");
        string styles = ProjectReportHtmlWriter.Asset("report.css");

        Assert.Contains("[heading, body, notes(section)]", script, StringComparison.Ordinal);
        Assert.Contains("cards, notes(section)]", script, StringComparison.Ordinal);
        Assert.Contains("className: \"section-notes\"", script, StringComparison.Ordinal);
        // Visible, not a hover hint, and not hidden on paper.
        string hiddenInPrint = styles.Split('\n').Single(line => line.Contains("display: none !important", StringComparison.Ordinal));
        Assert.DoesNotContain("section-notes", hiddenInPrint, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"section-notes[^{]*\{[^}]*display:\s*none", styles);
    }

    private static Tracker Tracker()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        return new(TrackerId.New(), ProjectId.New(), "Core schema", now, now);
    }

    [Fact]
    public void TheImplementedHeadlineSaysWhatItCountsOnHover()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Tracker tracker = new(TrackerId.New(), ProjectId.New(), "Core schema", now, now);
        ProgressManagerSummary summary = new(20, 11, 2, 4, 3, null, 0, 5, 3, 1);

        SummarySection section = Assert.IsType<SummarySection>(
            new SummarySectionProvider().Build(Context([tracker], summary)));

        IReadOnlyList<SummaryCard> cards = section.ByScope[ReportScope.AllKey];
        Assert.Equal("5 Completed, 2 Reconciled, 3 Rework needed, 1 Reworking",
            cards.Single(card => card.Label == "Implemented").Detail);
        Assert.All(cards.Where(card => card.Label != "Implemented"), card => Assert.Null(card.Detail));
    }

    private static ReportContext Context(Tracker[] trackers, ProgressManagerSummary? summary = null)
    {
        ProgressDashboardReport empty = new(summary ?? ProgressManagerSummary.Empty, [], [], [], [], null, null);
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
