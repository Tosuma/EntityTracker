using System.Text.Json;
using System.Text.RegularExpressions;

using EntityTracker.Reporting.ProjectReports;
using EntityTracker.Theme;

using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

namespace EntityTracker.Reporting.Tests.ProjectReports;

/// <summary>The exported report and the charts take every colour from ColorScheme.</summary>
public sealed partial class ReportColorTests
{
    [Fact]
    public void EveryColourVariableInTheStylesComesFromTheScheme()
    {
        string[] used = CssVariable().Matches(ProjectReportHtmlWriter.Asset("report.css"))
            .Select(static match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        string styles = ProjectReportHtmlWriter.Styles();

        Assert.All(used.Except(ScriptVariables), name =>
            Assert.True(ProjectReportHtmlWriter.CssColors.ContainsKey(name), $"--{name} is not in ColorScheme."));
        Assert.All(CssVariable().Matches(styles), match => Assert.Contains(match.Groups[1].Value, ScriptVariables));
        Assert.Contains($"background: {ColorScheme.Report.Page};", styles, StringComparison.Ordinal);
        Assert.Contains($"color: {ColorScheme.Report.Muted};", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptColoursComeFromTheScheme()
    {
        ReportColors colors = ProjectReportHtmlWriter.Colors();

        Assert.Equal([ColorScheme.Status.InProgress.Fill, ColorScheme.Status.InProgress.Text], colors.Status["In progress"]);
        Assert.Equal([ColorScheme.Status.DevelopmentCompleted.Fill, ColorScheme.Status.DevelopmentCompleted.Text],
            colors.Status["Completed"]);
        Assert.Equal([ColorScheme.ReportWorkStatus.Ready.Fill, ColorScheme.ReportWorkStatus.Ready.Text], colors.Work["Ready"]);
        Assert.Equal([ColorScheme.ReportWorkStatus.WaitingOnDependencies.Fill, ColorScheme.ReportWorkStatus.WaitingOnDependencies.Text],
            colors.Work["Waiting on dependencies"]);
        Assert.Equal(ColorScheme.Chart.Decrease, colors.Decrease);
        Assert.Equal(7, colors.Status.Count);
        Assert.Equal(6, colors.Work.Count);
    }

    [Fact]
    public void TheReportCarriesItsColoursForTheScript()
    {
        string html = ProjectReportHtmlWriter.Write(ProjectReportTests.Report());
        string json = Regex.Match(html, "<script id=\"report-colors\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Singleline).Groups[1].Value;

        JsonElement colors = JsonDocument.Parse(json).RootElement;

        Assert.Equal(ColorScheme.Status.Reconciled.Fill,
            colors.GetProperty("status").GetProperty("Reconciled")[0].GetString());
        Assert.Equal(ColorScheme.Chart.Grid, colors.GetProperty("grid").GetString());
        Assert.Contains("document.getElementById(\"report-colors\")", ProjectReportHtmlWriter.Asset("report.js"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChartStatusColoursComeFromTheScheme()
    {
        ProgressStatusCategory[] statuses = Enum.GetValues<ProgressStatusCategory>();
        ProgressDashboardReport report = new(
            new ProgressManagerSummary(statuses.Length, 0, 0, 0, 0, new DateOnly(2026, 10, 11)),
            statuses.Select(static status => new ProgressStatusCount(status, 1)).ToArray(),
            [], [], [], null, null);

        ProgressChartPresentation presentation = new ProgressChartPresentationBuilder().Build(report);

        for (int index = 0; index < statuses.Length; index++)
        {
            SKColor fill = Assert.IsType<SolidColorPaint>(
                Assert.IsType<PieSeries<int>>(presentation.CurrentStatusSeries[index]).Fill).Color;
            Assert.Equal(SKColor.Parse(ReportLabels.StatusColor(statuses[index])), fill);
        }

        Assert.Equal(ColorScheme.Status.Blocked.Fill, ReportLabels.StatusColor(ProgressStatusCategory.Blocked));
        Assert.Equal(ColorScheme.Status.Reworking.Fill, ReportLabels.StatusColor(ProgressStatusCategory.Reworking));
    }

    /// <summary>Variables report.js sets per element, not colours of the scheme.</summary>
    private static readonly string[] ScriptVariables = ["swatch", "swatch-ink"];

    [GeneratedRegex(@"var\(--([a-z0-9-]+)")]
    private static partial Regex CssVariable();
}
