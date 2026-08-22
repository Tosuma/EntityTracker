using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityOverviewRow(
    EntityId EntityId,
    string Rank,
    string SourceName,
    string Provenance,
    string Status,
    string DependencyState,
    int DependencyCount,
    IReadOnlyList<string> DependencyNames,
    string MissingDependencies,
    string Notes);
