namespace EntityTracker.Domain;

public enum EntityLifecycleState
{
    Active,
    Archived,

    /// <summary>
    /// A name another entity depends on, that no real entity has yet: an unresolved reference. It
    /// becomes Active, keeping its ID, when an entity with its name is added.
    /// </summary>
    Placeholder
}
