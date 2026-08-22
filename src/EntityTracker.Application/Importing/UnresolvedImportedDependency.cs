namespace EntityTracker.Application.Importing;

public sealed record UnresolvedImportedDependency
{
    public UnresolvedImportedDependency(
        EntitySourceKey dependentSourceKey,
        EntitySourceKey dependencySourceKey,
        string dependencySourceName,
        ImportedDependencyKind kind)
    {
        ArgumentNullException.ThrowIfNull(dependentSourceKey);
        ArgumentNullException.ThrowIfNull(dependencySourceKey);
        ArgumentNullException.ThrowIfNull(dependencySourceName);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The imported dependency kind is not defined.");
        }

        string trimmedName = dependencySourceName.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException(
                "An unresolved dependency source name cannot be empty or whitespace.",
                nameof(dependencySourceName));
        }

        if (EntitySourceKey.From(trimmedName) != dependencySourceKey)
        {
            throw new ArgumentException(
                "The unresolved dependency source key must match its source name.",
                nameof(dependencySourceKey));
        }

        if (dependentSourceKey == dependencySourceKey)
        {
            throw new ArgumentException(
                "An imported entity cannot depend on itself.",
                nameof(dependencySourceKey));
        }

        DependentSourceKey = dependentSourceKey;
        DependencySourceKey = dependencySourceKey;
        DependencySourceName = trimmedName;
        Kind = kind;
    }

    public EntitySourceKey DependentSourceKey { get; }

    public EntitySourceKey DependencySourceKey { get; }

    public string DependencySourceName { get; }

    public ImportedDependencyKind Kind { get; }
}
