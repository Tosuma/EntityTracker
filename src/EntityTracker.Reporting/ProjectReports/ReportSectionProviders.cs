using System.Globalization;

using EntityTracker.Application.Overview;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Theme;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>Headline numbers: active, implemented, reconciled, ready, waiting on dependencies and blocked.</summary>
public sealed class SummarySectionProvider : IReportSectionProvider
{
    public ReportSection Build(ReportContext context) => new SummarySection("summary", "Summary",
        ReportVisibility.Everyone,
        context.Scopes.ToDictionary(scope => scope.Key, scope => Cards(context.ReportFor(scope).ManagerSummary)),
        Notes);

    /// <summary>Gets what the summary covers, so it isn't read as the progress of the whole migration.</summary>
    public static IReadOnlyList<ReportNote> Notes { get; } =
    [
        new("This report covers the technical implementation only. It does not include workshops, mapping " +
            "meetings or other non-technical work.")
    ];

    private static IReadOnlyList<SummaryCard> Cards(ProgressManagerSummary summary) =>
    [
        new("Active entities", summary.ActiveEntityCount),
        new("Implemented", summary.ImplementedEntityCount, Detail:
            $"{summary.DevelopmentCompletedEntityCount} Completed, {summary.ReconciledEntityCount} Reconciled, " +
            $"{summary.ReworkNeededEntityCount} Rework needed, {summary.ReworkingEntityCount} Reworking"),
        new("Reconciled", summary.ReconciledEntityCount),
        new("Ready to start", summary.ReadyEntityCount),
        new("Waiting on dependencies", summary.WaitingOnDependenciesEntityCount,
            Attention: summary.WaitingOnDependenciesEntityCount > 0),
        new("Blocked", summary.ManuallyBlockedEntityCount, Attention: summary.ManuallyBlockedEntityCount > 0)
    ];
}

/// <summary>
/// One of the progress charts the app shows, for every scope, with the notes a client needs to read
/// it. The weekly chart is the report's own: the net change and the entities in progress, where the
/// app shows increases and decreases.
/// </summary>
public sealed class ProgressChartSectionProvider(ProgressChartKind kind) : IReportSectionProvider
{
    /// <summary>Gets the weekly chart's description in the report, where its bars differ from the app's.</summary>
    public const string WeeklyDescription =
        "Each week, how many more entities are implemented than at the start of the week, and how many are in " +
        "progress at its end.";

    /// <summary>Gets the progress chart's description in a client report, which has only these two lines.</summary>
    public const string ClientProgressDescription =
        "Each day, the implemented entities, against the active entities today.";

    public ReportSection Build(ReportContext context) => new ChartSection(
        ProgressChartPresentationBuilder.GetFileNameSegment(kind),
        ProgressChartPresentationBuilder.GetTitle(kind),
        ReportVisibility.Everyone,
        kind switch
        {
            ProgressChartKind.CurrentStatus => ReportChartType.Donut,
            ProgressChartKind.WeeklyNetImplementedChange => ReportChartType.Bars,
            _ => ReportChartType.Line
        },
        context.Scopes.ToDictionary(scope => scope.Key, scope => Chart(context.ReportFor(scope), context.Request.Audience)),
        kind switch
        {
            ProgressChartKind.WeeklyNetImplementedChange => WeeklyDescription,
            ProgressChartKind.ImplementedOverTime when context.Request.Audience == ReportAudience.Client =>
                ClientProgressDescription,
            _ => ProgressChartPresentationBuilder.GetDescription(kind)
        },
        NotesFor(kind));

    /// <summary>Gets the lines shown under a chart that explain its terms, or null when it needs none.</summary>
    public static IReadOnlyList<ReportNote>? NotesFor(ProgressChartKind kind) => kind switch
    {
        ProgressChartKind.ImplementedOverTime =>
        [
            new("Entities that can be migrated with the tool. This includes entities in Rework needed, Reworking, " +
                "Completed and Reconciled.", "Implemented"),
            new("The dashed line: the projected number of entities to be migrated. It will be updated during the " +
                "project.", "Active entities")
        ],
        ProgressChartKind.CurrentStatus =>
        [
            new("Work on the entity hasn't begun.", "Not started"),
            new("The entity is being developed.", "In progress"),
            new("Development was done, but the entity needs changes.", "Rework needed"),
            new("The needed changes are being made.", "Reworking"),
            new("Development is done. This is not final: it can change if the physical data model changes.",
                "Completed"),
            new("Development is done and the entity has been reconciled.", "Reconciled"),
            new("Work on the entity was started and then stopped. An entity that hasn't started because it is " +
                "waiting on a dependency is not blocked.", "Blocked")
        ],
        ProgressChartKind.WeeklyNetImplementedChange =>
        [
            new("Entities that became implemented that week, minus those that stopped being implemented. It is " +
                "below zero when more stopped than became implemented.", "Net change"),
            new("Entities in progress at the end of the week, or today for the current week.", "In progress")
        ],
        _ => null
    };

    private ReportChart Chart(ProgressDashboardReport report, ReportAudience audience) => kind switch
    {
        ProgressChartKind.CurrentStatus => new ReportChart(
            report.CurrentStatusCounts.Select(count => ReportLabels.Status(count.Status)).ToArray(),
            [new ReportSeries("Entities", ColorScheme.Chart.Entities,
                report.CurrentStatusCounts.Select(count => (double)count.Count).ToArray(),
                report.CurrentStatusCounts.Select(count => ReportLabels.SliceColor(count.Status)).ToArray())]),
        ProgressChartKind.ImplementedOverTime => new ReportChart(
            report.ImplementedOverTime.Select(point => Date(point.Date)).ToArray(),
            [
                new ReportSeries("Implemented", ColorScheme.Chart.Implemented,
                    report.ImplementedOverTime.Select(point => (double)point.ImplementedCount).ToArray()),
                // Too much detail for a client; the team's report has it, as the app does.
                .. audience == ReportAudience.Client ? [] : OtherStatusLines(report),
                // The target: today's number of active entities, flat across the chart, dashed.
                new ReportSeries("Active entities", ColorScheme.Chart.Target,
                    report.ImplementedOverTime.Select(_ => (double)report.ManagerSummary.ActiveEntityCount).ToArray(),
                    Dashed: true)
            ]),
        ProgressChartKind.ReadyAndBlockedOverTime => new ReportChart(
            report.ReadyAndBlockedOverTime.Select(point => Date(point.Date)).ToArray(),
            [
                new ReportSeries("Ready to start", ColorScheme.Chart.ReadyTrend,
                    report.ReadyAndBlockedOverTime.Select(point => (double)point.ReadyCount).ToArray()),
                new ReportSeries("Waiting on dependencies", ColorScheme.Chart.WaitingTrend,
                    report.ReadyAndBlockedOverTime.Select(point => (double)point.BlockedCount).ToArray())
            ]),
        ProgressChartKind.WeeklyNetImplementedChange => new ReportChart(
            report.WeeklyNetImplementedChange.Select(week => Date(week.WeekStartingMonday)).ToArray(),
            [
                new ReportSeries("Net change", ColorScheme.Chart.Implemented,
                    report.WeeklyNetImplementedChange.Select(week => (double)(week.Increase - week.Decrease)).ToArray()),
                // In the colour the progress chart and the pie give In progress.
                new ReportSeries("In progress", ReportLabels.SliceColor(ProgressStatusCategory.InProgress),
                    report.WeeklyNetImplementedChange
                        .Select(week => (double)InProgressAtEndOf(week.WeekStartingMonday, report.ImplementedOverTime))
                        .ToArray())
            ]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The statuses other than implemented, in the pie's colours for the same statuses.</summary>
    private static ReportSeries[] OtherStatusLines(ProgressDashboardReport report) =>
    [
        new("In progress", ReportLabels.SliceColor(ProgressStatusCategory.InProgress),
            report.ImplementedOverTime.Select(point => (double)point.InProgressCount).ToArray()),
        new("Not started", ReportLabels.SliceColor(ProgressStatusCategory.NotStarted),
            report.ImplementedOverTime.Select(point => (double)point.NotStartedCount).ToArray()),
        new("Blocked", ReportLabels.SliceColor(ProgressStatusCategory.Blocked),
            report.ImplementedOverTime.Select(point => (double)point.BlockedCount).ToArray())
    ];

    /// <summary>
    /// The entities in progress on the week's last day with data: its Sunday, or the last day of the
    /// period when the week isn't over.
    /// </summary>
    private static int InProgressAtEndOf(DateOnly monday, IReadOnlyList<ImplementedProgressPoint> days) =>
        days.LastOrDefault(day => day.Date >= monday && day.Date < monday.AddDays(7))?.InProgressCount ?? 0;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Every active entity of the selected Trackers, one row each. Columns marked internal-only, such
/// as internal notes and developer names, never reach a client report.
/// </summary>
public sealed class EntityTableSectionProvider : IReportSectionProvider
{
    public static IReadOnlyList<ReportColumn> Columns { get; } =
    [
        new("tracker", "Tracker", Filter: true, Scope: true),
        new("entity", "Entity", Searchable: true),
        new("group", "Group", Filter: true, Searchable: true),
        new("status", "Development status", Filter: true, Options: ReportLabels.StatusOrder),
        new("work", "Work status", Filter: true, Options: ReportLabels.WorkStatusOrder),
        new("priority", "Priority"),
        new("filterActive", "Filter active", Searchable: true),
        new("sharedNotes", "Shared notes", Searchable: true),
        new("dependencies", "Dependencies", Searchable: true),
        new("waitingOn", "Waiting on"),
        new("internalNotes", "Internal notes", ReportVisibility.InternalOnly, Searchable: true),
        new("developers", "Responsible developers", ReportVisibility.InternalOnly, Searchable: true, Filter: true),
        new("missing", "Missing dependencies", ReportVisibility.InternalOnly)
    ];

    public ReportSection Build(ReportContext context) => new TableSection("entities", "Entities",
        ReportVisibility.Everyone, Columns,
        context.Trackers
            .SelectMany(tracker => context.Entities[tracker.Id].Select(item => Row(tracker, item)))
            .ToArray());

    private static IReadOnlyDictionary<string, string> Row(Tracker tracker, EntityOverviewItem item) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tracker"] = tracker.Name,
            ["entity"] = item.SourceName,
            ["group"] = item.GroupName,
            ["status"] = ReportLabels.Status(item.Status),
            ["work"] = ReportLabels.WorkStatus(item.WorkflowState),
            ["priority"] = item.EffectivePriority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["filterActive"] = item.FilterActive,
            ["dependencies"] = string.Join(", ", item.DependencyNames),
            ["waitingOn"] = string.Join(", ", item.Blockers.Select(static blocker => blocker.SourceName)),
            ["sharedNotes"] = item.SharedNotes,
            ["internalNotes"] = item.Notes,
            ["developers"] = string.Join(", ", item.CurrentDevelopers.Select(static developer => developer.DisplayName)),
            ["missing"] = string.Join(", ", item.MissingDependencyNames)
        };
}
