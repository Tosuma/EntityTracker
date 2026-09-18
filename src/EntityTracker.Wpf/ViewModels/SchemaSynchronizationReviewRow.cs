using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record SchemaSynchronizationReviewRow(
    EntityId EntityId,
    string SourceName,
    string Details,
    IReadOnlyList<SchemaSynchronizationDependencyChangeRow>? DependencyChanges = null,
    SynchronizationProgressImpactRow? ProgressImpact = null)
{
    public IReadOnlyList<SchemaSynchronizationDependencyChangeRow> DependencyChangeItems { get; } =
        DependencyChanges ?? [];

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public bool HasDependencyChanges => DependencyChangeItems.Count > 0;

    public bool HasProgressImpact => ProgressImpact is not null;
}

public sealed record SchemaSynchronizationDependencyChangeRow(
    DependencySynchronizationChangeKind Kind,
    string Action,
    string DependencySourceName,
    string? Detail)
{
    public string DetailText => string.IsNullOrWhiteSpace(Detail) ? string.Empty : $" · {Detail}";

    public string AccessibleText => string.IsNullOrWhiteSpace(Detail)
        ? $"{Action}: {DependencySourceName}"
        : $"{Action}: {DependencySourceName}. {Detail}";
}

public sealed record SynchronizationResolutionEffectRow(
    EntityId EntityId,
    string SourceName,
    string MissingNames,
    bool IsDirectlyUnresolved)
{
    public string Explanation => IsDirectlyUnresolved
        ? $"Directly missing: {MissingNames}"
        : $"Blocked upstream by missing dependencies: {MissingNames}";
}
