using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

/// <summary>
/// Captures a Tracker's structure for a Sync from source baseline. Copies made before schemas keep such a
/// baseline as their dormant link to their source, which Convert to schema reads.
/// </summary>
public static class TrackerSyncPlanner
{
    public static TrackerSyncStructure Capture(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<EntityDependency> dependencies)
    {
        // Two Trackers have different entity IDs, so they are compared by entity name. Each entity's ID
        // is kept too, so the next sync recognises it after a rename. A placeholder is not an entity of
        // its own; it is only the name dependencies refer to.
        var byId = entities.ToDictionary(static entity => entity.Id);
        var declarations = new Dictionary<EntityId, Dictionary<string, TrackerSyncDependency>>();
        foreach (TrackedEntity entity in entities.Where(static entity => !entity.IsPlaceholder))
            declarations[entity.Id] = new Dictionary<string, TrackerSyncDependency>(StringComparer.Ordinal);
        foreach (EntityDependency dependency in dependencies)
        {
            if (byId.TryGetValue(dependency.DependencyEntityId, out TrackedEntity? target) &&
                declarations.TryGetValue(dependency.DependentEntityId, out var owner))
                owner[Key(target.SourceName)] = new TrackerSyncDependency(target.SourceName);
        }
        return new TrackerSyncStructure(entities.Where(static entity => !entity.IsPlaceholder)
            .OrderBy(static entity => Key(entity.SourceName), StringComparer.Ordinal)
            .Select(entity => new TrackerSyncEntity(
                entity.SourceName,
                entity.LifecycleState == EntityLifecycleState.Active,
                entity.RequestedPriority,
                entity.GroupName,
                declarations[entity.Id].Values
                    .OrderBy(static dependency => Key(dependency.Name), StringComparer.Ordinal).ToArray(),
                entity.FilterActive,
                entity.DueDate,
                entity.Id.Value))
            .ToArray());
    }

    public static string Key(string name) => EntitySourceKey.From(name).Value;
}
