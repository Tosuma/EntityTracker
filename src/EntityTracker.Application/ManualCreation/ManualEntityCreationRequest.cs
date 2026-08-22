namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationRequest
{
    public ManualEntityCreationRequest(
        string entityName,
        IEnumerable<ManualDependencySelection> dependencies)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(dependencies);

        EntityName = entityName;
        Dependencies = dependencies.ToArray();

        if (Dependencies.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                "Dependencies cannot contain null entries.",
                nameof(dependencies));
        }
    }

    public string EntityName { get; }

    public IReadOnlyList<ManualDependencySelection> Dependencies { get; }
}
