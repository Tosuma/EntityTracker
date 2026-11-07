namespace EntityTracker.Domain;

public sealed class Tracker
{
    public Tracker(
        TrackerId id,
        ProjectId projectId,
        string name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        CatalogLifecycleState lifecycleState = CatalogLifecycleState.Active,
        DateTimeOffset? recycledAtUtc = null,
        TrackerId? copiedFromTrackerId = null,
        SchemaId? schemaId = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(projectId);
        Id = id;
        ProjectId = projectId;
        Name = Project.NormalizeName(name);
        CreatedAtUtc = Project.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = Project.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        Project.EnsureLifecycle(lifecycleState, recycledAtUtc);
        LifecycleState = lifecycleState;
        RecycledAtUtc = recycledAtUtc;
        CopiedFromTrackerId = copiedFromTrackerId;
        SchemaId = schemaId;
    }

    public TrackerId Id { get; }

    public ProjectId ProjectId { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public CatalogLifecycleState LifecycleState { get; }

    public DateTimeOffset? RecycledAtUtc { get; }

    public TrackerId? CopiedFromTrackerId { get; }

    /// <summary>The schema the Tracker was created from, until it is detached or the schema is purged.</summary>
    public SchemaId? SchemaId { get; }
}
