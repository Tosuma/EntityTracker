namespace EntityTracker.Application.Importing;

public enum ImportedDependencyKind
{
    Mandatory,
    Optional
}

public sealed record ImportedDependency
{
    public ImportedDependency(
        EntitySourceKey dependentSourceKey,
        EntitySourceKey dependencySourceKey,
        ImportedDependencyKind kind)
    {
        ArgumentNullException.ThrowIfNull(dependentSourceKey);
        ArgumentNullException.ThrowIfNull(dependencySourceKey);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The imported dependency kind is not defined.");
        }

        if (dependentSourceKey == dependencySourceKey)
        {
            throw new ArgumentException(
                "An imported entity cannot depend on itself.",
                nameof(dependencySourceKey));
        }

        DependentSourceKey = dependentSourceKey;
        DependencySourceKey = dependencySourceKey;
        Kind = kind;
    }

    public EntitySourceKey DependentSourceKey { get; }

    public EntitySourceKey DependencySourceKey { get; }

    public ImportedDependencyKind Kind { get; }
}
