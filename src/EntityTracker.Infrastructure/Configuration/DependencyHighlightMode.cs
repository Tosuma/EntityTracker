namespace EntityTracker.Infrastructure.Configuration;

/// <summary>What selecting an entity in the dependency graph highlights.</summary>
public enum DependencyHighlightMode
{
    /// <summary>Everything the selection depends on, all the way up.</summary>
    Dependencies,

    /// <summary>Every entity that depends on the selection, directly or indirectly.</summary>
    Dependents,

    /// <summary>One step both ways: the selection's own links and the entities at their other ends.</summary>
    DirectLinks
}
