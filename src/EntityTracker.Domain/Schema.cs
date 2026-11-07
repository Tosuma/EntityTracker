namespace EntityTracker.Domain;

/// <summary>
/// A Project's shared structure: which entities exist and how they depend on each other. Trackers can
/// be created from it, and its changes are pushed to them. It holds no progress.
/// </summary>
public sealed class Schema
{
    public Schema(
        SchemaId id,
        ProjectId projectId,
        string name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        CatalogLifecycleState lifecycleState = CatalogLifecycleState.Active,
        DateTimeOffset? recycledAtUtc = null)
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
    }

    public SchemaId Id { get; }

    public ProjectId ProjectId { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public CatalogLifecycleState LifecycleState { get; }

    public DateTimeOffset? RecycledAtUtc { get; }
}
