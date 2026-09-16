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
        "overview.png",
        "overview-search.png",
        "overview-missing-entities-as-dependencies.png",
        "schema-synchronization.png",
        "schema-synchronization-import-csv-with-missing-entities.png",
        "schema-synchronization-unresolved-dependencies.png",
        "add-entity.png",
        "edit-entity.png",
        "progress.png",
        "archived-entity.png",
        "sql-query.png",
        "connections.png"
    ];

    internal static string GetAppearanceDirectoryName(ApplicationAppearance appearance) =>
        appearance switch
        {
            ApplicationAppearance.Dark => "dark",
            ApplicationAppearance.Light => "light",
            _ => throw new ArgumentOutOfRangeException(nameof(appearance))
        };
}
