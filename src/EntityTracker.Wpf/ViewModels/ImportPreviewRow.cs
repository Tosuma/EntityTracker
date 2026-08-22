namespace EntityTracker.Wpf.ViewModels;

public sealed record ImportPreviewRow(
    string Rank,
    string SourceName,
    int MandatoryDependencyCount,
    int OptionalDependencyCount,
    int DependencyCount,
    string DependencyState,
    string MissingDependencies);
