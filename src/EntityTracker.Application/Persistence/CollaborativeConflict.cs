using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Describes a future collaborative write conflict without depending on a storage provider.
/// Values are display-safe representations supplied by the provider; they must not contain secrets.
/// </summary>
public sealed class CollaborativeConflict
{
    public CollaborativeConflict(
        EntitySourceKey entityKey,
        EntityId? entityId,
        CollaborativeConflictField field,
        string? baseValue,
        string? localValue,
        string? remoteValue,
        EntitySourceKey? dependencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(entityKey);

        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        bool isDependencyConflict = field is
            CollaborativeConflictField.ImportedDependency or
            CollaborativeConflictField.ManualDependencyOverride;

        if (isDependencyConflict != (dependencyKey is not null))
        {
            throw new ArgumentException(
                isDependencyConflict
                    ? "A dependency conflict must identify its dependency key."
                    : "Only a dependency conflict may identify a dependency key.",
                nameof(dependencyKey));
        }

        EntityKey = entityKey;
        EntityId = entityId;
        Field = field;
        BaseValue = baseValue;
        LocalValue = localValue;
        RemoteValue = remoteValue;
        DependencyKey = dependencyKey;
    }

    public EntitySourceKey EntityKey { get; }

    public EntityId? EntityId { get; }

    public CollaborativeConflictField Field { get; }

    public EntitySourceKey? DependencyKey { get; }

    public string? BaseValue { get; }

    public string? LocalValue { get; }

    public string? RemoteValue { get; }
}
