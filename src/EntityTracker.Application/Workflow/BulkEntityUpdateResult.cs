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
/// Reports how many uniquely selected entities changed or already matched the update, and how to undo it
/// (null when nothing changed).
/// </summary>
public sealed record BulkEntityUpdateResult(int ChangedCount, int UnchangedCount, BulkEntityUndo? Undo = null)
{
    public int SelectedCount => ChangedCount + UnchangedCount;
}

/// <summary>What a value was before an update, and what the update made it.</summary>
public sealed record FieldUndo<T>(T Before, T After);

/// <summary>What one bulk update changed, for each entity it changed.</summary>
public sealed record BulkEntityUndo(IReadOnlyList<BulkEntityUndoItem> Items);

/// <summary>The fields an update changed on one entity; a field it left alone is null.</summary>
public sealed record BulkEntityUndoItem(
    EntityId EntityId,
    string Name,
    FieldUndo<DevelopmentStatus>? Status = null,
    FieldUndo<int?>? Priority = null,
    FieldUndo<DateOnly?>? DueDate = null,
    BulkResponsibilityChange? Responsibility = null);

/// <summary>How many entities an undo restored, and the names of those changed since, which it left alone.</summary>
public sealed record BulkEntityUndoResult(int RestoredCount, IReadOnlyList<string> NotRestored);
