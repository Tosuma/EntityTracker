using System.Globalization;

using EntityTracker.Application.Overview;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>Headline numbers: active, implemented, reconciled, ready and blocked.</summary>
public sealed class SummarySectionProvider : IReportSectionProvider
{
    public ReportSection Build(ReportContext context) => new SummarySection("summary", "Summary",
        ReportVisibility.Everyone,
        context.Scopes.ToDictionary(scope => scope.Key, scope => Cards(context.ReportFor(scope).ManagerSummary)));

    private static IReadOnlyList<SummaryCard> Cards(ProgressManagerSummary summary) =>
    [
        new("Active entities", summary.ActiveEntityCount),
        new("Implemented", summary.ImplementedEntityCount),
        new("Reconciled", summary.ReconciledEntityCount),
        new("Ready to start", summary.ReadyEntityCount),
        new("Blocked", summary.BlockedEntityCount, Attention: summary.BlockedEntityCount > 0)
    ];
}

/// <summary>One of the progress charts the app shows, for every scope.</summary>
public sealed class ProgressChartSectionProvider(ProgressChartKind kind) : IReportSectionProvider
{
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
        context.Scopes.ToDictionary(scope => scope.Key, scope => Chart(context.ReportFor(scope))));

    private ReportChart Chart(ProgressDashboardReport report) => kind switch
    {
        ProgressChartKind.CurrentStatus => new ReportChart(
            report.CurrentStatusCounts.Select(count => ReportLabels.Status(count.Status)).ToArray(),
            [new ReportSeries("Entities", ReportLabels.Green80,
                report.CurrentStatusCounts.Select(count => (double)count.Count).ToArray(),
                report.CurrentStatusCounts.Select(count => ReportLabels.StatusColor(count.Status)).ToArray())]),
        ProgressChartKind.ImplementedOverTime => new ReportChart(
            report.ImplementedOverTime.Select(point => Date(point.Date)).ToArray(),
            [new ReportSeries("Implemented", ReportLabels.Green100,
                report.ImplementedOverTime.Select(point => (double)point.ImplementedCount).ToArray())]),
        ProgressChartKind.ReadyAndBlockedOverTime => new ReportChart(
            report.ReadyAndBlockedOverTime.Select(point => Date(point.Date)).ToArray(),
            [
                new ReportSeries("Ready to start", ReportLabels.Green80,
                    report.ReadyAndBlockedOverTime.Select(point => (double)point.ReadyCount).ToArray()),
                new ReportSeries("Waiting on dependencies", ReportLabels.Coral,
                    report.ReadyAndBlockedOverTime.Select(point => (double)point.BlockedCount).ToArray())
            ]),
        ProgressChartKind.WeeklyNetImplementedChange => new ReportChart(
            report.WeeklyNetImplementedChange.Select(week => Date(week.WeekStartingMonday)).ToArray(),
            [new ReportSeries("Net implemented change", ReportLabels.Green60,
                report.WeeklyNetImplementedChange.Select(week => (double)week.NetChange).ToArray())]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

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
        new("missing", "Missing dependencies", ReportVisibility.InternalOnly),
        new("origin", "Origin", ReportVisibility.InternalOnly, Filter: true)
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
            ["missing"] = string.Join(", ", item.MissingDependencyNames),
            ["origin"] = item.Provenance.ToString()
        };
}
