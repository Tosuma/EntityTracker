namespace EntityTracker.Domain;

public sealed class TrackedEntity
{
    public TrackedEntity(
        EntityId id,
        string sourceName,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityLifecycleState lifecycleState = EntityLifecycleState.Active,
        EntityProvenance provenance = EntityProvenance.Imported,
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ValidateSourceName(sourceName);
        ArgumentNullException.ThrowIfNull(notes);

        EnsureDefinedStatus(status);
        EnsureDefinedLifecycleState(lifecycleState);
        EnsureDefinedProvenance(provenance);
        EnsureValidRequestedPriority(requestedPriority);

        Id = id;
        SourceName = sourceName;
        Status = status;
        Notes = notes;
        LifecycleState = lifecycleState;
        Provenance = provenance;
        RequestedPriority = requestedPriority;
        ResponsibleDeveloper = NormalizeResponsibleDeveloper(responsibleDeveloper);
        GroupName = NormalizeGroupName(groupName);
    }

    public EntityId Id { get; }

    public string SourceName { get; private set; }

    public DevelopmentStatus Status { get; private set; }

    public string Notes { get; private set; }

    public EntityLifecycleState LifecycleState { get; private set; }

    public EntityProvenance Provenance { get; private set; }

    public int? RequestedPriority { get; private set; }

    public string ResponsibleDeveloper { get; private set; }

    public string GroupName { get; private set; }

    public void ChangeSourceName(string sourceName)
    {
        ValidateSourceName(sourceName);
        SourceName = sourceName;
    }

    public void ChangeStatus(DevelopmentStatus status)
    {
        EnsureDefinedStatus(status);
        Status = status;
    }

    public void ChangeNotes(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Notes = notes;
    }

    public void ChangeLifecycleState(EntityLifecycleState lifecycleState)
    {
        EnsureDefinedLifecycleState(lifecycleState);
        LifecycleState = lifecycleState;
    }

    public void ChangeProvenance(EntityProvenance provenance)
    {
        EnsureDefinedProvenance(provenance);

        if (Provenance != EntityProvenance.ManualOnly ||
            provenance != EntityProvenance.ManualAndImported)
        {
            throw new InvalidOperationException(
                "Entity provenance may only transition from ManualOnly to ManualAndImported.");
        }

        Provenance = provenance;
    }

    public void ChangeRequestedPriority(int? requestedPriority)
    {
        EnsureValidRequestedPriority(requestedPriority);
        RequestedPriority = requestedPriority;
    }

    public void ChangeResponsibleDeveloper(string? responsibleDeveloper)
    {
        ResponsibleDeveloper = NormalizeResponsibleDeveloper(responsibleDeveloper);
    }

    public void ChangeGroupName(string? groupName)
    {
        GroupName = NormalizeGroupName(groupName);
    }

    private static void ValidateSourceName(string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("A source name cannot be empty or whitespace.", nameof(sourceName));
        }
    }

    private static void EnsureDefinedStatus(DevelopmentStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The development status is not defined.");
        }
    }

    private static void EnsureDefinedLifecycleState(EntityLifecycleState lifecycleState)
    {
        if (!Enum.IsDefined(lifecycleState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleState),
                lifecycleState,
                "The entity lifecycle state is not defined.");
        }
    }

    private static void EnsureDefinedProvenance(EntityProvenance provenance)
    {
        if (!Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provenance),
                provenance,
                "The entity provenance is not defined.");
        }
    }

    private static void EnsureValidRequestedPriority(int? requestedPriority)
    {
        if (requestedPriority is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedPriority),
                requestedPriority,
                "Requested priority must be between 1 and 5.");
        }
    }

    private static string NormalizeResponsibleDeveloper(string? responsibleDeveloper) =>
        responsibleDeveloper?.Trim() ?? string.Empty;

    private static string NormalizeGroupName(string? groupName) =>
        groupName?.Trim() ?? string.Empty;
}
