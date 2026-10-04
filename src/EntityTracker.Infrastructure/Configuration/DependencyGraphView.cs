namespace EntityTracker.Infrastructure.Configuration;

/// <summary>How the dependency graph is drawn.</summary>
public enum DependencyGraphView
{
    /// <summary>Foundations in the centre, each dependency layer on a ring further out.</summary>
    SolarSystem,

    /// <summary>A classical tree running top to bottom: an entity sits below what it depends on.</summary>
    Tree
}
