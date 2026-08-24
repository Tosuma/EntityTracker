using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace EntityTracker.Reporting;

public sealed record ProgressChartPresentation(
    ISeries[] CurrentStatusSeries,
    ISeries[] ImplementedSeries,
    ISeries[] ReadinessSeries,
    ISeries[] WeeklySeries,
    Axis[] ImplementedXAxes,
    Axis[] ReadinessXAxes,
    Axis[] WeeklyXAxes,
    Axis[] CountYAxes,
    Axis[] SignedYAxes);
