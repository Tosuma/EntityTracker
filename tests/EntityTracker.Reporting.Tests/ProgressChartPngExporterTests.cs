using SkiaSharp;

namespace EntityTracker.Reporting.Tests;

public sealed class ProgressChartPngExporterTests
{
    private readonly ProgressChartPngExporter _exporter =
        new(new ProgressChartPresentationBuilder());

    [Theory]
    [InlineData(ProgressChartKind.CurrentStatus)]
    [InlineData(ProgressChartKind.ImplementedOverTime)]
    [InlineData(ProgressChartKind.ReadyAndBlockedOverTime)]
    [InlineData(ProgressChartKind.WeeklyNetImplementedChange)]
    public void RenderPng_ProducesReadableReportSizedImageForEveryChart(ProgressChartKind kind)
    {
        byte[] png = _exporter.RenderPng(CreateReport(), kind);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        Assert.Equal(ProgressChartPngExporter.ImageWidth, bitmap.Width);
        Assert.Equal(ProgressChartPngExporter.ImageHeight, bitmap.Height);
        Assert.Contains(bitmap.Pixels, static pixel => pixel != SKColors.White);
    }

    [Fact]
    public async Task SavePngAsync_WritesRenderedPng()
    {
        string path = Path.Combine(Path.GetTempPath(), $"entitytracker-{Guid.NewGuid():N}.png");
        try
        {
            await _exporter.SavePngAsync(
                CreateReport(),
                ProgressChartKind.ImplementedOverTime,
                path);

            byte[] png = await File.ReadAllBytesAsync(path);
            using SKBitmap bitmap = SKBitmap.Decode(png);
            Assert.NotNull(bitmap);
            Assert.Equal(ProgressChartPngExporter.ImageWidth, bitmap.Width);
            Assert.Equal(ProgressChartPngExporter.ImageHeight, bitmap.Height);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RenderPng_RejectsReportWithoutHistoricalData()
    {
        ProgressDashboardReport report = new(
            ProgressManagerSummary.Empty,
            [],
            [],
            [],
            [],
            null,
            null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            _exporter.RenderPng(report, ProgressChartKind.CurrentStatus));

        Assert.Contains("no progress history", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProgressDashboardReport CreateReport()
    {
        DateOnly first = new(2026, 7, 1);
        DateOnly last = new(2026, 8, 20);
        ImplementedProgressPoint[] implemented = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(index => new ImplementedProgressPoint(first.AddDays(index), 8 + index / 3))
            .ToArray();
        ReadinessProgressPoint[] readiness = implemented
            .Select((point, index) => new ReadinessProgressPoint(point.Date, 35 - index / 4, 12 + index / 5))
            .ToArray();
        WeeklyImplementedChange[] weekly = implemented
            .Where((_, index) => index % 7 == 0)
            .Select((point, index) => new WeeklyImplementedChange(point.Date, index % 3 == 2 ? -2 : 4))
            .ToArray();

        return new ProgressDashboardReport(
            new ProgressManagerSummary(125, 64, 30, 6, 25, last),
            [
                new ProgressStatusCount(ProgressStatusCategory.NotStarted, 30),
                new ProgressStatusCount(ProgressStatusCategory.InProgress, 31),
                new ProgressStatusCount(ProgressStatusCategory.ReworkNeeded, 13),
                new ProgressStatusCount(ProgressStatusCategory.DevelopmentCompleted, 21),
                new ProgressStatusCount(ProgressStatusCategory.Reconciled, 30)
            ],
            implemented,
            readiness,
            weekly,
            first,
            last);
    }
}
