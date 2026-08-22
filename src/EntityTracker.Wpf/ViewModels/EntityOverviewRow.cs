namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityOverviewRow(
    string Rank,
    string SourceName,
    string Provenance,
    string Status,
    string DependencyState,
    int DependencyCount,
    string MissingDependencies,
    string Notes);
