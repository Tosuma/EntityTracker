using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public sealed record SynchronizationResolutionEffect(
    EntityId EntityId,
    string SourceName,
    DependencyResolutionState State,
    IReadOnlyList<string> MissingDependencyNames);
