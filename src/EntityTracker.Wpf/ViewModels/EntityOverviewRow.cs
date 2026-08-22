namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityOverviewRow(
    int Rank,
    string SourceName,
    string Status,
    int DependencyCount,
    string Notes);
