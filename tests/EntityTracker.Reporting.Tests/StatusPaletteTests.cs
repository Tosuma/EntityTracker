using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using EntityTracker.Reporting.ProjectReports;

using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

namespace EntityTracker.Reporting.Tests;

/// <summary>
/// Reports and PNG charts colour statuses exactly like the app, so the two cannot drift apart. The
/// app's palette (EntityTrackerPalette.xaml) is the source of truth.
/// </summary>
public sealed partial class StatusPaletteTests
{
    private static readonly IReadOnlyDictionary<string, string> AppStatusColors = LoadAppStatusColors();

    [Fact]
    public void ReportStatusColorsMatchTheApp()
    {
        Assert.All(Enum.GetValues<ProgressStatusCategory>(), status =>
            Assert.Equal(AppStatusColors[status.ToString()], ReportLabels.StatusColor(status), ignoreCase: true));
    }

    [Fact]
    public void ChartStatusColorsMatchTheApp()
    {
        ProgressStatusCategory[] statuses = Enum.GetValues<ProgressStatusCategory>();
        ProgressDashboardReport report = new(
            new ProgressManagerSummary(statuses.Length, 0, 0, 0, 0, new DateOnly(2026, 10, 10)),
            statuses.Select(static status => new ProgressStatusCount(status, 1)).ToArray(),
            [], [], [], null, null);

        ProgressChartPresentation presentation = new ProgressChartPresentationBuilder().Build(report);

        Assert.Equal(statuses.Length, presentation.CurrentStatusSeries.Length);
        for (int index = 0; index < statuses.Length; index++)
        {
            SKColor fill = Assert.IsType<SolidColorPaint>(
                Assert.IsType<PieSeries<int>>(presentation.CurrentStatusSeries[index]).Fill).Color;
            Assert.Equal(AppStatusColors[statuses[index].ToString()], Hex(fill), ignoreCase: true);
        }
    }

    [Theory]
    [InlineData("Not started", "NotStarted")]
    [InlineData("In progress", "InProgress")]
    [InlineData("Rework needed", "ReworkNeeded")]
    [InlineData("Reworking", "Reworking")]
    [InlineData("Blocked", "Blocked")]
    [InlineData("Dev. completed", "DevelopmentCompleted")]
    [InlineData("Completed", "DevelopmentCompleted")]
    [InlineData("Reconciled", "Reconciled")]
    [InlineData("Ready", "Ready")]
    [InlineData("Waiting on dependencies", "WaitingOnDependencies")]
    public void ExportedReportScriptColorsMatchTheApp(string label, string status)
    {
        using Stream stream = typeof(ReportLabels).Assembly.GetManifestResourceStream(
            "EntityTracker.Reporting.ProjectReports.Assets.report.js")!;
        string script = new StreamReader(stream).ReadToEnd();

        Match match = Regex.Match(script, $$"""
            "{{Regex.Escape(label)}}": \["(#[0-9A-Fa-f]{6})"
            """);

        Assert.True(match.Success, $"report.js has no colour for {label}.");
        Assert.Equal(AppStatusColors[status], match.Groups[1].Value, ignoreCase: true);
    }

    private static string Hex(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>Resolves every Brush.Status.* in the app palette to its hex colour, keyed by status name.</summary>
    private static Dictionary<string, string> LoadAppStatusColors()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(PalettePath(directory.FullName))) directory = directory.Parent;
        XDocument palette = XDocument.Load(PalettePath(directory!.FullName));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Dictionary<string, string> colors = palette.Descendants()
            .Where(static element => element.Name.LocalName == "Color")
            .ToDictionary(element => (string)element.Attribute(x + "Key")!, static element => element.Value.Trim());
        return palette.Descendants()
            .Where(element => element.Name.LocalName == "SolidColorBrush" &&
                              ((string?)element.Attribute(x + "Key"))?.StartsWith("Brush.Status.", StringComparison.Ordinal) == true)
            .ToDictionary(
                element => ((string)element.Attribute(x + "Key")!)["Brush.Status.".Length..],
                element => colors[ResourceKey().Match((string)element.Attribute("Color")!).Groups[1].Value]);
    }

    private static string PalettePath(string root) =>
        Path.Combine(root, "src", "EntityTracker.Wpf", "Themes", "EntityTrackerPalette.xaml");

    [GeneratedRegex(@"\{StaticResource ([^}]+)\}")]
    private static partial Regex ResourceKey();
}
