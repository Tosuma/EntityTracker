namespace EntityTracker.Wpf.ViewModels;

public sealed record ImportPreviewRow(
    int Rank,
    string SourceName,
    int MandatoryDependencyCount,
    int OptionalDependencyCount,
    int DependencyCount);
