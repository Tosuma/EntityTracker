using EntityTracker.Reporting.ProjectReports;

namespace EntityTracker.Reporting;

/// <summary>
/// The lines of the "Progress over time" chart, to choose which a saved or copied image, or a Project
/// report, shows.
/// </summary>
[Flags]
public enum ProgressOverTimeLines
{
    None = 0,
    Implemented = 1,
    InProgress = 2,
    NotStarted = 4,
    Blocked = 8,
    ActiveEntities = 16,

    /// <summary>The lines a client report shows: what is implemented, against the active entities.</summary>
    Client = Implemented | ActiveEntities,

    All = Implemented | InProgress | NotStarted | Blocked | ActiveEntities
}

/// <summary>Names and order of the "Progress over time" lines, as the chart's legend shows them.</summary>
public static class ProgressOverTimeLineNames
{
    /// <summary>Each line, in the chart's order, with the name its series has.</summary>
    public static IReadOnlyList<(ProgressOverTimeLines Line, string Name)> InOrder { get; } =
    [
        (ProgressOverTimeLines.Implemented, "Implemented"),
        (ProgressOverTimeLines.InProgress, "In progress"),
        (ProgressOverTimeLines.NotStarted, "Not started"),
        (ProgressOverTimeLines.Blocked, "Blocked"),
        (ProgressOverTimeLines.ActiveEntities, "Active entities")
    ];

    /// <summary>
    /// Whether <paramref name="lines"/> keeps the series named <paramref name="seriesName"/>. A series that
    /// isn't one of the lines, such as another chart's, is always kept.
    /// </summary>
    public static bool Keeps(ProgressOverTimeLines lines, string? seriesName)
    {
        foreach ((ProgressOverTimeLines line, string name) in InOrder)
            if (name == seriesName) return lines.HasFlag(line);
        return true;
    }

    /// <summary>The lines a report for <paramref name="audience"/> starts with: two for a client, all for the team.</summary>
    public static ProgressOverTimeLines DefaultFor(ReportAudience audience) =>
        audience == ReportAudience.Client ? ProgressOverTimeLines.Client : ProgressOverTimeLines.All;
}
