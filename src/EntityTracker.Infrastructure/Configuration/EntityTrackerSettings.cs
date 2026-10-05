namespace EntityTracker.Infrastructure.Configuration;

using EntityTracker.Domain;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 8;
    public static readonly IReadOnlyList<int> AutoSyncIntervals = [1, 5, 15, 30, 60];

    public EntityTrackerSettings(
        ApplicationAppearance appearance = ApplicationAppearance.System,
        ProjectId? lastProjectId = null,
        TrackerId? lastTrackerId = null,
        bool autoSyncEnabled = true,
        int autoSyncIntervalMinutes = 5,
        bool searchResponsibleNames = true,
        IReadOnlyDictionary<ProjectId, DeveloperId>? projectDeveloperChoices = null,
        OverviewExportRows overviewExportRows = OverviewExportRows.ShownEntities,
        OverviewCsvSeparator overviewCsvSeparator = OverviewCsvSeparator.Semicolon,
        bool animateDependencyGraph = true,
        bool showDependencyGraphRings = false,
        DependencyGraphView dependencyGraphView = DependencyGraphView.SolarSystem,
        DependencyHighlightMode solarHighlightMode = DependencyHighlightMode.Dependencies,
        DependencyHighlightMode treeHighlightMode = DependencyHighlightMode.DirectLinks)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }
        if (!AutoSyncIntervals.Contains(autoSyncIntervalMinutes))
            throw new ArgumentOutOfRangeException(nameof(autoSyncIntervalMinutes));
        if (!Enum.IsDefined(overviewExportRows))
            throw new ArgumentOutOfRangeException(nameof(overviewExportRows));
        if (!Enum.IsDefined(overviewCsvSeparator))
            throw new ArgumentOutOfRangeException(nameof(overviewCsvSeparator));
        if (!Enum.IsDefined(dependencyGraphView))
            throw new ArgumentOutOfRangeException(nameof(dependencyGraphView));
        if (!Enum.IsDefined(solarHighlightMode))
            throw new ArgumentOutOfRangeException(nameof(solarHighlightMode));
        if (!Enum.IsDefined(treeHighlightMode))
            throw new ArgumentOutOfRangeException(nameof(treeHighlightMode));

        Appearance = appearance;
        LastProjectId = lastProjectId;
        LastTrackerId = lastTrackerId;
        AutoSyncEnabled = autoSyncEnabled;
        AutoSyncIntervalMinutes = autoSyncIntervalMinutes;
        SearchResponsibleNames = searchResponsibleNames;
        ProjectDeveloperChoices = new Dictionary<ProjectId, DeveloperId>(projectDeveloperChoices ??
            new Dictionary<ProjectId, DeveloperId>());
        OverviewExportRows = overviewExportRows;
        OverviewCsvSeparator = overviewCsvSeparator;
        AnimateDependencyGraph = animateDependencyGraph;
        ShowDependencyGraphRings = showDependencyGraphRings;
        DependencyGraphView = dependencyGraphView;
        SolarHighlightMode = solarHighlightMode;
        TreeHighlightMode = treeHighlightMode;
    }

    public ApplicationAppearance Appearance { get; }

    public ProjectId? LastProjectId { get; }

    public TrackerId? LastTrackerId { get; }
    public bool AutoSyncEnabled { get; }
    public int AutoSyncIntervalMinutes { get; }
    public bool SearchResponsibleNames { get; }
    public IReadOnlyDictionary<ProjectId, DeveloperId> ProjectDeveloperChoices { get; }
    public OverviewExportRows OverviewExportRows { get; }
    public OverviewCsvSeparator OverviewCsvSeparator { get; }

    /// <summary>Gets whether the dependency graph slowly rotates around its centre.</summary>
    public bool AnimateDependencyGraph { get; }

    /// <summary>Gets whether the dependency graph draws its orbit rings.</summary>
    public bool ShowDependencyGraphRings { get; }

    /// <summary>Gets the dependency graph view used last: the solar system or the tree.</summary>
    public DependencyGraphView DependencyGraphView { get; }

    /// <summary>Gets what selecting an entity highlights in the solar system.</summary>
    public DependencyHighlightMode SolarHighlightMode { get; }

    /// <summary>Gets what selecting an entity highlights in the tree.</summary>
    public DependencyHighlightMode TreeHighlightMode { get; }

    /// <summary>Gets what selecting an entity highlights in the given view.</summary>
    public DependencyHighlightMode HighlightModeFor(DependencyGraphView view) =>
        view == DependencyGraphView.Tree ? TreeHighlightMode : SolarHighlightMode;

    public static EntityTrackerSettings Default { get; } = new();
}
