namespace EntityTracker.Domain;

public sealed class TrackedEntity
{
    public TrackedEntity(
        EntityId id,
        string sourceName,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "")
    {
        ArgumentNullException.ThrowIfNull(id);
        ValidateSourceName(sourceName);
        ArgumentNullException.ThrowIfNull(notes);

        EnsureDefinedStatus(status);

        Id = id;
        SourceName = sourceName;
        Status = status;
        Notes = notes;
    }

    public EntityId Id { get; }

    public string SourceName { get; private set; }

    public DevelopmentStatus Status { get; private set; }

    public string Notes { get; private set; }

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
}
