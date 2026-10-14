using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

/// <summary>
/// What an edited entity should depend on: an existing entity, or a name. A name an entity has is
/// that entity; a name no entity has becomes a placeholder entity when the edit is saved.
/// </summary>
public sealed record DependencyTarget
{
    private DependencyTarget(EntityId? entityId, string? name)
    {
        EntityId = entityId;
        Name = name;
    }

    public EntityId? EntityId { get; }

    public string? Name { get; }

    public static DependencyTarget Entity(EntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return new DependencyTarget(entityId, null);
    }

    public static DependencyTarget Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DependencyTarget(null, name.Trim());
    }
}
