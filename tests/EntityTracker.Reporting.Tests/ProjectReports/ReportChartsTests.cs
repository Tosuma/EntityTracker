using System.Text.Json;
using System.Text.RegularExpressions;

using EntityTracker.Reporting.ProjectReports;

using Jint;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The report's chart hover and filter helpers, run as the browser runs them.</summary>
public sealed class ReportChartsTests
{
    [Theory]
    [InlineData(5, 0, 100, 0, 0)]
    [InlineData(5, 0, 100, 12, 0)]
    [InlineData(5, 0, 100, 13, 1)]
    [InlineData(5, 0, 100, 100, 4)]
    [InlineData(5, 0, 100, -40, 0)]
    [InlineData(5, 0, 100, 400, 4)]
    [InlineData(5, 44, 584, 336, 2)]
    [InlineData(1, 0, 100, 80, 0)]
    public void NearestIndexFindsTheClosestDate(int count, double left, double width, double x, int expected) =>
        Assert.Equal(expected, Number(FormattableString.Invariant($"EntityTrackerCharts.nearestIndex({count}, {left}, {width}, {x})")));

    [Theory]
    [InlineData(4, 0, 100, 0, 0)]
    [InlineData(4, 0, 100, 24.9, 0)]
    [InlineData(4, 0, 100, 25, 1)]
    [InlineData(4, 0, 100, 99.9, 3)]
    [InlineData(4, 0, 100, 150, 3)]
    public void BarIndexFindsTheBarUnderThePointer(int count, double left, double width, double x, int expected) =>
        Assert.Equal(expected, Number(FormattableString.Invariant($"EntityTrackerCharts.barIndex({count}, {left}, {width}, {x})")));

    [Theory]
    [InlineData(-1, 1, 5, 0)]
    [InlineData(-1, -1, 5, 4)]
    [InlineData(2, 1, 5, 3)]
    [InlineData(4, 1, 5, 4)]
    [InlineData(0, -1, 5, 0)]
    [InlineData(0, 1, 0, -1)]
    public void ArrowKeysStepWithinTheChart(int current, int delta, int count, int expected) =>
        Assert.Equal(expected, Number(FormattableString.Invariant($"EntityTrackerCharts.stepIndex({current}, {delta}, {count})")));

    [Fact]
    public void DonutCardsShowCountAndShare()
    {
        Assert.Equal("Rework needed · 14 · 5.6%", Text("EntityTrackerCharts.sliceText('Rework needed', 14, 249)"));
        Assert.Equal("Reconciled · 1 · 50%", Text("EntityTrackerCharts.sliceText('Reconciled', 1, 2)"));
        Assert.Equal("Blocked · 0 · 0%", Text("EntityTrackerCharts.sliceText('Blocked', 0, 0)"));
        Assert.Equal("33.3|66.7|0", Text("EntityTrackerCharts.shares([1, 2, 0]).map(function (p) { return Math.round(p * 10) / 10; }).join('|')"));
        Assert.Equal("0|0", Text("EntityTrackerCharts.shares([0, 0]).join('|')"));
    }

    [Fact]
    public void PointCardsListEverySeriesAtThatDate()
    {
        string lines = Text("""
            EntityTrackerCharts.pointLines('Jun 11, 2026', [
              { name: 'Ready to start', values: [3, 7] },
              { name: 'Waiting on dependencies', values: [12, 9.5] }
            ], 1).join('|')
            """);

        Assert.Equal("Jun 11, 2026|Ready to start 7|Waiting on dependencies 9.5", lines);
    }

    [Fact]
    public void FixedFilterOptionsKeepTheirOrderAndValuesNoRowHas()
    {
        string options = Text("""
            JSON.stringify(EntityTrackerCharts.filterOptions(
              ['Not started', 'Blocked', 'In progress', 'Reworking'],
              [{ status: 'In progress' }, { status: 'Not started' }, { status: 'In progress' }, { status: 'Retired' }],
              'status'))
            """);

        Assert.Equal(
            """[{"value":"Not started","count":1},{"value":"Blocked","count":0},{"value":"In progress","count":2},{"value":"Reworking","count":0},{"value":"Retired","count":1}]""",
            options);
    }

    [Fact]
    public void FiltersWithoutFixedOptionsOfferTheValuesThatOccur()
    {
        string options = Text("""
            JSON.stringify(EntityTrackerCharts.filterOptions(null,
              [{ developers: 'Maya, alex' }, { developers: 'alex' }, { developers: '' }],
              'developers'))
            """);

        Assert.Equal("""[{"value":"alex","count":2},{"value":"Maya","count":1}]""", options);
    }

    [Fact]
    public void TheEntityTableOffersEveryStatusInTheAppsOrder()
    {
        ReportColumn status = EntityTableSectionProvider.Columns.Single(column => column.Key == "status");
        ReportColumn work = EntityTableSectionProvider.Columns.Single(column => column.Key == "work");

        Assert.Equal(["Not started", "Blocked", "In progress", "Rework needed", "Reworking", "Dev. completed", "Reconciled"],
            status.Options);
        Assert.Equal(["Ready", "Waiting on dependencies", "Blocked", "In progress", "Completed", "Reconciled"], work.Options);
    }

    [Fact]
    public void TheFileCarriesEveryStatusEvenWhenNoEntityHasIt()
    {
        ProjectReport report = new("Commerce modernization", ReportAudience.Client,
            new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero), null, null,
            [new ReportScope(ReportScope.AllKey, "Core schema")],
            [
                new TableSection("entities", "Entities", ReportVisibility.Everyone, EntityTableSectionProvider.Columns,
                    [new Dictionary<string, string> { ["entity"] = "customer", ["status"] = "Not started", ["work"] = "Ready" }])
            ]);

        string html = ProjectReportHtmlWriter.Write(report);
        string data = Regex.Match(html, "<script id=\"report-data\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Singleline).Groups[1].Value;
        JsonElement statusColumn = JsonDocument.Parse(data).RootElement.GetProperty("sections")[0].GetProperty("columns")
            .EnumerateArray().Single(column => column.GetProperty("key").GetString() == "status");

        Assert.Contains("Reworking", statusColumn.GetProperty("options").EnumerateArray().Select(option => option.GetString()));
        Assert.Contains("EntityTrackerCharts", html, StringComparison.Ordinal);
    }

    private static Engine Charts()
    {
        Engine engine = new();
        engine.Execute(ProjectReportHtmlWriter.Asset("report-charts.js"));
        return engine;
    }

    private static double Number(string expression) => Charts().Evaluate(expression).AsNumber();

    private static string Text(string expression) => Charts().Evaluate(expression).AsString();
}
