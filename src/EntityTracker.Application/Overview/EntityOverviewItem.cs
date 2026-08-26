using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewItem
{
    internal EntityOverviewItem(
        EntityId entityId,
        int? rank,
        int? effectivePriority,
        string sourceName,
        EntityProvenance provenance,
        DevelopmentStatus status,
        string notes,
        string responsibleDeveloper,
        EntityLifecycleState lifecycleState,
        int dependencyCount,
        IEnumerable<string> dependencyNames,
        DependencyResolutionState? dependencyState,
        IEnumerable<string> dependencyResolutionIssueNames,
        EntityWorkflowState workflowState,
        IEnumerable<DependencyBlocker> blockers)
    {
        EntityId = entityId;
        Rank = rank;
        EffectivePriority = effectivePriority;
        SourceName = sourceName;
        Provenance = provenance;
        Status = status;
        Notes = notes;
        ResponsibleDeveloper = responsibleDeveloper;
        LifecycleState = lifecycleState;
        DependencyCount = dependencyCount;
        DependencyNames = Array.AsReadOnly(dependencyNames.ToArray());
        DependencyState = dependencyState;
        DependencyResolutionIssueNames = Array.AsReadOnly(
            dependencyResolutionIssueNames.ToArray());
        WorkflowState = workflowState;
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    public EntityId EntityId { get; }

    public int? Rank { get; }

    public int? EffectivePriority { get; }

    public string SourceName { get; }

    public EntityProvenance Provenance { get; }

    public DevelopmentStatus Status { get; }

    public string Notes { get; }

    public string ResponsibleDeveloper { get; }

    public EntityLifecycleState LifecycleState { get; }

    public int DependencyCount { get; }

    public IReadOnlyList<string> DependencyNames { get; }

    public DependencyResolutionState? DependencyState { get; }

    /// <summary>
    /// Gets the unresolved source names that prevent this entity from receiving a
    /// dependency-safe rank, either directly or through its dependency chain.
    /// </summary>
    public IReadOnlyList<string> DependencyResolutionIssueNames { get; }

    public EntityWorkflowState WorkflowState { get; }

    public IReadOnlyList<DependencyBlocker> Blockers { get; }

    public IReadOnlyList<string> MissingDependencyNames =>
        Blockers.Select(static blocker => blocker.SourceName).ToArray();
}
