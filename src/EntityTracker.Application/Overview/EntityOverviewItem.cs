using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed record EntityOverviewItem(
    EntityId EntityId,
    int Rank,
    string SourceName,
    DevelopmentStatus Status,
    string Notes,
    int DependencyCount);
