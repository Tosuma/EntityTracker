using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>A dependency whose name matches no active entity in the Tracker.</summary>
public sealed record PersistedUnresolvedDependency
{
    public PersistedUnresolvedDependency(UnresolvedDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        Dependency = dependency;
    }

    public UnresolvedDependency Dependency { get; }
}
