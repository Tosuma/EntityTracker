using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public static class TrackerSyncPlanner
{
    public static TrackerSyncStructure Capture(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<EntityDependency> dependencies)
    {
        // Two Trackers have different entity IDs, so they are compared by entity name. A placeholder
        // is not an entity of its own; it is only the name dependencies refer to.
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
                entity.DueDate))
            .ToArray());
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
        bool hasBaseline = baseline is not null;
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
            if (Different(hasBaseline, aActive, bActive, oldAe?.Active == true, oldBe?.Active == true))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Entity, name, null,
                    aActive ? "Active" : "Archived or absent",
                    bActive ? "Active" : "Archived or absent"));

            // An entity that is active on only one side is one decision: it comes whole, with its
            // priority, group, Filter active and dependencies, or not at all, so those are not asked
            // about separately.
            if (aActive != bActive) continue;
            if (!aActive) continue;
            if (Different(hasBaseline, ae?.RequestedPriority, be?.RequestedPriority,
                    oldAe?.RequestedPriority, oldBe?.RequestedPriority))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.RequestedPriority, name, null,
                    ae?.RequestedPriority?.ToString() ?? "None",
                    be?.RequestedPriority?.ToString() ?? "None"));
            if (Different(hasBaseline, ae?.GroupName ?? "", be?.GroupName ?? "",
                    oldAe?.GroupName ?? "", oldBe?.GroupName ?? ""))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Group, name, null,
                    string.IsNullOrEmpty(ae?.GroupName) ? "None" : ae.GroupName,
                    string.IsNullOrEmpty(be?.GroupName) ? "None" : be.GroupName));
            // A baseline saved before Filter active was synced has null here, which counts as a first sync.
            if (Different(hasBaseline, ae?.FilterActive ?? "", be?.FilterActive ?? "",
                    oldAe?.FilterActive, oldBe?.FilterActive))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.FilterActive, name, null,
                    ae?.FilterActive ?? "", be?.FilterActive ?? ""));
            if (Different(hasBaseline, ae?.DueDate, be?.DueDate, oldAe?.DueDate, oldBe?.DueDate))
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.DueDate, name, null,
                    DueDateText.Format(ae?.DueDate, "None"), DueDateText.Format(be?.DueDate, "None")));

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
                // A dependency has no kind, so only whether it exists matters.
                if (!Different(hasBaseline, av is not null, bv is not null, oldAv is not null, oldBv is not null))
                    continue;
                string targetName = av?.Name ?? bv?.Name ?? oldAv?.Name ?? oldBv!.Name;
                changes.Add(new TrackerSyncChange(TrackerSyncChangeKind.Dependency,
                    name, targetName, av is null ? Absent : Present, bv is null ? Absent : Present));
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
            if ((ae?.Active == true) != (be?.Active == true))
            {
                // Active on one side only: the chosen side gives the whole entity. Taking the source's
                // archived state archives the copy's entity but keeps its own details.
                bool useSource = Pick(TrackerSyncChangeKind.Entity, "", true, false);
                if (useSource && ae?.Active == true) result.Add(ae);
                else if (useSource) result.Add(be! with { Active = false });
                else if (be is not null) result.Add(be);
                continue;
            }

            bool active = be?.Active == true;
            int? priority = Pick(TrackerSyncChangeKind.RequestedPriority, "",
                ae?.RequestedPriority, be?.RequestedPriority);
            string group = Pick(TrackerSyncChangeKind.Group, "", ae?.GroupName ?? "", be?.GroupName ?? "");
            string filterActive = Pick(TrackerSyncChangeKind.FilterActive, "",
                ae?.FilterActive ?? "", be?.FilterActive ?? "");
            DateOnly? dueDate = Pick(TrackerSyncChangeKind.DueDate, "", ae?.DueDate, be?.DueDate);
            var ad = Dependencies(ae);
            var bd = Dependencies(be);
            List<TrackerSyncDependency> dependencies = [];
            foreach (string target in ad.Keys.Union(bd.Keys).OrderBy(static item => item, StringComparer.Ordinal))
            {
                ad.TryGetValue(target, out TrackerSyncDependency? av);
                bd.TryGetValue(target, out TrackerSyncDependency? bv);
                TrackerSyncDependency? selected = Pick(TrackerSyncChangeKind.Dependency, target, av, bv);
                if (selected is not null)
                    dependencies.Add(selected);
            }
            result.Add(new TrackerSyncEntity(name, active, priority, group, dependencies, filterActive, dueDate));

            T Pick<T>(TrackerSyncChangeKind kind, string target, T sourceValue, T destinationValue)
            {
                if (!choices.TryGetValue((kind, key, target), out TrackerSyncChange? change))
                    return destinationValue;
                return change.Choice switch
                {
                    TrackerSyncChoice.Source => sourceValue,
                    TrackerSyncChoice.Destination => destinationValue,
                    _ => throw new InvalidOperationException("An invalid sync choice was selected.")
                };
            }
        }
        return new TrackerSyncStructure(result);
    }

    public static string Key(string name) => EntitySourceKey.From(name).Value;

    /// <summary>The value of a dependency row on the side that has the dependency.</summary>
    public const string Present = "Present";

    /// <summary>The value of a dependency row on the side that does not have the dependency.</summary>
    public const string Absent = "Absent";

    private static Dictionary<string, TrackerSyncEntity> Map(TrackerSyncStructure? structure) =>
        (structure?.Entities ?? []).ToDictionary(static entity => Key(entity.Name), StringComparer.Ordinal);

    private static Dictionary<string, TrackerSyncDependency> Dependencies(TrackerSyncEntity? entity) =>
        (entity?.Active == true ? entity.Dependencies : [])
        .ToDictionary(static dependency => Key(dependency.Name), StringComparer.Ordinal);

    /// <summary>
    /// Whether the two sides differ in a way to ask about: always on a first sync, and afterwards only
    /// when a side changed since the last sync.
    /// </summary>
    private static bool Different<T>(bool hasBaseline, T a, T b, T oldA, T oldB) =>
        !EqualityComparer<T>.Default.Equals(a, b) &&
        (!hasBaseline || oldA is null && oldB is null ||
         !EqualityComparer<T>.Default.Equals(a, oldA) ||
         !EqualityComparer<T>.Default.Equals(b, oldB));
}
