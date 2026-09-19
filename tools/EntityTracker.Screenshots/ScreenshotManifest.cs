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
        "project-comparison.png",
        "tracker-recycle-confirmation.png",
        "tracker-recycle-bin.png",
        "project-dashboard-tracker-restored.png",
        "create-tracker-copy.png",
        "overview.png",
        "overview-details.png",
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
        "sql-query.png",
        "settings.png"
    ];

    internal static string GetAppearanceDirectoryName(ApplicationAppearance appearance) =>
        appearance switch
        {
            ApplicationAppearance.Dark => "dark",
            ApplicationAppearance.Light => "light",
            _ => throw new ArgumentOutOfRangeException(nameof(appearance))
        };
}
