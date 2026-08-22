namespace EntityTracker.Domain;

/// <summary>
/// Represents a dependency whose target is known only by its source name.
/// </summary>
public sealed record UnresolvedDependency
{
    public UnresolvedDependency(EntityId dependentEntityId, string dependencySourceName)
    {
        ArgumentNullException.ThrowIfNull(dependentEntityId);
        ArgumentNullException.ThrowIfNull(dependencySourceName);

        string trimmedName = dependencySourceName.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException(
                "An unresolved dependency source name cannot be empty or whitespace.",
                nameof(dependencySourceName));
        }

        DependentEntityId = dependentEntityId;
        DependencySourceName = trimmedName;
    }

    public EntityId DependentEntityId { get; }

    public string DependencySourceName { get; }
}
