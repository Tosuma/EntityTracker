namespace EntityTracker.Domain;

public sealed record EntityStatusHistoryEntry
{
    public EntityStatusHistoryEntry(
        EntityId entityId,
        DevelopmentStatus? previousStatus,
        DevelopmentStatus newStatus,
        DateTimeOffset occurredAtUtc,
        StatusHistoryEntryKind kind,
        Guid eventId = default,
        Guid? previousEventId = null)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (previousStatus is { } oldStatus && !Enum.IsDefined(oldStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(previousStatus));
        }

        if (!Enum.IsDefined(newStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(newStatus));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Status history timestamps must be UTC.", nameof(occurredAtUtc));
        }

        if (kind == StatusHistoryEntryKind.Transition && previousStatus is null)
        {
            throw new ArgumentException(
                "A status transition requires a previous status.",
                nameof(previousStatus));
        }

        if (kind != StatusHistoryEntryKind.Transition && previousStatus is not null)
        {
            throw new ArgumentException(
                "Baseline and creation entries do not have a previous status.",
                nameof(previousStatus));
        }

        EntityId = entityId;
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        if (previousEventId == Guid.Empty || previousEventId == EventId)
        {
            throw new ArgumentException("The causal predecessor must be a different nonempty event ID.", nameof(previousEventId));
        }
        PreviousEventId = previousEventId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        OccurredAtUtc = occurredAtUtc;
        Kind = kind;
    }

    public EntityId EntityId { get; }
    public Guid EventId { get; }
    public Guid? PreviousEventId { get; }

    public DevelopmentStatus? PreviousStatus { get; }

    public DevelopmentStatus NewStatus { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public StatusHistoryEntryKind Kind { get; }
}
