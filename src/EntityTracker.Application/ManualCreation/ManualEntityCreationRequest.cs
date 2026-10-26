using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationRequest
{
    public ManualEntityCreationRequest(
        string entityName,
        IEnumerable<ManualDependencySelection> dependencies,
        string? responsibleDeveloper = null,
        string? groupName = null,
        int? requestedPriority = null,
        IEnumerable<DeveloperId>? developerIds = null,
        DateOnly? dueDate = null)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(dependencies);

        EntityName = entityName;
        Dependencies = dependencies.ToArray();
        ResponsibleDeveloper = responsibleDeveloper;
        GroupName = groupName;
        RequestedPriority = requestedPriority;
        DeveloperIds = (developerIds ?? []).Distinct().ToArray();
        DueDate = dueDate;

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

    public int? RequestedPriority { get; }
    public IReadOnlyList<DeveloperId> DeveloperIds { get; }

    public DateOnly? DueDate { get; }
}
