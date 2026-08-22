namespace EntityTracker.Domain;

public sealed class TrackedEntity
{
    public TrackedEntity(
        EntityId id,
        string sourceName,
        DevelopmentStatus status = DevelopmentStatus.NotStarted)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sourceName);

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("A source name cannot be empty or whitespace.", nameof(sourceName));
        }

        EnsureDefinedStatus(status);

        Id = id;
        SourceName = sourceName;
        Status = status;
    }

    public EntityId Id { get; }

    public string SourceName { get; }

    public DevelopmentStatus Status { get; private set; }

    public void ChangeStatus(DevelopmentStatus status)
    {
        EnsureDefinedStatus(status);
        Status = status;
    }

    private static void EnsureDefinedStatus(DevelopmentStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The development status is not defined.");
        }
    }
}
