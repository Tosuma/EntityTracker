namespace EntityTracker.Application.ManualOverrides;

/// <summary>A new due date for an entity; a null <see cref="DueDate"/> clears it.</summary>
public sealed record DueDateChange(DateOnly? DueDate);
