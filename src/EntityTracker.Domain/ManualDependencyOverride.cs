namespace EntityTracker.Domain;

/// <summary>
/// Preserves a user's correction to an imported dependency without changing the imported fact.
/// </summary>
public sealed record ManualDependencyOverride
{
    public ManualDependencyOverride(
        EntityId dependentEntityId,
        string dependencySourceName,
        ManualDependencyOverrideAction action)
    {
        ArgumentNullException.ThrowIfNull(dependentEntityId);
        ArgumentNullException.ThrowIfNull(dependencySourceName);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        string trimmedName = dependencySourceName.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException(
                "A dependency override source name cannot be empty or whitespace.",
                nameof(dependencySourceName));
        }

        DependentEntityId = dependentEntityId;
        DependencySourceName = trimmedName;
        Action = action;
    }

    public EntityId DependentEntityId { get; }

    public string DependencySourceName { get; }

    public ManualDependencyOverrideAction Action { get; }
}
