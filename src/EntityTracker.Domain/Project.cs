namespace EntityTracker.Domain;

public sealed class Project
{
    public Project(
        ProjectId id,
        string name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        CatalogLifecycleState lifecycleState = CatalogLifecycleState.Active,
        DateTimeOffset? recycledAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        Name = NormalizeName(name);
        CreatedAtUtc = EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        EnsureLifecycle(lifecycleState, recycledAtUtc);
        LifecycleState = lifecycleState;
        RecycledAtUtc = recycledAtUtc;
    }

    public ProjectId Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public CatalogLifecycleState LifecycleState { get; }

    public DateTimeOffset? RecycledAtUtc { get; }

    internal static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A project or tracker name cannot be empty.", nameof(name));
        }

        return normalized;
    }

    internal static DateTimeOffset EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Catalog timestamps must be UTC.", parameterName);
        }

        return value;
    }

    internal static void EnsureLifecycle(
        CatalogLifecycleState lifecycleState,
        DateTimeOffset? recycledAtUtc)
    {
        if (!Enum.IsDefined(lifecycleState))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleState));
        }

        if ((lifecycleState == CatalogLifecycleState.Recycled) != (recycledAtUtc is not null))
        {
            throw new ArgumentException(
                "A recycled catalog item requires a recycle timestamp and an active item cannot have one.",
                nameof(recycledAtUtc));
        }

        if (recycledAtUtc is { } timestamp)
        {
            EnsureUtc(timestamp, nameof(recycledAtUtc));
        }
    }
}
