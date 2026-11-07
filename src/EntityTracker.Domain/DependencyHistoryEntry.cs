namespace EntityTracker.Domain;

/// <summary>What a <see cref="DependencyHistoryEntry"/> records.</summary>
public enum DependencyHistoryChange
{
    /// <summary>The entity started to depend on <see cref="DependencyHistoryEntry.DependencyEntityId"/>.</summary>
    DependencyAdded,

    /// <summary>The entity stopped depending on <see cref="DependencyHistoryEntry.DependencyEntityId"/>.</summary>
    DependencyRemoved,

    /// <summary>The entity's lifecycle state became <see cref="DependencyHistoryEntry.LifecycleState"/>.</summary>
    Lifecycle,

    /// <summary>The entity left its Tracker: an unused placeholder, or a permanently deleted entity.</summary>
    Removed
}

/// <summary>
/// One change to what decides whether an entity waits on its dependencies: a dependency added or removed,
/// an entity becoming active, archived or a placeholder, or an entity leaving its Tracker. Together with the
/// status history it gives the state of any past day. A baseline entry records how things already were when
/// the history began, rather than a change at that moment.
/// </summary>
public sealed record DependencyHistoryEntry
{
    public DependencyHistoryEntry(
        Guid eventId,
        EntityId entityId,
        DependencyHistoryChange change,
        DateTimeOffset occurredAtUtc,
        EntityId? dependencyEntityId = null,
        EntityLifecycleState? lifecycleState = null,
        bool isBaseline = false)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (eventId == Guid.Empty) throw new ArgumentException("A dependency history entry needs an ID.", nameof(eventId));
        if (!Enum.IsDefined(change)) throw new ArgumentOutOfRangeException(nameof(change));
        if (occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Dependency history timestamps must be UTC.", nameof(occurredAtUtc));
        bool isDependency = change is DependencyHistoryChange.DependencyAdded or DependencyHistoryChange.DependencyRemoved;
        if (isDependency != dependencyEntityId is not null)
            throw new ArgumentException("Only a dependency change names a dependency.", nameof(dependencyEntityId));
        if ((change == DependencyHistoryChange.Lifecycle) != lifecycleState is not null)
            throw new ArgumentException("Only a lifecycle change names a lifecycle state.", nameof(lifecycleState));
        if (lifecycleState is { } state && !Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(lifecycleState));
        if (dependencyEntityId == entityId)
            throw new ArgumentException("An entity cannot depend on itself.", nameof(dependencyEntityId));

        EventId = eventId;
        EntityId = entityId;
        Change = change;
        OccurredAtUtc = occurredAtUtc;
        DependencyEntityId = dependencyEntityId;
        LifecycleState = lifecycleState;
        IsBaseline = isBaseline;
    }

    public Guid EventId { get; }

    public EntityId EntityId { get; }

    public DependencyHistoryChange Change { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public EntityId? DependencyEntityId { get; }

    public EntityLifecycleState? LifecycleState { get; }

    public bool IsBaseline { get; }
}
