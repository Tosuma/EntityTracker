using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>Everything a section provider may draw on, loaded once per report.</summary>
public sealed record ReportContext(
    ProjectReportRequest Request,
    string ProjectName,
    IReadOnlyList<Tracker> Trackers,
    ProgressDashboardReport Combined,
    IReadOnlyDictionary<TrackerId, ProgressDashboardReport> PerTracker,
    IReadOnlyDictionary<TrackerId, IReadOnlyList<EntityOverviewItem>> Entities)
{
    /// <summary>Gets the scopes a section holds data for: all selected Trackers, then each one.</summary>
    public IReadOnlyList<ReportScope> Scopes { get; } =
    [
        new(ReportScope.AllKey, Trackers.Count == 1 ? Trackers[0].Name : "All selected Trackers"),
        .. Trackers.Count == 1 ? [] : Trackers.Select(tracker => new ReportScope(ScopeKey(tracker.Id), tracker.Name))
    ];

    public static string ScopeKey(TrackerId trackerId) => trackerId.Value.ToString("N");

    /// <summary>
    /// Gets the scope that is exactly this Tracker: its own scope, or "all" when it is the only
    /// Tracker in the report.
    /// </summary>
    public string TrackerScopeKey(Tracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        return Trackers.Count == 1 ? ReportScope.AllKey : ScopeKey(tracker.Id);
    }

    /// <summary>Gets the progress report for a scope.</summary>
    public ProgressDashboardReport ReportFor(ReportScope scope) => scope.Key == ReportScope.AllKey
        ? Combined
        : PerTracker[Trackers.Single(tracker => ScopeKey(tracker.Id) == scope.Key).Id];
}

/// <summary>Adds one section to every Project report; register one per kind of content.</summary>
public interface IReportSectionProvider
{
    /// <summary>Builds the section, or returns null when there is nothing to show.</summary>
    ReportSection? Build(ReportContext context);
}

/// <summary>
/// Builds a Project report for the selected Trackers. Every registered section provider adds its
/// section, so a new chart appears in the app and in every export without further work. The
/// result is filtered for its audience before anyone renders it: internal-only sections and
/// columns are removed from a client report, not merely hidden.
/// </summary>
public sealed class ProjectReportBuilder
{
    private readonly IProjectRepository _projects;
    private readonly ITrackerRepository _trackers;
    private readonly ProgressReportingService _progress;
    private readonly AggregateProgressReportingService _aggregate;
    private readonly EntityOverviewService _overview;
    private readonly IReadOnlyList<IReportSectionProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public ProjectReportBuilder(
        IProjectRepository projects,
        ITrackerRepository trackers,
        ProgressReportingService progress,
        AggregateProgressReportingService aggregate,
        EntityOverviewService overview,
        IEnumerable<IReportSectionProvider>? providers = null,
        TimeProvider? timeProvider = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _trackers = trackers ?? throw new ArgumentNullException(nameof(trackers));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _aggregate = aggregate ?? throw new ArgumentNullException(nameof(aggregate));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _providers = providers?.ToArray() ?? DefaultProviders;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the sections every report has, in order.</summary>
    public static IReadOnlyList<IReportSectionProvider> DefaultProviders { get; } =
    [
        new SummarySectionProvider(),
        new ProgressChartSectionProvider(ProgressChartKind.CurrentStatus),
        new ProgressChartSectionProvider(ProgressChartKind.ImplementedOverTime),
        new ProgressChartSectionProvider(ProgressChartKind.ReadyAndBlockedOverTime),
        new ProgressChartSectionProvider(ProgressChartKind.WeeklyNetImplementedChange),
        new EntityTableSectionProvider()
    ];

    /// <summary>
    /// Gets the default sections with more added after the charts and before the entity table, for
    /// sections built outside this library, such as the app's dependency graph.
    /// </summary>
    public static IReadOnlyList<IReportSectionProvider> DefaultProvidersWith(params IReportSectionProvider[] extra) =>
    [
        .. DefaultProviders.Where(static provider => provider is not EntityTableSectionProvider),
        .. extra,
        .. DefaultProviders.OfType<EntityTableSectionProvider>()
    ];

    public async Task<ProjectReport> BuildAsync(ProjectReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Project project = await _projects.GetAsync(request.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("The Project no longer exists.");
        Tracker[] trackers = await SelectedTrackersAsync(request, cancellationToken);

        Dictionary<TrackerId, ProgressDashboardReport> perTracker = [];
        Dictionary<TrackerId, IReadOnlyList<EntityOverviewItem>> entities = [];
        foreach (Tracker tracker in trackers)
        {
            perTracker[tracker.Id] = await _progress.GetReportAsync(tracker.Id, request.Range, cancellationToken);
            EntityOverviewResult overview = await _overview.GetAsync(tracker.Id, cancellationToken);
            entities[tracker.Id] = overview.Items;
        }

        ProgressDashboardReport combined = trackers.Length == 1
            ? perTracker[trackers[0].Id]
            : await _aggregate.GetTrackersReportAsync(trackers, request.Range, cancellationToken);
        ReportContext context = new(request, project.Name, trackers, combined, perTracker, entities);
        ReportSection[] sections = _providers
            .Select(provider => provider.Build(context))
            .OfType<ReportSection>()
            .Select(section => ForAudience(section, request.Audience))
            .OfType<ReportSection>()
            .ToArray();
        return new ProjectReport(project.Name, request.Audience, _timeProvider.GetUtcNow(),
            combined.EffectiveFrom, combined.EffectiveTo, context.Scopes, sections);
    }

    /// <summary>
    /// Gets the progress of the chosen Trackers together, as the report's charts show it for "all
    /// selected Trackers"; used to save or copy those charts as images.
    /// </summary>
    public async Task<ProgressDashboardReport> BuildProgressAsync(ProjectReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Tracker[] trackers = await SelectedTrackersAsync(request, cancellationToken);
        return trackers.Length == 1
            ? await _progress.GetReportAsync(trackers[0].Id, request.Range, cancellationToken)
            : await _aggregate.GetTrackersReportAsync(trackers, request.Range, cancellationToken);
    }

    private async Task<Tracker[]> SelectedTrackersAsync(ProjectReportRequest request, CancellationToken cancellationToken)
    {
        HashSet<TrackerId> selected = request.TrackerIds.ToHashSet();
        Tracker[] trackers = (await _trackers.GetByProjectAsync(request.ProjectId, cancellationToken))
            .Where(tracker => tracker.LifecycleState == CatalogLifecycleState.Active && selected.Contains(tracker.Id))
            .OrderBy(static tracker => tracker.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return trackers.Length > 0
            ? trackers
            : throw new InvalidOperationException("Choose at least one active Tracker for the report.");
    }

    /// <summary>
    /// Removes what the audience may not see. A client report loses internal-only sections, and
    /// internal-only columns are dropped from tables together with their cell values.
    /// </summary>
    public static ReportSection? ForAudience(ReportSection section, ReportAudience audience)
    {
        if (audience == ReportAudience.Internal) return section;
        if (section.Visibility == ReportVisibility.InternalOnly) return null;
        if (section is not TableSection table) return section;

        ReportColumn[] columns = table.Columns.Where(static column => column.Visibility == ReportVisibility.Everyone).ToArray();
        HashSet<string> keep = columns.Select(static column => column.Key).ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string>[] rows = table.Rows
            .Select(row => (IReadOnlyDictionary<string, string>)row
                .Where(cell => keep.Contains(cell.Key))
                .ToDictionary(cell => cell.Key, cell => cell.Value, StringComparer.Ordinal))
            .ToArray();
        return table with { Columns = columns, Rows = rows };
    }
}
