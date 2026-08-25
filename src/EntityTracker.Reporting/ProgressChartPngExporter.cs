using System.Globalization;

using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView.SKCharts;

using SkiaSharp;

namespace EntityTracker.Reporting;

public sealed class ProgressChartPngExporter
{
    public const int ImageWidth = 1600;
    public const int ImageHeight = 900;

    private const int OuterMargin = 48;
    private const int ChartTop = 108;
    private readonly ProgressChartPresentationBuilder _presentationBuilder;

    public ProgressChartPngExporter(ProgressChartPresentationBuilder presentationBuilder)
    {
        ArgumentNullException.ThrowIfNull(presentationBuilder);
        _presentationBuilder = presentationBuilder;
    }

    public byte[] RenderPng(ProgressDashboardReport report, ProgressChartKind kind)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!report.HasHistoricalData)
        {
            throw new InvalidOperationException(
                "A chart cannot be exported when the selected range has no progress history.");
        }

        ProgressChartPresentation presentation = _presentationBuilder.BuildForExport(report);
        InMemorySkiaSharpChart chart = CreateChart(presentation, kind);
        using MemoryStream chartStream = new();
        chart.SaveImage(chartStream, SKEncodedImageFormat.Png, 100);
        chartStream.Position = 0;

        using SKData chartData = SKData.Create(chartStream);
        using SKImage chartImage = SKImage.FromEncodedData(chartData) ??
            throw new InvalidOperationException("The chart renderer did not produce a PNG image.");
        using SKSurface surface = SKSurface.Create(new SKImageInfo(ImageWidth, ImageHeight)) ??
            throw new InvalidOperationException("The PNG drawing surface could not be created.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(ProgressChartPalette.White);

        using SKTypeface titleTypeface = SKTypeface.FromFamilyName(
            "Segoe UI",
            SKFontStyle.Bold);
        using SKTypeface subtitleTypeface = SKTypeface.FromFamilyName(
            "Segoe UI",
            SKFontStyle.Normal);
        using SKPaint titlePaint = new()
        {
            IsAntialias = true,
            Color = ProgressChartPalette.DarkGreen,
            TextSize = 32,
            Typeface = titleTypeface
        };
        using SKPaint subtitlePaint = new()
        {
            IsAntialias = true,
            Color = ProgressChartPalette.DarkGreen.WithAlpha(184),
            TextSize = 19,
            Typeface = subtitleTypeface
        };

        canvas.DrawText(
            ProgressChartPresentationBuilder.GetTitle(kind),
            OuterMargin,
            45,
            titlePaint);
        canvas.DrawText(BuildSubtitle(report, kind), OuterMargin, 78, subtitlePaint);
        canvas.DrawImage(chartImage, OuterMargin, ChartTop);

        using SKImage output = surface.Snapshot();
        using SKData encoded = output.Encode(SKEncodedImageFormat.Png, 100) ??
            throw new InvalidOperationException("The rendered chart could not be encoded as PNG.");
        return encoded.ToArray();
    }

    public async Task SavePngAsync(
        ProgressDashboardReport report,
        ProgressChartKind kind,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] png = await Task.Run(() => RenderPng(report, kind), cancellationToken);
        await File.WriteAllBytesAsync(path, png, cancellationToken);
    }

    private static InMemorySkiaSharpChart CreateChart(
        ProgressChartPresentation presentation,
        ProgressChartKind kind)
    {
        int width = ImageWidth - OuterMargin * 2;
        int height = ImageHeight - ChartTop - OuterMargin;
        return kind switch
        {
            ProgressChartKind.CurrentStatus => new SKPieChart
            {
                Width = width,
                Height = height,
                Series = presentation.CurrentStatusSeries,
                LegendPosition = LegendPosition.Right
            },
            ProgressChartKind.ImplementedOverTime => new SKCartesianChart
            {
                Width = width,
                Height = height,
                Series = presentation.ImplementedSeries,
                XAxes = presentation.ImplementedXAxes,
                YAxes = presentation.CountYAxes,
                LegendPosition = LegendPosition.Top
            },
            ProgressChartKind.ReadyAndBlockedOverTime => new SKCartesianChart
            {
                Width = width,
                Height = height,
                Series = presentation.ReadinessSeries,
                XAxes = presentation.ReadinessXAxes,
                YAxes = presentation.CountYAxes,
                LegendPosition = LegendPosition.Top
            },
            ProgressChartKind.WeeklyNetImplementedChange => new SKCartesianChart
            {
                Width = width,
                Height = height,
                Series = presentation.WeeklySeries,
                XAxes = presentation.WeeklyXAxes,
                YAxes = presentation.SignedYAxes,
                LegendPosition = LegendPosition.Top
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string BuildSubtitle(
        ProgressDashboardReport report,
        ProgressChartKind kind)
    {
        string asOf = report.ManagerSummary.DataAsOfDate is { } date
            ? $"Data as of {date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}"
            : "Data date unavailable";
        if (kind == ProgressChartKind.CurrentStatus)
        {
            return asOf;
        }

        string range = report.EffectiveFrom is { } from && report.EffectiveTo is { } to
            ? $"History shown {FormatRange(from, to)}"
            : "No history in the selected range";
        return $"{range}  ·  {asOf}";
    }

    private static string FormatRange(DateOnly from, DateOnly to) => from.Year == to.Year
        ? $"{from.ToString("dd MMM", CultureInfo.CurrentCulture)}–" +
          to.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
        : $"{from.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}–" +
          to.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
}
