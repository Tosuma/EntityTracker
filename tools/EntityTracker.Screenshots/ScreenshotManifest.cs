using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Screenshots;

internal static class ScreenshotManifest
{
    internal static IReadOnlyList<ApplicationAppearance> Appearances { get; } =
    [
        ApplicationAppearance.Dark,
        ApplicationAppearance.Light
    ];

    internal static IReadOnlyList<string> FileNames { get; } =
    [
        "portfolio.png",
        "project-dashboard.png",
        "project-developers.png",
        "project-developer-retirement.png",
        "project-developers-retired.png",
        "project-git-repository.png",
        "project-sync-progress.png",
        "project-sync-action-needed.png",
        "project-merge-review.png",
        "project-comparison.png",
        "tracker-recycle-confirmation.png",
        "tracker-recycle-bin.png",
        "tracker-permanent-delete-confirmation.png",
        "project-dashboard-tracker-restored.png",
        "create-tracker-copy.png",
        "overview.png",
        "overview-export-menu.png",
        "overview-details.png",
        "dependency-graph.png",
        "dependency-graph-selected.png",
        "dependency-graph-hover.png",
        "dependency-graph-tree.png",
        "dependency-graph-tree-selected.png",
        "dependency-graph-dependents.png",
        "dependency-graph-search.png",
        "dependency-graph-details.png",
        "responsibility-history.png",
        "overview-filter-flyout.png",
        "overview-search.png",
        "overview-missing-entities-as-dependencies.png",
        "schema-synchronization.png",
        "schema-synchronization-changed-entities.png",
        "schema-synchronization-import-csv-with-missing-entities.png",
        "schema-synchronization-unresolved-dependencies.png",
        "add-entity.png",
        "edit-entity.png",
        "edit-entity-dependencies.png",
        "archive-entity-confirmation.png",
        "progress.png",
        "archived-entity.png",
        "archived-details.png",
        "help-and-sql.png",
        "sql-query.png",
        "settings.png",
        "settings-project.png",
        "settings-tracker.png",
        "settings-sync.png",
        "settings-about.png",
        "app-update-required.png"
    ];

    internal static string GetAppearanceDirectoryName(ApplicationAppearance appearance) =>
        appearance switch
        {
            ApplicationAppearance.Dark => "dark",
            ApplicationAppearance.Light => "light",
            _ => throw new ArgumentOutOfRangeException(nameof(appearance))
        };
}
