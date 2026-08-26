namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationRequest
{
    public ManualEntityCreationRequest(
        string entityName,
        IEnumerable<ManualDependencySelection> dependencies,
        string? responsibleDeveloper = null,
        string? groupName = null)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(dependencies);

        EntityName = entityName;
        Dependencies = dependencies.ToArray();
        ResponsibleDeveloper = responsibleDeveloper;
        GroupName = groupName;

        if (Dependencies.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                "Dependencies cannot contain null entries.",
                nameof(dependencies));
        }
    }

    public string EntityName { get; }

    public IReadOnlyList<ManualDependencySelection> Dependencies { get; }

    public string? ResponsibleDeveloper { get; }

    public string? GroupName { get; }
}
