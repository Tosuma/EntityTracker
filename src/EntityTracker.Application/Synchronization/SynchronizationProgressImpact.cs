using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public sealed record SynchronizationProgressImpact(
    EntityId EntityId,
    string SourceName,
    DevelopmentStatus CurrentStatus,
    SynchronizationProgressDecision? Decision);
