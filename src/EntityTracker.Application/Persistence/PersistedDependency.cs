using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>A dependency whose name resolved to an active entity: the edge the ranking uses.</summary>
public sealed record PersistedDependency
{
    public PersistedDependency(DependencyEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        Edge = edge;
    }

    public DependencyEdge Edge { get; }
}
