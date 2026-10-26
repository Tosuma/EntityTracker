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
    internal static readonly SKColor Target = From(ColorScheme.Chart.Target);

    /// <summary>Gets the text colour of axes, legends and tooltips on the theme's canvas.</summary>
    internal static SKColor TextFor(ProgressChartTheme theme) =>
        From(theme == ProgressChartTheme.Dark ? ColorScheme.DarkChart.Text : ColorScheme.Chart.Text);

    /// <summary>Gets a development status's pie slice colour on the theme's canvas.</summary>
    internal static string Category(ProgressStatusCategory status, ProgressChartTheme theme = ProgressChartTheme.Light) =>
        (theme == ProgressChartTheme.Dark ? ColorScheme.DarkChart.Categories : ColorScheme.Chart.Categories)[status switch
        {
            ProgressStatusCategory.NotStarted => 0,
            ProgressStatusCategory.Blocked => 1,
            ProgressStatusCategory.InProgress => 2,
            ProgressStatusCategory.ReworkNeeded => 3,
            ProgressStatusCategory.Reworking => 4,
            ProgressStatusCategory.DevelopmentCompleted => 5,
            ProgressStatusCategory.Reconciled => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        }];

    /// <summary>Gets the grid line colour on the theme's canvas.</summary>
    internal static SKColor GridFor(ProgressChartTheme theme) =>
        From(theme == ProgressChartTheme.Dark ? ColorScheme.DarkChart.Grid : ColorScheme.Chart.Grid);

    /// <summary>Gets the tooltip background on the theme's canvas.</summary>
    internal static SKColor TooltipBackgroundFor(ProgressChartTheme theme) =>
        From(theme == ProgressChartTheme.Dark
            ? ColorScheme.DarkChart.TooltipBackground
            : ColorScheme.Chart.TooltipBackground);

    internal static SKColor From(string hex) => SKColor.Parse(hex);

    /// <summary>Gets the label colour that reads best on a slice: white, or the chart's dark text.</summary>
    internal static SKColor LabelOn(string fill)
    {
        SKColor background = From(fill);
        SKColor white = From(ColorScheme.Base.White);
        SKColor dark = From(ColorScheme.Chart.Text);
        return Contrast(white, background) >= Contrast(dark, background) ? white : dark;

        static double Contrast(SKColor a, SKColor b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double Luminance(SKColor color) =>
            0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);

        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
