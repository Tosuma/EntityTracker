using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewItem
{
    internal EntityOverviewItem(
        EntityId entityId,
        int? rank,
        string sourceName,
        DevelopmentStatus status,
        string notes,
        int dependencyCount,
        DependencyResolutionState dependencyState,
        IEnumerable<string> missingDependencyNames)
    {
        EntityId = entityId;
        Rank = rank;
        SourceName = sourceName;
        Status = status;
        Notes = notes;
        DependencyCount = dependencyCount;
        DependencyState = dependencyState;
        MissingDependencyNames = Array.AsReadOnly(missingDependencyNames.ToArray());
    }

    public EntityId EntityId { get; }

    public int? Rank { get; }

    public string SourceName { get; }

    public DevelopmentStatus Status { get; }

    public string Notes { get; }

    public int DependencyCount { get; }

    public DependencyResolutionState DependencyState { get; }

    public IReadOnlyList<string> MissingDependencyNames { get; }
}
