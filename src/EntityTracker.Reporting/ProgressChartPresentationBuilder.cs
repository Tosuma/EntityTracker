using System.Globalization;

using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using SkiaSharp;

namespace EntityTracker.Reporting;

public sealed class ProgressChartPresentationBuilder
{
    /// <summary>Builds the charts the app shows, with axes, legends and tooltips readable on the theme.</summary>
    public ProgressChartPresentation Build(
        ProgressDashboardReport report,
        ProgressChartTheme theme = ProgressChartTheme.Light) =>
        Build(report, includeExportPieLabels: false, theme);

    /// <summary>Builds the charts for a saved or copied image: always light, whatever the app's theme.</summary>
    public ProgressChartPresentation BuildForExport(ProgressDashboardReport report) =>
        Build(report, includeExportPieLabels: true, ProgressChartTheme.Light);

    private static ProgressChartPresentation Build(
        ProgressDashboardReport report,
        bool includeExportPieLabels,
        ProgressChartTheme theme)
    {
        ArgumentNullException.ThrowIfNull(report);

        ISeries[] currentStatusSeries = CreateCurrentStatusSeries(
            report.CurrentStatusCounts,
            includeExportPieLabels,
            theme);

        DateOnly[] implementedDates = report.ImplementedOverTime
            .Select(static point => point.Date)
            .ToArray();
        Axis[] implementedXAxes = CreateDateAxes(implementedDates, theme);
        Axis[] readinessXAxes = CreateDateAxes(report.ReadyAndBlockedOverTime
            .Select(static point => point.Date)
            .ToArray(), theme);
        Axis[] weeklyXAxes = CreateDateAxes(report.WeeklyNetImplementedChange
            .Select(static point => point.WeekStartingMonday)
            .ToArray(), theme);

        ISeries[] implementedSeries =
        [
            new LineSeries<int>
            {
                Name = "Implemented",
                Values = report.ImplementedOverTime
                    .Select(static point => point.ImplementedCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(ProgressChartPalette.Implemented, 3),
                Fill = new SolidColorPaint(ProgressChartPalette.Implemented.WithAlpha(35)),
                GeometrySize = 5
            },
            // The other statuses as plain lines, in the pie's colours for the same statuses.
            CreateStatusLine("In progress", ProgressStatusCategory.InProgress,
                report.ImplementedOverTime.Select(static point => point.InProgressCount), theme),
            CreateStatusLine("Not started", ProgressStatusCategory.NotStarted,
                report.ImplementedOverTime.Select(static point => point.NotStartedCount), theme),
            CreateStatusLine("Blocked", ProgressStatusCategory.Blocked,
                report.ImplementedOverTime.Select(static point => point.BlockedCount), theme),
            // The target: today's number of active entities, flat across the chart. Dashed, so it
            // stays apart from the orange Blocked line.
            new LineSeries<int>
            {
                Name = "Active entities",
                Values = report.ImplementedOverTime
                    .Select(_ => report.ManagerSummary.ActiveEntityCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(ProgressChartPalette.Target, 2)
                {
                    PathEffect = new DashEffect([6, 4])
                },
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0
            }
        ];
        ISeries[] readinessSeries =
        [
            new LineSeries<int>
            {
                Name = "Ready to start",
                Values = report.ReadyAndBlockedOverTime
                    .Select(static point => point.ReadyCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(ProgressChartPalette.ReadyTrend, 3),
                Fill = null,
                GeometrySize = 5
            },
            new LineSeries<int>
            {
                Name = "Waiting on dependencies",
                Values = report.ReadyAndBlockedOverTime
                    .Select(static point => point.BlockedCount)
                    .ToArray(),
                Stroke = new SolidColorPaint(ProgressChartPalette.WaitingTrend, 3),
                Fill = null,
                GeometrySize = 5
            }
        ];

        // Two bars a week, side by side and both upward: entities that became implemented, and
        // entities that stopped being implemented.
        ISeries[] weeklySeries =
        [
            new ColumnSeries<int>
            {
                Name = "Increase",
                Values = report.WeeklyNetImplementedChange.Select(static week => week.Increase).ToArray(),
                Fill = new SolidColorPaint(ProgressChartPalette.Increase)
            },
            new ColumnSeries<int>
            {
                Name = "Decrease",
                Values = report.WeeklyNetImplementedChange.Select(static week => week.Decrease).ToArray(),
                Fill = new SolidColorPaint(ProgressChartPalette.Decrease)
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
            [new Axis
            {
                MinLimit = 0,
                MinStep = 1,
                LabelsPaint = CreateTextPaint(theme),
                SeparatorsPaint = CreateGridPaint(theme)
            }],
            [new Axis
            {
                MinLimit = 0,
                MinStep = 1,
                LabelsPaint = CreateTextPaint(theme),
                SeparatorsPaint = CreateGridPaint(theme)
            }],
            CreateTextPaint(theme),
            CreateTextPaint(theme),
            new SolidColorPaint(ProgressChartPalette.TooltipBackgroundFor(theme)));
    }

    private static LineSeries<int> CreateStatusLine(
        string name,
        ProgressStatusCategory status,
        IEnumerable<int> values,
        ProgressChartTheme theme) => new()
    {
        Name = name,
        Values = values.ToArray(),
        Stroke = new SolidColorPaint(ProgressChartPalette.From(ProgressChartPalette.Category(status, theme)), 2),
        Fill = null,
        GeometrySize = 3,
        GeometryStroke = new SolidColorPaint(ProgressChartPalette.From(ProgressChartPalette.Category(status, theme)), 2)
    };

    public static string GetTitle(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.CurrentStatus => "Entities by development status",
        ProgressChartKind.ImplementedOverTime => "Progress over time",
        ProgressChartKind.ReadyAndBlockedOverTime => "Ready to start vs waiting on dependencies over time",
        ProgressChartKind.WeeklyNetImplementedChange => "Weekly implemented change",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>A short description of the chart, shown when pointing at its title.</summary>
    public static string GetDescription(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.CurrentStatus => "How many active entities have each development status now.",
        ProgressChartKind.ImplementedOverTime =>
            "Implemented, in progress, not started and blocked entities each day, against today's active total.",
        ProgressChartKind.ReadyAndBlockedOverTime =>
            "Each day, the entities ready to start and the ones still waiting on a dependency.",
        ProgressChartKind.WeeklyNetImplementedChange =>
            "Each week, how many entities became implemented and how many stopped being implemented.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string GetFileNameSegment(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.CurrentStatus => "current-work-status",
        ProgressChartKind.ImplementedOverTime => "progress-over-time",
        ProgressChartKind.ReadyAndBlockedOverTime => "ready-vs-blocked",
        ProgressChartKind.WeeklyNetImplementedChange => "weekly-implemented-change",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static ISeries[] CreateCurrentStatusSeries(
        IReadOnlyList<ProgressStatusCount> counts,
        bool includeExportPieLabels,
        ProgressChartTheme theme)
    {
        int total = counts.Sum(static item => item.Count);
        return counts.Select(item =>
        {
            // Standard chart colours, not the status colours, and no borders between the slices.
            string fill = ProgressChartPalette.Category(item.Status, theme);
            PieSeries<int> series = new()
            {
                Name = FormatStatus(item.Status),
                Values = [item.Count],
                Fill = new SolidColorPaint(ProgressChartPalette.From(fill)),
                Stroke = null
            };
            if (includeExportPieLabels && item.Count > 0 && total > 0)
            {
                string label = FormatCountAndPercentage(item.Count, total);
                series.ShowDataLabels = true;
                series.DataLabelsPosition = PolarLabelsPosition.Middle;
                series.DataLabelsSize = 22;
                series.DataLabelsPaint = new SolidColorPaint(ProgressChartPalette.LabelOn(fill));
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

    private static Axis[] CreateDateAxes(DateOnly[] dates, ProgressChartTheme theme)
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
                LabelsPaint = CreateTextPaint(theme),
                SeparatorsPaint = CreateGridPaint(theme),
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

    private static SolidColorPaint CreateTextPaint(ProgressChartTheme theme) => new(ProgressChartPalette.TextFor(theme));

    private static SolidColorPaint CreateGridPaint(ProgressChartTheme theme) => new(ProgressChartPalette.GridFor(theme), 1);

    private static string FormatStatus(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => "Not started",
        ProgressStatusCategory.InProgress => "In progress",
        ProgressStatusCategory.ReworkNeeded => "Rework needed",
        ProgressStatusCategory.DevelopmentCompleted => "Completed",
        ProgressStatusCategory.Reconciled => "Reconciled",
        ProgressStatusCategory.Blocked => "Blocked",
        ProgressStatusCategory.Reworking => "Reworking",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
