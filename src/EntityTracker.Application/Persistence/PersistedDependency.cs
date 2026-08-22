using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public sealed record PersistedDependency
{
    public PersistedDependency(DependencyEdge edge, ImportedDependencyKind kind)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The persisted dependency kind is not defined.");
        }

        Edge = edge;
        Kind = kind;
    }

    public DependencyEdge Edge { get; }

    public ImportedDependencyKind Kind { get; }
}
