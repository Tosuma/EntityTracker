using EntityTracker.Domain;
using EntityTracker.Reporting.ProjectReports;
using EntityTracker.Theme;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The progress charts in the exported report, built from the same data as the app's.</summary>
public sealed class ReportProgressChartTests
{
    private static readonly DateOnly Monday = new(2026, 8, 3);

    [Fact]
    public void TheImplementedChartHasAFlatTargetAtTodaysActiveEntities()
    {
        ReportChart chart = Chart(ProgressChartKind.ImplementedOverTime);

        Assert.Equal(["Implemented", "In progress", "Not started", "Blocked", "Active entities"],
            chart.Series.Select(series => series.Name));
        ReportSeries target = chart.Series[^1];
        Assert.Equal(ColorScheme.Chart.Target, target.Color);
        Assert.Equal([12d, 12d, 12d], target.Values);
        Assert.True(target.Dashed);
        Assert.DoesNotContain(chart.Series.SkipLast(1), series => series.Dashed);
    }

    [Fact]
    public void TheProgressChartDrawsTheOtherStatusesInThePiesColours()
    {
        ReportChart chart = Chart(ProgressChartKind.ImplementedOverTime);

        Assert.Equal([ColorScheme.Chart.Categories[2], ColorScheme.Chart.Categories[0], ColorScheme.Chart.Categories[1]],
            chart.Series.Skip(1).Take(3).Select(series => series.Color));
        Assert.Equal([2d, 3d, 1d], chart.Series[1].Values);
        Assert.Equal([6d, 5d, 4d], chart.Series[2].Values);
        Assert.Equal([1d, 1d, 0d], chart.Series[3].Values);
    }

    [Fact]
    public void TheReportScriptKeepsADashedLineDashed() =>
        Assert.Contains("if (series.dashed)", ProjectReportHtmlWriter.Asset("report.js"), StringComparison.Ordinal);

    [Fact]
    public void TheWeeklyChartHasANetChangeAndAnInProgressBarEachWeek()
    {
        ChartSection section = Section(ProgressChartKind.WeeklyNetImplementedChange);
        ReportChart chart = section.ByScope[ReportScope.AllKey];

        Assert.Equal("Weekly implemented change", section.Title);
        Assert.Equal(ReportChartType.Bars, section.ChartType);
        Assert.Equal(["Net change", "In progress"], chart.Series.Select(series => series.Name));
        Assert.Equal([ColorScheme.Chart.Implemented, ProgressChartPalette.Category(ProgressStatusCategory.InProgress)],
            chart.Series.Select(series => series.Color));
        // 3 up and 2 down, then 1 up and 3 down: a week can go backwards.
        Assert.Equal([1d, -2d], chart.Series[0].Values);
        // In progress on the week's last day with data: Tuesday of the first week, Monday of the second.
        Assert.Equal([3d, 1d], chart.Series[1].Values);
    }

    [Fact]
    public void ANegativeWeekDrawsBelowTheZeroLine()
    {
        string script = ProjectReportHtmlWriter.Asset("report.js");

        // The axis takes in zero and every value below it, and a bar grows down from zero.
        Assert.Contains("var e = extent(chart, true);", script, StringComparison.Ordinal);
        Assert.Contains("return [Math.min(0, min)", script, StringComparison.Ordinal);
        Assert.Contains("y: Math.min(zero, box.y(v))", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChartsExplainTheirTermsInWords()
    {
        IReadOnlyList<ReportNote> implemented = Section(ProgressChartKind.ImplementedOverTime).Notes!;
        IReadOnlyList<ReportNote> statuses = Section(ProgressChartKind.CurrentStatus).Notes!;
        IReadOnlyList<ReportNote> weekly = Section(ProgressChartKind.WeeklyNetImplementedChange).Notes!;

        Assert.Equal(["Implemented", "Active entities"], implemented.Select(note => note.Term));
        Assert.Contains("Rework needed, Reworking, Completed and Reconciled", implemented[0].Text, StringComparison.Ordinal);
        Assert.Contains("projected number of entities to be migrated", implemented[1].Text, StringComparison.Ordinal);

        // One line per status the pie can show, in its order.
        Assert.Equal(
            ["Not started", "In progress", "Rework needed", "Reworking", "Completed", "Reconciled", "Blocked"],
            statuses.Select(note => note.Term));
        Assert.Contains("waiting on a dependency is not blocked",
            statuses.Single(note => note.Term == "Blocked").Text, StringComparison.Ordinal);
        Assert.Contains("can change if the physical data model changes",
            statuses.Single(note => note.Term == "Completed").Text, StringComparison.Ordinal);

        Assert.Equal(["Net change", "In progress"], weekly.Select(note => note.Term));
    }

    [Fact]
    public void ThePieUsesTheChartPaletteRatherThanTheStatusColours()
    {
        ReportChart chart = Chart(ProgressChartKind.CurrentStatus);

        Assert.Equal([ColorScheme.Chart.Categories[0], ColorScheme.Chart.Categories[5]], chart.Series[0].PointColors);
    }

    [Theory]
    [InlineData(ProgressChartKind.CurrentStatus)]
    [InlineData(ProgressChartKind.ImplementedOverTime)]
    [InlineData(ProgressChartKind.ReadyAndBlockedOverTime)]
    public void EveryChartCarriesItsDescriptionForTheTitleHint(ProgressChartKind kind) =>
        Assert.Equal(ProgressChartPresentationBuilder.GetDescription(kind), Section(kind).Description);

    [Fact]
    public void TheWeeklyChartDescribesTheReportsOwnBars() =>
        Assert.Equal(ProgressChartSectionProvider.WeeklyDescription,
            Section(ProgressChartKind.WeeklyNetImplementedChange).Description);

    [Fact]
    public void TheReportScriptShowsTheDescriptionWhenPointingAtTheTitle()
    {
        string script = ProjectReportHtmlWriter.Asset("report.js");

        Assert.Contains("heading.title = section.description", script, StringComparison.Ordinal);
    }

    private static ReportChart Chart(ProgressChartKind kind) => Section(kind).ByScope[ReportScope.AllKey];

    private static ChartSection Section(ProgressChartKind kind)
    {
        DateTimeOffset now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
        Tracker tracker = new(TrackerId.New(), ProjectId.New(), "Core schema", now, now);
        ProgressDashboardReport report = new(
            new ProgressManagerSummary(12, 5, 2, 3, 1, new DateOnly(2026, 8, 12)),
            [
                new ProgressStatusCount(ProgressStatusCategory.NotStarted, 7),
                new ProgressStatusCount(ProgressStatusCategory.DevelopmentCompleted, 5)
            ],
            [new(Monday, 3, 2, 6, 1), new(Monday.AddDays(1), 4, 3, 5, 1), new(Monday.AddDays(7), 5, 1, 4, 0)],
            [],
            [new(Monday, 3, 2), new(Monday.AddDays(7), 1, 3)],
            Monday,
            Monday.AddDays(7));
        ReportContext context = new(
            new ProjectReportRequest(tracker.ProjectId, [tracker.Id], ReportAudience.Client, ProgressDateRange.AllHistory),
            "Commerce modernization",
            [tracker],
            report,
            new Dictionary<TrackerId, ProgressDashboardReport> { [tracker.Id] = report },
            new Dictionary<TrackerId, IReadOnlyList<Application.Overview.EntityOverviewItem>> { [tracker.Id] = [] });
        return Assert.IsType<ChartSection>(new ProgressChartSectionProvider(kind).Build(context));
    }
}
