using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewItem
{
    internal EntityOverviewItem(
        EntityId entityId,
        int? rank,
        string sourceName,
        EntityProvenance provenance,
        DevelopmentStatus status,
        string notes,
        int dependencyCount,
        IEnumerable<string> dependencyNames,
        DependencyResolutionState dependencyState,
        IEnumerable<string> missingDependencyNames)
    {
        EntityId = entityId;
        Rank = rank;
        SourceName = sourceName;
        Provenance = provenance;
        Status = status;
        Notes = notes;
        DependencyCount = dependencyCount;
        DependencyNames = Array.AsReadOnly(dependencyNames.ToArray());
        DependencyState = dependencyState;
        MissingDependencyNames = Array.AsReadOnly(missingDependencyNames.ToArray());
    }

    public EntityId EntityId { get; }

    public int? Rank { get; }

    public string SourceName { get; }

    public EntityProvenance Provenance { get; }

    public DevelopmentStatus Status { get; }

    public string Notes { get; }

    public int DependencyCount { get; }

    public IReadOnlyList<string> DependencyNames { get; }

    public DependencyResolutionState DependencyState { get; }

    public IReadOnlyList<string> MissingDependencyNames { get; }
}
