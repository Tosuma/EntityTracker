namespace EntityTracker.Domain;

public sealed class TrackedEntity
{
    public TrackedEntity(
        EntityId id,
        TrackerId trackerId,
        string sourceName,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityLifecycleState lifecycleState = EntityLifecycleState.Active,
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null,
        string filterActive = "",
        string sharedNotes = "")
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(trackerId);
        ValidateSourceName(sourceName);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(filterActive);
        ArgumentNullException.ThrowIfNull(sharedNotes);

        EnsureDefinedStatus(status);
        EnsureDefinedLifecycleState(lifecycleState);
        EnsureValidRequestedPriority(requestedPriority);

        Id = id;
        TrackerId = trackerId;
        SourceName = sourceName.Trim();
        Status = status;
        Notes = notes;
        FilterActive = filterActive;
        SharedNotes = sharedNotes;
        LifecycleState = lifecycleState;
        RequestedPriority = requestedPriority;
        ResponsibleDeveloper = NormalizeResponsibleDeveloper(responsibleDeveloper);
        GroupName = NormalizeGroupName(groupName);
    }

    /// <summary>
    /// A placeholder for <paramref name="sourceName"/>, for a dependency on a name no entity has yet. Its ID
    /// comes from the Tracker and the name, so colleagues who create the same placeholder agree on it.
    /// </summary>
    public static TrackedEntity Placeholder(TrackerId trackerId, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ValidateSourceName(sourceName);
        return new TrackedEntity(PlaceholderId(trackerId, sourceName), trackerId, sourceName.Trim(),
            lifecycleState: EntityLifecycleState.Placeholder);
    }

    /// <summary>The ID <see cref="Placeholder"/> gives a placeholder for this name in this Tracker.</summary>
    public static EntityId PlaceholderId(TrackerId trackerId, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(sourceName);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"placeholder:{trackerId.Value:D}:{sourceName.Trim().ToUpperInvariant()}"));
        return new EntityId(new Guid(hash.AsSpan(0, 16)));
    }

    public bool IsPlaceholder => LifecycleState == EntityLifecycleState.Placeholder;

    public EntityId Id { get; }

    public TrackerId TrackerId { get; }

    public string SourceName { get; private set; }

    public DevelopmentStatus Status { get; private set; }

    public string Notes { get; private set; }

    public string FilterActive { get; private set; }

    /// <summary>Gets notes shared with the client: they appear in client reports. <see cref="Notes"/> stay internal.</summary>
    public string SharedNotes { get; private set; }

    public EntityLifecycleState LifecycleState { get; private set; }

    public int? RequestedPriority { get; private set; }

    public string ResponsibleDeveloper { get; private set; }

    public string GroupName { get; private set; }

    public void ChangeSourceName(string sourceName)
    {
        ValidateSourceName(sourceName);
        SourceName = sourceName.Trim();
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

    public void ChangeFilterActive(string filterActive)
    {
        ArgumentNullException.ThrowIfNull(filterActive);
        FilterActive = filterActive;
    }

    public void ChangeSharedNotes(string sharedNotes)
    {
        ArgumentNullException.ThrowIfNull(sharedNotes);
        SharedNotes = sharedNotes;
    }

    public void ChangeLifecycleState(EntityLifecycleState lifecycleState)
    {
        EnsureDefinedLifecycleState(lifecycleState);
        LifecycleState = lifecycleState;
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
