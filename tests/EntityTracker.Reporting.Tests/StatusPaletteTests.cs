using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using EntityTracker.Reporting.ProjectReports;

using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

namespace EntityTracker.Reporting.Tests;

/// <summary>
/// Reports and charts take each status's colour from StatusColors, whose development colours match the
/// app's palette (EntityTrackerPalette.xaml), and report.js repeats the same fills.
/// </summary>
public sealed partial class StatusPaletteTests
{
    private static readonly IReadOnlyDictionary<string, string> AppStatusColors = LoadAppStatusColors();

    [Fact]
    public void DevelopmentStatusColorsMatchTheApp()
    {
        Assert.All(Enum.GetValues<ProgressStatusCategory>(), status =>
        {
            Assert.Equal(AppStatusColors[status.ToString()], StatusColors.For(status).Fill, ignoreCase: true);
            Assert.Equal(AppStatusColors[$"{status}.Text"], StatusColors.For(status).Text, ignoreCase: true);
            Assert.Equal(StatusColors.For(status).Fill, ReportLabels.StatusColor(status));
        });
    }

    [Fact]
    public void ChartStatusColorsComeFromStatusColors()
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
            Assert.Equal(StatusColors.For(statuses[index]).Fill, Hex(fill), ignoreCase: true);
        }
    }

    public static TheoryData<string, string> ReportLabelsAndColors => new()
    {
        { "Not started", StatusColors.NotStarted.Fill },
        { "In progress", StatusColors.InProgress.Fill },
        { "Rework needed", StatusColors.ReworkNeeded.Fill },
        { "Reworking", StatusColors.Reworking.Fill },
        { "Blocked", StatusColors.Blocked.Fill },
        { "Dev. completed", StatusColors.DevelopmentCompleted.Fill },
        { "Completed", StatusColors.WorkCompleted.Fill },
        { "Reconciled", StatusColors.Reconciled.Fill },
        { "Ready", StatusColors.WorkReady.Fill },
        { "Waiting on dependencies", StatusColors.WorkWaitingOnDependencies.Fill }
    };

    [Theory]
    [MemberData(nameof(ReportLabelsAndColors))]
    public void ExportedReportScriptColorsMatchStatusColors(string label, string fill)
    {
        using Stream stream = typeof(ReportLabels).Assembly.GetManifestResourceStream(
            "EntityTracker.Reporting.ProjectReports.Assets.report.js")!;
        string script = new StreamReader(stream).ReadToEnd();

        Match match = Regex.Match(script, $$"""
            "{{Regex.Escape(label)}}": \["(#[0-9A-Fa-f]{6})"
            """);

        Assert.True(match.Success, $"report.js has no colour for {label}.");
        Assert.Equal(fill, match.Groups[1].Value, ignoreCase: true);
    }

    private static string Hex(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>Resolves every Brush.Status.* in the app palette to its hex colour, keyed by status name (and "Name.Text").</summary>
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
