using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace EntityTracker.Reporting;

/// <param name="LegendTextPaint">The legend's text, readable on the chart's theme.</param>
/// <param name="TooltipTextPaint">The tooltip's text.</param>
/// <param name="TooltipBackgroundPaint">The tooltip's background.</param>
public sealed record ProgressChartPresentation(
    ISeries[] CurrentStatusSeries,
    ISeries[] ImplementedSeries,
    ISeries[] ReadinessSeries,
    ISeries[] WeeklySeries,
    Axis[] ImplementedXAxes,
    Axis[] ReadinessXAxes,
    Axis[] WeeklyXAxes,
    Axis[] CountYAxes,
    Axis[] SignedYAxes,
    SolidColorPaint? LegendTextPaint = null,
    SolidColorPaint? TooltipTextPaint = null,
    SolidColorPaint? TooltipBackgroundPaint = null);
