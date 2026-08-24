using System.Globalization;

using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

namespace EntityTracker.Reporting;

public sealed class ProgressChartPresentationBuilder
{
    private static readonly SKColor TextColor = new(82, 97, 106);

    public ProgressChartPresentation Build(ProgressDashboardReport report) =>
        Build(report, includeExportPieLabels: false);

    public ProgressChartPresentation BuildForExport(ProgressDashboardReport report) =>
        Build(report, includeExportPieLabels: true);

    private static ProgressChartPresentation Build(
        ProgressDashboardReport report,
        bool includeExportPieLabels)
    {
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<ProgressStatusCategory, SKColor> statusColors = new()
        {
            [ProgressStatusCategory.NotStarted] = new SKColor(148, 163, 184),
            [ProgressStatusCategory.InProgress] = new SKColor(23, 107, 135),
            [ProgressStatusCategory.ReworkNeeded] = new SKColor(217, 119, 6),
            [ProgressStatusCategory.DevelopmentCompleted] = new SKColor(37, 99, 235),
            [ProgressStatusCategory.Reconciled] = new SKColor(46, 139, 87)
        };

        ISeries[] currentStatusSeries = CreateCurrentStatusSeries(
            report.CurrentStatusCounts,
            statusColors,
            includeExportPieLabels);

        DateOnly[] implementedDates = report.ImplementedOverTime
            .Select(static point => point.Date)
            .ToArray();
        Axis[] implementedXAxes = CreateDateAxes(implementedDates);
        Axis[] readinessXAxes = CreateDateAxes(report.ReadyAndBlockedOverTime
            .Select(static point => point.Date)
            .ToArray());
        Axis[] weeklyXAxes = CreateDateAxes(report.WeeklyNetImplementedChange
            .Select(static point => point.WeekStartingMonday)
            .ToArray());

        ISeries[] implementedSeries =
        [
            new LineSeries<int>
            {
                Name = "Implemented",
                Values = report.ImplementedOverTime
                    .Select(static point => point.ImplementedCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(new SKColor(23, 107, 135), 3),
                Fill = new SolidColorPaint(new SKColor(23, 107, 135, 35)),
                GeometrySize = 5
            }
        ];
        ISeries[] readinessSeries =
        [
            new LineSeries<int>
            {
                Name = "Ready",
                Values = report.ReadyAndBlockedOverTime
                    .Select(static point => point.ReadyCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(new SKColor(46, 139, 87), 3),
                Fill = null,
                GeometrySize = 5
            },
            new LineSeries<int>
            {
                Name = "Blocked",
                Values = report.ReadyAndBlockedOverTime
                    .Select(static point => point.BlockedCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(new SKColor(198, 40, 40), 3),
                Fill = null,
                GeometrySize = 5
            }
        ];

        int?[] positive = report.WeeklyNetImplementedChange
            .Select(static point => point.NetChange >= 0 ? point.NetChange : (int?)null)
            .ToArray();
        int?[] negative = report.WeeklyNetImplementedChange
            .Select(static point => point.NetChange < 0 ? point.NetChange : (int?)null)
            .ToArray();
        ISeries[] weeklySeries =
        [
            new ColumnSeries<int?>
            {
                Name = "Increase",
                Values = positive,
                Fill = new SolidColorPaint(new SKColor(46, 139, 87))
            },
            new ColumnSeries<int?>
            {
                Name = "Decrease",
                Values = negative,
                Fill = new SolidColorPaint(new SKColor(198, 40, 40))
            }
        ];

        return new ProgressChartPresentation(
            currentStatusSeries,
            implementedSeries,
            readinessSeries,
            weeklySeries,
            implementedXAxes,
            readinessXAxes,
            weeklyXAxes,
            [new Axis { MinLimit = 0, MinStep = 1, LabelsPaint = CreateTextPaint() }],
            [new Axis { MinStep = 1, LabelsPaint = CreateTextPaint() }]);
    }

    public static string GetTitle(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.CurrentStatus => "Entities by current work status",
        ProgressChartKind.ImplementedOverTime => "Implemented entities over time",
        ProgressChartKind.ReadyAndBlockedOverTime => "Ready vs blocked over time",
        ProgressChartKind.WeeklyNetImplementedChange => "Weekly net implemented change",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string GetFileNameSegment(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.CurrentStatus => "current-work-status",
        ProgressChartKind.ImplementedOverTime => "implemented-over-time",
        ProgressChartKind.ReadyAndBlockedOverTime => "ready-vs-blocked",
        ProgressChartKind.WeeklyNetImplementedChange => "weekly-implemented-change",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static ISeries[] CreateCurrentStatusSeries(
        IReadOnlyList<ProgressStatusCount> counts,
        IReadOnlyDictionary<ProgressStatusCategory, SKColor> statusColors,
        bool includeExportPieLabels)
    {
        int total = counts.Sum(static item => item.Count);
        return counts.Select(item =>
        {
            PieSeries<int> series = new()
            {
                Name = FormatStatus(item.Status),
                Values = [item.Count],
                Fill = new SolidColorPaint(statusColors[item.Status])
            };
            if (includeExportPieLabels && item.Count > 0 && total > 0)
            {
                string label = FormatCountAndPercentage(item.Count, total);
                series.ShowDataLabels = true;
                series.DataLabelsPosition = PolarLabelsPosition.Middle;
                series.DataLabelsSize = 22;
                series.DataLabelsPaint = new SolidColorPaint(GetPieLabelColor(item.Status));
                series.DataLabelsFormatter = _ => label;
            }

            return (ISeries)series;
        }).ToArray();
    }

    private static string FormatCountAndPercentage(int count, int total)
    {
        double percentage = count * 100d / total;
        return $"{count} ({percentage.ToString("0.#", CultureInfo.CurrentCulture)}%)";
    }

    private static SKColor GetPieLabelColor(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => new SKColor(38, 52, 61),
        ProgressStatusCategory.ReworkNeeded => new SKColor(38, 52, 61),
        ProgressStatusCategory.InProgress => SKColors.White,
        ProgressStatusCategory.DevelopmentCompleted => SKColors.White,
        ProgressStatusCategory.Reconciled => SKColors.White,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static Axis[] CreateDateAxes(DateOnly[] dates)
    {
        string format = dates.Select(static date => date.Year).Distinct().Take(2).Count() > 1
            ? "dd MMM yy"
            : "dd MMM";
        return
        [
            new Axis
            {
                Labeler = value => FormatDateLabel(value, dates, format),
                LabelsDensity = 1.25f,
                LabelsRotation = 0,
                LabelsPaint = CreateTextPaint(),
                MinStep = 1
            }
        ];
    }

    private static string FormatDateLabel(
        double value,
        IReadOnlyList<DateOnly> dates,
        string format)
    {
        int index = (int)Math.Round(value);
        return Math.Abs(value - index) < 0.001 && index >= 0 && index < dates.Count
            ? dates[index].ToString(format)
            : string.Empty;
    }

    private static SolidColorPaint CreateTextPaint() => new(TextColor);

    private static string FormatStatus(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => "Not started",
        ProgressStatusCategory.InProgress => "In progress",
        ProgressStatusCategory.ReworkNeeded => "Rework needed",
        ProgressStatusCategory.DevelopmentCompleted => "Dev. completed",
        ProgressStatusCategory.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
