namespace EntityTracker.Domain;

/// <summary>One uninterrupted period of responsibility for an entity.</summary>
public sealed record ResponsibilityPeriod
{
    public ResponsibilityPeriod(Guid id, EntityId entityId, DeveloperId developerId,
        DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A period ID is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(developerId);
        if (startedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The start must be UTC.", nameof(startedAtUtc));
        if (endedAtUtc is { } end && (end.Offset != TimeSpan.Zero || end < startedAtUtc))
            throw new ArgumentException("The end must be UTC and no earlier than the start.", nameof(endedAtUtc));
        Id = id;
        EntityId = entityId;
        DeveloperId = developerId;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
    }

    public Guid Id { get; }
    public EntityId EntityId { get; }
    public DeveloperId DeveloperId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? EndedAtUtc { get; }
    public bool IsCurrent => EndedAtUtc is null;

    public ResponsibilityPeriod End(DateTimeOffset endedAtUtc) =>
        IsCurrent ? new(Id, EntityId, DeveloperId, StartedAtUtc, endedAtUtc) : this;
}
