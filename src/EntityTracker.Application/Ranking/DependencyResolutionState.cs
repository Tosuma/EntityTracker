namespace EntityTracker.Application.Ranking;

/// <summary>
/// Describes whether an entity can participate in dependency-safe ranking.
/// This is distinct from development status and from future workflow readiness rules.
/// </summary>
public enum DependencyResolutionState
{
    /// <summary>All declared dependency references are known.</summary>
    Resolved,

    /// <summary>The entity has at least one directly unresolved dependency reference.</summary>
    Unresolved,

    /// <summary>The entity transitively depends on an unresolved entity.</summary>
    Blocked
}
