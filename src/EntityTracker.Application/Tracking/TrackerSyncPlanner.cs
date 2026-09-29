using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public static class TrackerSyncPlanner
{
    public static TrackerSyncStructure Capture(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> resolved,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved,
        IReadOnlyList<ManualDependencyOverride> overrides)
    {
        var byId = entities.ToDictionary(static entity => entity.Id);
        var declarations = new Dictionary<EntityId, Dictionary<string, TrackerSyncDependency>>();
        foreach (TrackedEntity entity in entities)
            declarations[entity.Id] = new Dictionary<string, TrackerSyncDependency>(StringComparer.Ordinal);
        foreach (PersistedDependency edge in resolved)
        {
            if (byId.TryGetValue(edge.Edge.DependencyEntityId, out TrackedEntity? target))
                Put(edge.Edge.DependentEntityId, target.SourceName, edge.Kind);
        }
        foreach (PersistedUnresolvedDependency edge in unresolved)
            Put(edge.Dependency.DependentEntityId, edge.Dependency.DependencySourceName, edge.Kind);
        foreach (ManualDependencyOverride item in overrides)
        {
            string key = Key(item.DependencySourceName);
            if (!declarations.TryGetValue(item.DependentEntityId, out var owner)) continue;
            if (item.Action == ManualDependencyOverrideAction.Suppress) owner.Remove(key);
            else owner[key] = new TrackerSyncDependency(item.DependencySourceName, ImportedDependencyKind.Mandatory);
        }
        return new TrackerSyncStructure(entities
            .OrderBy(static entity => Key(entity.SourceName), StringComparer.Ordinal)
            .Select(entity => new TrackerSyncEntity(
                entity.SourceName,
                entity.LifecycleState == EntityLifecycleState.Active,
                entity.RequestedPriority,
                entity.GroupName,
                declarations[entity.Id].Values
                    .OrderBy(static dependency => Key(dependency.Name), StringComparer.Ordinal).ToArray()))
            .ToArray());

        void Put(EntityId ownerId, string targetName, ImportedDependencyKind kind)
        {
            if (declarations.TryGetValue(ownerId, out var owner))
                owner[Key(targetName)] = new TrackerSyncDependency(targetName, kind);
        }
    }

    public static TrackerSyncReview CreateReview(
        TrackerId sourceId, TrackerId destinationId,
        TrackerSyncStructure source, TrackerSyncStructure destination,
        TrackerSyncBaseline? baseline,
        string sourceFingerprint, string destinationFingerprint)
    {
        var a = Map(source);
        var b = Map(destination);
        var oldA = Map(baseline?.Source);
        var oldB = Map(baseline?.Destination);
        List<TrackerSyncChange> changes = [];
        foreach (string key in a.Keys.Union(b.Keys).Union(oldA.Keys).Union(oldB.Keys)
                     .OrderBy(static key => key, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out TrackerSyncEntity? ae);
            b.TryGetValue(key, out TrackerSyncEntity? be);
            oldA.TryGetValue(key, out TrackerSyncEntity? oldAe);
            oldB.TryGetValue(key, out TrackerSyncEntity? oldBe);
            string name = ae?.Name ?? be?.Name ?? oldAe?.Name ?? oldBe!.Name;
            bool aActive = ae?.Active == true;
            bool bActive = be?.Active == true;
            if (Different(aActive, bActive, oldAe?.Active == true, oldBe?.Active == true))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Entity, name, null,
                    aActive ? "Active" : "Archived or absent",
                    bActive ? "Active" : "Archived or absent", false));

            if (!aActive && !bActive) continue;
            if (Different(ae?.RequestedPriority, be?.RequestedPriority,
                    oldAe?.RequestedPriority, oldBe?.RequestedPriority))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.RequestedPriority, name, null,
                    ae?.RequestedPriority?.ToString() ?? "None",
                    be?.RequestedPriority?.ToString() ?? "None", false));
            if (Different(ae?.GroupName ?? "", be?.GroupName ?? "",
                    oldAe?.GroupName ?? "", oldBe?.GroupName ?? ""))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Group, name, null,
                    string.IsNullOrEmpty(ae?.GroupName) ? "None" : ae.GroupName,
                    string.IsNullOrEmpty(be?.GroupName) ? "None" : be.GroupName, false));

            var ad = Dependencies(ae);
            var bd = Dependencies(be);
            var oldAd = Dependencies(oldAe);
            var oldBd = Dependencies(oldBe);
            foreach (string target in ad.Keys.Union(bd.Keys).Union(oldAd.Keys).Union(oldBd.Keys)
                         .OrderBy(static target => target, StringComparer.Ordinal))
            {
                ad.TryGetValue(target, out TrackerSyncDependency? av);
                bd.TryGetValue(target, out TrackerSyncDependency? bv);
                oldAd.TryGetValue(target, out TrackerSyncDependency? oldAv);
                oldBd.TryGetValue(target, out TrackerSyncDependency? oldBv);
                if (!Different(av?.Kind, bv?.Kind, oldAv?.Kind, oldBv?.Kind)) continue;
                string targetName = av?.Name ?? bv?.Name ?? oldAv?.Name ?? oldBv!.Name;
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Dependency,
                    name, targetName, av?.Kind.ToString() ?? "Absent",
                    bv?.Kind.ToString() ?? "Absent", av is null || bv is null));
            }
        }
        return new TrackerSyncReview(sourceId, destinationId, source, destination,
            baseline, sourceFingerprint, destinationFingerprint, changes);
    }

    public static TrackerSyncStructure Resolve(TrackerSyncReview review)
    {
        if (!review.CanApply)
            throw new InvalidOperationException("Choose a result for every sync change before applying.");
        var a = Map(review.Source);
        var b = Map(review.Destination);
        var choices = review.Changes.ToDictionary(
            static change => (change.Kind, Key(change.EntityName),
                change.DependencyName is null ? "" : Key(change.DependencyName)));
        List<TrackerSyncEntity> result = [];
        foreach (string key in a.Keys.Union(b.Keys).OrderBy(static key => key, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out TrackerSyncEntity? ae);
            b.TryGetValue(key, out TrackerSyncEntity? be);
            string name = be?.Name ?? ae!.Name;
            bool active = Pick(TrackerSyncChangeKind.Entity, "", ae?.Active == true, be?.Active == true);
            int? priority = Pick(TrackerSyncChangeKind.RequestedPriority, "",
                ae?.RequestedPriority, be?.RequestedPriority);
            string group = Pick(TrackerSyncChangeKind.Group, "", ae?.GroupName ?? "", be?.GroupName ?? "");
            var ad = Dependencies(ae);
            var bd = Dependencies(be);
            List<TrackerSyncDependency> dependencies = [];
            foreach (string target in ad.Keys.Union(bd.Keys).OrderBy(static item => item, StringComparer.Ordinal))
            {
                ad.TryGetValue(target, out TrackerSyncDependency? av);
                bd.TryGetValue(target, out TrackerSyncDependency? bv);
                TrackerSyncDependency? selected = Pick(TrackerSyncChangeKind.Dependency, target, av, bv);
                if (selected is not null) dependencies.Add(selected);
            }
            result.Add(new TrackerSyncEntity(name, active, priority, group, dependencies));

            T Pick<T>(TrackerSyncChangeKind kind, string target, T sourceValue, T destinationValue)
            {
                if (!choices.TryGetValue((kind, key, target), out TrackerSyncChange? change))
                    return destinationValue;
                return change.Choice switch
                {
                    TrackerSyncChoice.Source => sourceValue,
                    TrackerSyncChoice.Destination => destinationValue,
                    TrackerSyncChoice.Both when kind == TrackerSyncChangeKind.Dependency =>
                        !EqualityComparer<T>.Default.Equals(sourceValue, default!) ? sourceValue : destinationValue,
                    _ => throw new InvalidOperationException("An invalid sync choice was selected.")
                };
            }
        }
        return new TrackerSyncStructure(result);
    }

    public static string Key(string name) => EntitySourceKey.From(name).Value;

    private static Dictionary<string, TrackerSyncEntity> Map(TrackerSyncStructure? structure) =>
        (structure?.Entities ?? []).ToDictionary(static entity => Key(entity.Name), StringComparer.Ordinal);

    private static Dictionary<string, TrackerSyncDependency> Dependencies(TrackerSyncEntity? entity) =>
        (entity?.Active == true ? entity.Dependencies : [])
        .ToDictionary(static dependency => Key(dependency.Name), StringComparer.Ordinal);

    private static bool Different<T>(T a, T b, T oldA, T oldB) =>
        !EqualityComparer<T>.Default.Equals(a, b) &&
        (oldA is null && oldB is null ||
         !EqualityComparer<T>.Default.Equals(a, oldA) ||
         !EqualityComparer<T>.Default.Equals(b, oldB));
}
