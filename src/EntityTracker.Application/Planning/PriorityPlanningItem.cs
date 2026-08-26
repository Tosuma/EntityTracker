using EntityTracker.Domain;

namespace EntityTracker.Application.Planning;

public sealed record PriorityPlanningItem(
    EntityId EntityId,
    string SourceName,
    bool IsTarget,
    int? RequestedPriority,
    int? EffectivePriority);
