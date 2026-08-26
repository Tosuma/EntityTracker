using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record PriorityPlanningRow(
    EntityId EntityId,
    string SourceName,
    string Role,
    string RequestedPriority,
    string EffectivePriority);
