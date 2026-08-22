using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public enum ManualDependencySelectionKind
{
    ExistingEntity,
    Unresolved
}

public sealed record ManualDependencySelection
{
    private ManualDependencySelection(
        string sourceName,
        ManualDependencySelectionKind kind,
        EntityId? entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        SourceName = sourceName.Trim();
        SourceKey = EntitySourceKey.From(SourceName);
        Kind = kind;
        EntityId = entityId;
    }

    public string SourceName { get; }

    public EntitySourceKey SourceKey { get; }

    public ManualDependencySelectionKind Kind { get; }

    public EntityId? EntityId { get; }

    public static ManualDependencySelection Existing(EntityId entityId, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return new ManualDependencySelection(
            sourceName,
            ManualDependencySelectionKind.ExistingEntity,
            entityId);
    }

    public static ManualDependencySelection Unresolved(string sourceName) =>
        new(sourceName, ManualDependencySelectionKind.Unresolved, null);
}
