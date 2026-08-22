using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public sealed record PersistedUnresolvedDependency
{
    public PersistedUnresolvedDependency(
        UnresolvedDependency dependency,
        ImportedDependencyKind kind)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The persisted dependency kind is not defined.");
        }

        Dependency = dependency;
        Kind = kind;
    }

    public UnresolvedDependency Dependency { get; }

    public ImportedDependencyKind Kind { get; }
}
