using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record SynchronizationProgressImpactRow(
    EntityId EntityId,
    string SourceName,
    string CurrentStatus,
    SynchronizationProgressDecision? Decision)
{
    public string DecisionText => Decision switch
    {
        SynchronizationProgressDecision.KeepCurrentStatus => "Keep current status",
        SynchronizationProgressDecision.MarkReworkNeeded => "Mark Rework needed",
        null => "Decision required",
        _ => throw new ArgumentOutOfRangeException(nameof(Decision))
    };
}
