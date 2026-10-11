using EntityTracker.Theme;

using SkiaSharp;

namespace EntityTracker.Reporting;

/// <summary>The chart colours from <see cref="ColorScheme"/>, as SkiaSharp colours.</summary>
internal static class ProgressChartPalette
{
    internal static readonly SKColor Canvas = From(ColorScheme.Chart.Canvas);
    internal static readonly SKColor Text = From(ColorScheme.Chart.Text);
    internal static readonly SKColor Grid = From(ColorScheme.Chart.Grid);
    internal static readonly SKColor Implemented = From(ColorScheme.Chart.Implemented);
    internal static readonly SKColor ReadyTrend = From(ColorScheme.Chart.ReadyTrend);
    internal static readonly SKColor WaitingTrend = From(ColorScheme.Chart.WaitingTrend);
    internal static readonly SKColor Increase = From(ColorScheme.Chart.Increase);
    internal static readonly SKColor Decrease = From(ColorScheme.Chart.Decrease);

    internal static SKColor From(string hex) => SKColor.Parse(hex);

    /// <summary>Gets a development status's colour (and the colour of a label on it).</summary>
    internal static StatusColor Status(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => ColorScheme.Status.NotStarted,
        ProgressStatusCategory.InProgress => ColorScheme.Status.InProgress,
        ProgressStatusCategory.ReworkNeeded => ColorScheme.Status.ReworkNeeded,
        ProgressStatusCategory.Reworking => ColorScheme.Status.Reworking,
        ProgressStatusCategory.Blocked => ColorScheme.Status.Blocked,
        ProgressStatusCategory.DevelopmentCompleted => ColorScheme.Status.DevelopmentCompleted,
        ProgressStatusCategory.Reconciled => ColorScheme.Status.Reconciled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
