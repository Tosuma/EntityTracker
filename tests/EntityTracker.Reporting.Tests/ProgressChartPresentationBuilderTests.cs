using System.Globalization;

using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;

using EntityTracker.Theme;

using SkiaSharp;

namespace EntityTracker.Reporting.Tests;

public sealed class ProgressChartPresentationBuilderTests
{
    private readonly ProgressChartPresentationBuilder _builder = new();

    [Fact]
    public void Build_KeepsPieLabelsHiddenForInAppPresentation()
    {
        ProgressChartPresentation presentation = _builder.Build(CreateReport(
            notStarted: 30,
            inProgress: 31,
            rework: 13,
            completed: 21,
            reconciled: 30));

        Assert.All(presentation.CurrentStatusSeries, series =>
            Assert.False(Assert.IsType<PieSeries<int>>(series).ShowDataLabels));
    }

    [Fact]
    public void Build_DrawsAxesLegendsAndTooltipsForTheAppThemeButExportsStayLight()
    {
        ProgressDashboardReport report = CreateReport(notStarted: 1, inProgress: 1, rework: 1, completed: 1, reconciled: 1);

        ProgressChartPresentation dark = _builder.Build(report, ProgressChartTheme.Dark);
        ProgressChartPresentation light = _builder.Build(report);
        ProgressChartPresentation export = _builder.BuildForExport(report);

        SKColor darkText = SKColor.Parse(ColorScheme.DarkChart.Text);
        SKColor lightText = SKColor.Parse(ColorScheme.Chart.Text);
        Assert.Equal(darkText, Assert.IsType<SolidColorPaint>(dark.CountYAxes[0].LabelsPaint).Color);
        Assert.Equal(darkText, dark.LegendTextPaint!.Color);
        Assert.Equal(SKColor.Parse(ColorScheme.DarkChart.TooltipBackground), dark.TooltipBackgroundPaint!.Color);
        Assert.Equal(lightText, Assert.IsType<SolidColorPaint>(light.CountYAxes[0].LabelsPaint).Color);
        Assert.Equal(lightText, Assert.IsType<SolidColorPaint>(export.CountYAxes[0].LabelsPaint).Color);

        // No borders between slices; the dark card uses the palette's dark steps.
        Assert.All(dark.CurrentStatusSeries.Concat(light.CurrentStatusSeries).Concat(export.CurrentStatusSeries),
            series => Assert.Null(Assert.IsType<PieSeries<int>>(series).Stroke));
        Assert.Equal(SKColor.Parse(ColorScheme.DarkChart.Categories[0]),
            Assert.IsType<SolidColorPaint>(Assert.IsType<PieSeries<int>>(dark.CurrentStatusSeries[0]).Fill).Color);
        Assert.Equal(SKColor.Parse(ColorScheme.Chart.Categories[0]),
            Assert.IsType<SolidColorPaint>(Assert.IsType<PieSeries<int>>(export.CurrentStatusSeries[0]).Fill).Color);
    }

    [Fact]
    public void Build_UsesDocumentedStatusAndTrendPalette()
    {
        ProgressChartPresentation presentation = _builder.Build(CreateReport(
            notStarted: 30,
            inProgress: 31,
            rework: 13,
            completed: 21,
            reconciled: 30));

        // Standard chart colours by status, not the status colours.
        SKColor[] expectedStatusColors =
        [
            SKColor.Parse(ColorScheme.Chart.Categories[0]),
            SKColor.Parse(ColorScheme.Chart.Categories[2]),
            SKColor.Parse(ColorScheme.Chart.Categories[3]),
            SKColor.Parse(ColorScheme.Chart.Categories[5]),
            SKColor.Parse(ColorScheme.Chart.Categories[6])
        ];
        Assert.Equal(expectedStatusColors.Length, presentation.CurrentStatusSeries.Length);
        for (int index = 0; index < expectedStatusColors.Length; index++)
        {
            PieSeries<int> series = Assert.IsType<PieSeries<int>>(
                presentation.CurrentStatusSeries[index]);
            Assert.Equal(
                expectedStatusColors[index],
                Assert.IsType<SolidColorPaint>(series.Fill).Color);
        }

        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.Implemented),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ImplementedSeries[0]).Stroke).Color);
        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.ReadyTrend),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ReadinessSeries[0]).Stroke).Color);
        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.WaitingTrend),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ReadinessSeries[1]).Stroke).Color);
        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.Increase),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<ColumnSeries<int>>(presentation.WeeklySeries[0]).Fill).Color);
        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.Decrease),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<ColumnSeries<int>>(presentation.WeeklySeries[1]).Fill).Color);
        Assert.Equal(
            SKColor.Parse(ColorScheme.Chart.Target),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ImplementedSeries[1]).Stroke).Color);
    }

    [Fact]
    public void Build_DrawsTodaysActiveEntitiesAsAFlatTargetAndTwoWeeklyBars()
    {
        DateOnly monday = new(2026, 8, 3);
        ProgressDashboardReport report = CreateReport(notStarted: 3, inProgress: 2, rework: 1, completed: 2, reconciled: 2) with
        {
            ImplementedOverTime = [new(monday, 2), new(monday.AddDays(1), 4), new(monday.AddDays(7), 5)],
            WeeklyNetImplementedChange = [new(monday, 3, 1), new(monday.AddDays(7), 1, 0)]
        };

        ProgressChartPresentation presentation = _builder.Build(report);

        LineSeries<int> target = Assert.IsType<LineSeries<int>>(presentation.ImplementedSeries[1]);
        Assert.Equal("Active entities", target.Name);
        Assert.Equal([10, 10, 10], Assert.IsAssignableFrom<IEnumerable<int>>(target.Values));
        ColumnSeries<int> increase = Assert.IsType<ColumnSeries<int>>(presentation.WeeklySeries[0]);
        ColumnSeries<int> decrease = Assert.IsType<ColumnSeries<int>>(presentation.WeeklySeries[1]);
        Assert.Equal(("Increase", "Decrease"), (increase.Name, decrease.Name));
        Assert.Equal([3, 1], Assert.IsAssignableFrom<IEnumerable<int>>(increase.Values));
        Assert.Equal([1, 0], Assert.IsAssignableFrom<IEnumerable<int>>(decrease.Values));
        Assert.Equal(0, presentation.WeeklyYAxes[0].MinLimit);
    }

    [Theory]
    [InlineData(ProgressChartKind.CurrentStatus)]
    [InlineData(ProgressChartKind.ImplementedOverTime)]
    [InlineData(ProgressChartKind.ReadyAndBlockedOverTime)]
    [InlineData(ProgressChartKind.WeeklyNetImplementedChange)]
    public void EveryChartHasATitleAndAShortDescription(ProgressChartKind kind)
    {
        Assert.False(string.IsNullOrWhiteSpace(ProgressChartPresentationBuilder.GetTitle(kind)));
        string description = ProgressChartPresentationBuilder.GetDescription(kind);
        Assert.InRange(description.Length, 20, 100);
        Assert.EndsWith(".", description, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildForExport_FormatsNonZeroPieLabelsWithCountAndPercentage()
    {
        ProgressChartPresentation presentation = _builder.BuildForExport(CreateReport(
            notStarted: 30,
            inProgress: 31,
            rework: 13,
            completed: 21,
            reconciled: 30));

        PieSeries<int> notStarted = Assert.IsType<PieSeries<int>>(
            presentation.CurrentStatusSeries[0]);
        PieSeries<int> rework = Assert.IsType<PieSeries<int>>(
            presentation.CurrentStatusSeries[2]);

        Assert.True(notStarted.ShowDataLabels);
        Assert.Equal(PolarLabelsPosition.Middle, notStarted.DataLabelsPosition);
        Assert.Equal(22, notStarted.DataLabelsSize);
        Assert.NotNull(notStarted.DataLabelsPaint);
        Assert.NotNull(notStarted.DataLabelsFormatter);
        Assert.Equal("30 (24%)", FormatLabel(notStarted));
        Assert.Equal(
            $"13 ({10.4.ToString("0.#", CultureInfo.CurrentCulture)}%)",
            FormatLabel(rework));

        // A label is white on a deep slice and dark on a light one: the blue Not started and the yellow
        // Rework needed.
        Assert.Equal(SKColor.Parse(ColorScheme.Base.White),
            Assert.IsType<SolidColorPaint>(notStarted.DataLabelsPaint).Color);
        Assert.Equal(SKColor.Parse(ColorScheme.Chart.Text),
            Assert.IsType<SolidColorPaint>(rework.DataLabelsPaint).Color);
    }

    [Fact]
    public void BuildForExport_HidesZeroSlicesAndHandlesAllZeroDistribution()
    {
        ProgressChartPresentation presentation = _builder.BuildForExport(CreateReport(
            notStarted: 0,
            inProgress: 0,
            rework: 0,
            completed: 0,
            reconciled: 0));

        Assert.All(presentation.CurrentStatusSeries, series =>
        {
            PieSeries<int> pieSeries = Assert.IsType<PieSeries<int>>(series);
            Assert.False(pieSeries.ShowDataLabels);
        });
    }

    [Fact]
    public void BuildForExport_PreservesFractionalPercentageForSmallSlices()
    {
        ProgressChartPresentation presentation = _builder.BuildForExport(CreateReport(
            notStarted: 1,
            inProgress: 30,
            rework: 13,
            completed: 51,
            reconciled: 30));
        PieSeries<int> smallSlice = Assert.IsType<PieSeries<int>>(
            presentation.CurrentStatusSeries[0]);

        Assert.Equal(
            $"1 ({0.8.ToString("0.#", CultureInfo.CurrentCulture)}%)",
            FormatLabel(smallSlice));
    }

    private static string FormatLabel(PieSeries<int> series)
    {
        ChartPoint<int, DoughnutGeometry, LabelGeometry> point = new(ChartPoint.Empty);
        return series.DataLabelsFormatter!(point);
    }

    private static ProgressDashboardReport CreateReport(
        int notStarted,
        int inProgress,
        int rework,
        int completed,
        int reconciled) =>
        new(
            new ProgressManagerSummary(
                notStarted + inProgress + rework + completed + reconciled,
                rework + completed + reconciled,
                reconciled,
                0,
                0,
                new DateOnly(2026, 8, 20)),
            [
                new ProgressStatusCount(ProgressStatusCategory.NotStarted, notStarted),
                new ProgressStatusCount(ProgressStatusCategory.InProgress, inProgress),
                new ProgressStatusCount(ProgressStatusCategory.ReworkNeeded, rework),
                new ProgressStatusCount(ProgressStatusCategory.DevelopmentCompleted, completed),
                new ProgressStatusCount(ProgressStatusCategory.Reconciled, reconciled)
            ],
            [],
            [],
            [],
            null,
            null);
}
