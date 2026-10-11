using System.Globalization;

using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;

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
    public void Build_UsesDocumentedStatusAndTrendPalette()
    {
        ProgressChartPresentation presentation = _builder.Build(CreateReport(
            notStarted: 30,
            inProgress: 31,
            rework: 13,
            completed: 21,
            reconciled: 30));

        SKColor[] expectedStatusColors =
        [
            new(0xA0, 0xAF, 0xAF),
            new(0x41, 0x60, 0x5E),
            new(0xFF, 0x63, 0x59),
            new(0x71, 0x88, 0x86),
            new(0x12, 0x38, 0x36)
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
            new SKColor(0x12, 0x38, 0x36),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ImplementedSeries[0]).Stroke).Color);
        Assert.Equal(
            new SKColor(0x41, 0x60, 0x5E),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ReadinessSeries[0]).Stroke).Color);
        Assert.Equal(
            new SKColor(0xFF, 0x63, 0x59),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<LineSeries<int>>(presentation.ReadinessSeries[1]).Stroke).Color);
        Assert.Equal(
            new SKColor(0x41, 0x60, 0x5E),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<ColumnSeries<int?>>(presentation.WeeklySeries[0]).Fill).Color);
        Assert.Equal(
            new SKColor(0xFF, 0x63, 0x59),
            Assert.IsType<SolidColorPaint>(
                Assert.IsType<ColumnSeries<int?>>(presentation.WeeklySeries[1]).Fill).Color);
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

        SKColor[] expectedLabelColors =
        [
            new(0x14, 0x1E, 0x1E),
            SKColors.White,
            new(0x14, 0x1E, 0x1E),
            new(0x14, 0x1E, 0x1E),
            SKColors.White
        ];
        for (int index = 0; index < expectedLabelColors.Length; index++)
        {
            PieSeries<int> series = Assert.IsType<PieSeries<int>>(
                presentation.CurrentStatusSeries[index]);
            Assert.Equal(
                expectedLabelColors[index],
                Assert.IsType<SolidColorPaint>(series.DataLabelsPaint).Color);
        }
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
