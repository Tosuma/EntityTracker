namespace EntityTracker.Domain;

/// <summary>Records how an entity first entered its Tracker.</summary>
public enum EntityProvenance
{
    /// <summary>The entity was added by hand.</summary>
    Manual,

    /// <summary>The entity was copied, with its structure, from another Tracker.</summary>
    Copied
}
