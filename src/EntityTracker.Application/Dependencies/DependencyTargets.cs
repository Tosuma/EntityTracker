using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

/// <summary>
/// Finds the entity a dependency on a name refers to: the entity of the Tracker with that name (in any
/// lifecycle state), or a new placeholder when no entity has it. Dependencies always refer to entities
/// by ID; a placeholder gives a name that is not an entity yet an ID.
/// </summary>
public sealed class DependencyTargets
{
    private readonly TrackerId _trackerId;
    private readonly Dictionary<EntitySourceKey, TrackedEntity> _byKey;
    private readonly HashSet<EntityId> _ids;
    private readonly List<TrackedEntity> _newPlaceholders = [];

    public DependencyTargets(TrackerId trackerId, IEnumerable<TrackedEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entities);
        _trackerId = trackerId;
        TrackedEntity[] all = entities.ToArray();
        _byKey = all.ToDictionary(static entity => EntitySourceKey.From(entity.SourceName));
        _ids = all.Select(static entity => entity.Id).ToHashSet();
    }

    /// <summary>Placeholders <see cref="For"/> created; they must be added with the change.</summary>
    public IReadOnlyList<TrackedEntity> NewPlaceholders => _newPlaceholders;

    public TrackedEntity For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EntitySourceKey key = EntitySourceKey.From(name);
        if (_byKey.TryGetValue(key, out TrackedEntity? existing)) return existing;
        TrackedEntity placeholder = TrackedEntity.Placeholder(_trackerId, name);
        // The stable ID can only be taken by an entity that was a placeholder for this name and was
        // renamed since; this placeholder then gets a new ID.
        if (_ids.Contains(placeholder.Id))
            placeholder = new TrackedEntity(EntityId.New(), _trackerId, name.Trim(),
                lifecycleState: EntityLifecycleState.Placeholder);
        _byKey.Add(key, placeholder);
        _ids.Add(placeholder.Id);
        _newPlaceholders.Add(placeholder);
        return placeholder;
    }
}
