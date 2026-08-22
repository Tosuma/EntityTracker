namespace EntityTracker.Domain;

/// <summary>
/// Records how an entity first entered the tracker and whether CSV has since confirmed it.
/// ManualAndImported follows the same synchronization rules as Imported.
/// </summary>
public enum EntityProvenance
{
    /// <summary>The entity was first created from a CSV import.</summary>
    Imported,

    /// <summary>The entity was created manually and has never appeared in a CSV import.</summary>
    ManualOnly,

    /// <summary>The entity was created manually and later matched by a CSV import.</summary>
    ManualAndImported
}
