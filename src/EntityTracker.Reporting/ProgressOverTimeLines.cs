namespace EntityTracker.Reporting;

/// <summary>The lines of the "Progress over time" chart, to choose which a saved or copied image shows.</summary>
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
}
