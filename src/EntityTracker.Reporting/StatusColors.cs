using SkiaSharp;

namespace EntityTracker.Reporting;

/// <summary>
/// The colour of each status in reports and charts: one fill and one text colour per status. Change a
/// value here to recolour that status in charts and exported reports; report.js (STATUS_COLORS) has the
/// same fills for the exported page. Development statuses match the app's palette
/// (EntityTrackerPalette.xaml); the report's Ready and Waiting on dependencies are deliberately lighter.
/// </summary>
internal static class StatusColors
{
    internal readonly record struct StatusColor(string Fill, string Text)
    {
        internal SKColor FillColor => SKColor.Parse(Fill);
        internal SKColor TextColor => SKColor.Parse(Text);
    }

    // Development statuses
    internal static readonly StatusColor NotStarted = new("#A0AFAF", "#141E1E"); // Green 40%, Dark Green
    internal static readonly StatusColor InProgress = new("#41605E", "#FFFFFF"); // Green 80%, White
    internal static readonly StatusColor ReworkNeeded = new("#FF6359", "#141E1E"); // Coral, Dark Green
    internal static readonly StatusColor Reworking = new("#41605E", "#FFFFFF"); // Green 80%, White
    internal static readonly StatusColor Blocked = new("#FF6359", "#141E1E"); // Coral, Dark Green
    internal static readonly StatusColor DevelopmentCompleted = new("#718886", "#141E1E"); // Green 60%, Dark Green
    internal static readonly StatusColor Reconciled = new("#123836", "#FFFFFF"); // Green 100%, White

    // Work statuses
    internal static readonly StatusColor WorkReady = new("#D0D7D7", "#141E1E"); // Green 20%, Dark Green
    internal static readonly StatusColor WorkWaitingOnDependencies = new("#F6D9A8", "#141E1E"); // pale amber, Dark Green
    internal static readonly StatusColor WorkBlocked = new("#FF6359", "#141E1E"); // Coral, Dark Green
    internal static readonly StatusColor WorkInProgress = new("#41605E", "#FFFFFF"); // Green 80%, White
    internal static readonly StatusColor WorkCompleted = new("#718886", "#FFFFFF"); // Green 60%, White
    internal static readonly StatusColor WorkReconciled = new("#123836", "#FFFFFF"); // Green 100%, White

    internal static StatusColor For(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => NotStarted,
        ProgressStatusCategory.InProgress => InProgress,
        ProgressStatusCategory.ReworkNeeded => ReworkNeeded,
        ProgressStatusCategory.Reworking => Reworking,
        ProgressStatusCategory.Blocked => Blocked,
        ProgressStatusCategory.DevelopmentCompleted => DevelopmentCompleted,
        ProgressStatusCategory.Reconciled => Reconciled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
