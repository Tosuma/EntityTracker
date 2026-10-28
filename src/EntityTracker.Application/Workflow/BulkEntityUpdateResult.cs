using EntityTracker.Application.ManualOverrides;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

/// <summary>
/// What a bulk update changes on every selected entity. A null part keeps each entity's current value.
/// </summary>
public sealed record BulkEntityUpdate(
    DevelopmentStatus? Status = null,
    RequestedPriorityChange? Priority = null,
    DueDateChange? DueDate = null,
    BulkResponsibilityChange? Responsibility = null)
{
    public bool HasChanges => Status is not null || Priority is not null || DueDate is not null ||
                              Responsibility is not null;
}

/// <summary>A new requested priority for an entity; a null <see cref="Value"/> clears it.</summary>
public sealed record RequestedPriorityChange(int? Value);

/// <summary>Adds or removes one developer as responsible; other responsible developers stay.</summary>
public sealed record BulkResponsibilityChange(DeveloperId DeveloperId, bool Add);

/// <summary>
/// Reports how many uniquely selected entities changed or already matched the update.
/// </summary>
public sealed record BulkEntityUpdateResult(int ChangedCount, int UnchangedCount)
{
    public int SelectedCount => ChangedCount + UnchangedCount;
}
