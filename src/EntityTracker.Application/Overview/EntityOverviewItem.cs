using EntityTracker.Application.Ranking;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewItem
{
    internal EntityOverviewItem(
        EntityId entityId,
        int? rank,
        int? requestedPriority,
        int? effectivePriority,
        string sourceName,
        DevelopmentStatus status,
        string notes,
        string responsibleDeveloper,
        string groupName,
        EntityLifecycleState lifecycleState,
        int dependencyCount,
        IEnumerable<string> dependencyNames,
        DependencyResolutionState? dependencyState,
        IEnumerable<string> dependencyResolutionIssueNames,
        EntityWorkflowState workflowState,
        IEnumerable<DependencyBlocker> blockers,
        EntityAuditTimestamps auditTimestamps,
        IEnumerable<EntityOverviewDeveloper>? currentDevelopers = null,
        string filterActive = "",
        string sharedNotes = "",
        DateOnly? dueDate = null,
        DateOnly? effectiveDueDate = null)
    {
        EntityId = entityId;
        Rank = rank;
        RequestedPriority = requestedPriority;
        EffectivePriority = effectivePriority;
        DueDate = dueDate;
        EffectiveDueDate = effectiveDueDate;
        SourceName = sourceName;
        Status = status;
        Notes = notes;
        FilterActive = filterActive;
        SharedNotes = sharedNotes;
        ResponsibleDeveloper = responsibleDeveloper;
        CurrentDevelopers = Array.AsReadOnly((currentDevelopers ?? []).ToArray());
        GroupName = groupName;
        LifecycleState = lifecycleState;
        DependencyCount = dependencyCount;
        DependencyNames = Array.AsReadOnly(dependencyNames.ToArray());
        DependencyState = dependencyState;
        DependencyResolutionIssueNames = Array.AsReadOnly(
            dependencyResolutionIssueNames.ToArray());
        WorkflowState = workflowState;
        Blockers = Array.AsReadOnly(blockers.ToArray());
        AuditTimestamps = auditTimestamps;
    }

    public EntityId EntityId { get; }

    public int? Rank { get; }

    public int? RequestedPriority { get; }

    public int? EffectivePriority { get; }

    /// <summary>Gets the due date set on the entity itself.</summary>
    public DateOnly? DueDate { get; }

    /// <summary>
    /// Gets the earliest due date of the entity and of everything that depends on it; the overview is
    /// ordered by it before priority.
    /// </summary>
    public DateOnly? EffectiveDueDate { get; }

    public string SourceName { get; }

    public DevelopmentStatus Status { get; }

    public string Notes { get; }

    public string FilterActive { get; }

    /// <summary>Gets the notes shared with the client; <see cref="Notes"/> are internal.</summary>
    public string SharedNotes { get; }

    public string ResponsibleDeveloper { get; }

    public IReadOnlyList<EntityOverviewDeveloper> CurrentDevelopers { get; }

    public string GroupName { get; }

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

    public EntityAuditTimestamps AuditTimestamps { get; }

    public IReadOnlyList<string> MissingDependencyNames =>
        Blockers.Select(static blocker => blocker.SourceName).ToArray();
}
