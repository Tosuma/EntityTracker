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

    public static TrackerSyncReview CreateReview(
        TrackerId sourceId, TrackerId destinationId,
        TrackerSyncStructure source, TrackerSyncStructure destination,
        TrackerSyncBaseline? baseline,
        string sourceFingerprint, string destinationFingerprint)
    {
        Alignment aligned = Align(source, destination, baseline);
        var a = aligned.Source.Entities;
        var b = aligned.Destination.Entities;
        var oldA = aligned.OldSource.Entities;
        var oldB = aligned.OldDestination.Entities;
        bool hasBaseline = baseline is not null;
        List<TrackerSyncChange> changes = [];
        foreach (string key in a.Keys.Union(b.Keys).Union(oldA.Keys).Union(oldB.Keys)
                     .OrderBy(static key => key, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out TrackerSyncEntity? ae);
            b.TryGetValue(key, out TrackerSyncEntity? be);
            oldA.TryGetValue(key, out TrackerSyncEntity? oldAe);
            oldB.TryGetValue(key, out TrackerSyncEntity? oldBe);
            // An entity with different names on the two sides is shown by its name in the copy, where
            // it is being decided.
            string name = ae is not null && be is not null && Key(ae.Name) != Key(be.Name)
                ? be.Name
                : ae?.Name ?? be?.Name ?? oldAe?.Name ?? oldBe!.Name;
            bool aActive = ae?.Active == true;
            bool bActive = be?.Active == true;
            if (Different(hasBaseline, aActive, bActive, oldAe?.Active == true, oldBe?.Active == true))
                Add(TrackerSyncChangeKind.Entity, null, null,
                    aActive ? "Active" : "Archived or absent",
                    bActive ? "Active" : "Archived or absent");

            // An entity that is active on only one side is one decision: it comes whole, with its
            // priority, group, Filter active and dependencies, or not at all, so those are not asked
            // about separately.
            if (aActive != bActive) continue;
            if (!aActive) continue;
            if (Different(hasBaseline, ae!.Name, be!.Name, oldAe?.Name, oldBe?.Name))
                Add(TrackerSyncChangeKind.Name, null, null, ae.Name, be.Name);
            if (Different(hasBaseline, ae.RequestedPriority, be.RequestedPriority,
                    oldAe?.RequestedPriority, oldBe?.RequestedPriority))
                Add(TrackerSyncChangeKind.RequestedPriority, null, null,
                    ae.RequestedPriority?.ToString() ?? "None",
                    be.RequestedPriority?.ToString() ?? "None");
            if (Different(hasBaseline, ae.GroupName ?? "", be.GroupName ?? "",
                    oldAe?.GroupName ?? "", oldBe?.GroupName ?? ""))
                Add(TrackerSyncChangeKind.Group, null, null,
                    string.IsNullOrEmpty(ae.GroupName) ? "None" : ae.GroupName,
                    string.IsNullOrEmpty(be.GroupName) ? "None" : be.GroupName);
            // A baseline saved before Filter active was synced has null here, which counts as a first sync.
            if (Different(hasBaseline, ae.FilterActive ?? "", be.FilterActive ?? "",
                    oldAe?.FilterActive, oldBe?.FilterActive))
                Add(TrackerSyncChangeKind.FilterActive, null, null, ae.FilterActive ?? "", be.FilterActive ?? "");
            if (Different(hasBaseline, ae.DueDate, be.DueDate, oldAe?.DueDate, oldBe?.DueDate))
                Add(TrackerSyncChangeKind.DueDate, null, null,
                    DueDateText.Format(ae.DueDate, "None"), DueDateText.Format(be.DueDate, "None"));

            var ad = aligned.Source.Dependencies(ae);
            var bd = aligned.Destination.Dependencies(be);
            var oldAd = aligned.OldSource.Dependencies(oldAe);
            var oldBd = aligned.OldDestination.Dependencies(oldBe);
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
                string targetName = bv?.Name ?? av?.Name ?? oldBv?.Name ?? oldAv!.Name;
                Add(TrackerSyncChangeKind.Dependency, targetName, target,
                    av is null ? Absent : Present, bv is null ? Absent : Present);
            }

            void Add(TrackerSyncChangeKind kind, string? dependencyName, string? dependencyKey,
                string sourceValue, string destinationValue) =>
                changes.Add(new TrackerSyncChange(kind, name, dependencyName, sourceValue, destinationValue)
                {
                    EntityKey = key,
                    DependencyKey = dependencyKey
                });
        }
        return new TrackerSyncReview(sourceId, destinationId, source, destination,
            baseline, sourceFingerprint, destinationFingerprint, changes);
    }

    public static TrackerSyncStructure Resolve(TrackerSyncReview review)
    {
        if (!review.CanApply)
            throw new InvalidOperationException("Choose a result for every sync change before applying.");
        Alignment aligned = Align(review.Source, review.Destination, review.Baseline);
        var a = aligned.Source.Entities;
        var b = aligned.Destination.Entities;
        var choices = review.Changes.ToDictionary(
            static change => (change.Kind, change.EntityKey, change.DependencyKey ?? ""));

        // First each entity with its name, then its dependencies, which refer to the names the other
        // entities end up with.
        Dictionary<string, TrackerSyncEntity> result = new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<string, TrackerSyncDependency>> dependencies = new(StringComparer.Ordinal);
        foreach (string key in a.Keys.Union(b.Keys).OrderBy(static key => key, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out TrackerSyncEntity? ae);
            b.TryGetValue(key, out TrackerSyncEntity? be);
            if ((ae?.Active == true) != (be?.Active == true))
            {
                // Active on one side only: the chosen side gives the whole entity. Taking the source's
                // archived state archives the copy's entity but keeps its own details.
                bool useSource = Pick(key, TrackerSyncChangeKind.Entity, "", true, false);
                if (useSource && ae?.Active == true)
                    Keep(key, ae with { Id = be?.Id, SourceName = null }, aligned.Source.Dependencies(ae));
                else if (useSource) Keep(key, Paired(be! with { Active = false }, ae), []);
                else if (be is not null) Keep(key, Paired(be, ae), aligned.Destination.Dependencies(be));
                continue;
            }

            string name = ae is not null && be is not null
                ? Pick(key, TrackerSyncChangeKind.Name, "", ae.Name, be.Name)
                : be?.Name ?? ae!.Name;
            bool active = be?.Active == true;
            int? priority = Pick(key, TrackerSyncChangeKind.RequestedPriority, "",
                ae?.RequestedPriority, be?.RequestedPriority);
            string group = Pick(key, TrackerSyncChangeKind.Group, "", ae?.GroupName ?? "", be?.GroupName ?? "");
            string filterActive = Pick(key, TrackerSyncChangeKind.FilterActive, "",
                ae?.FilterActive ?? "", be?.FilterActive ?? "");
            DateOnly? dueDate = Pick(key, TrackerSyncChangeKind.DueDate, "", ae?.DueDate, be?.DueDate);
            var ad = aligned.Source.Dependencies(ae);
            var bd = aligned.Destination.Dependencies(be);
            Dictionary<string, TrackerSyncDependency> selected = new(StringComparer.Ordinal);
            foreach (string target in ad.Keys.Union(bd.Keys))
            {
                ad.TryGetValue(target, out TrackerSyncDependency? av);
                bd.TryGetValue(target, out TrackerSyncDependency? bv);
                if (Pick(key, TrackerSyncChangeKind.Dependency, target, av, bv) is { } dependency)
                    selected[target] = dependency;
            }
            Keep(key, Paired(new TrackerSyncEntity(name, active, priority, group, [], filterActive, dueDate,
                be?.Id), ae), selected);
        }

        string? duplicate = result.Values
            .GroupBy(static entity => Key(entity.Name), StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.First().Name;
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Two entities would be called {duplicate} in this tracker. Choose another result for one of them.");

        return new TrackerSyncStructure(result
            .Select(pair => pair.Value with
            {
                // A dependency on an entity refers to the name that entity ends up with.
                Dependencies = dependencies[pair.Key]
                    .Select(target => result.TryGetValue(target.Key, out TrackerSyncEntity? entity)
                        ? new TrackerSyncDependency(entity.Name)
                        : target.Value)
                    .OrderBy(static dependency => Key(dependency.Name), StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(static entity => Key(entity.Name), StringComparer.Ordinal)
            .ToArray());

        void Keep(string key, TrackerSyncEntity entity, Dictionary<string, TrackerSyncDependency> selected)
        {
            result[key] = entity;
            dependencies[key] = selected;
        }

        T Pick<T>(string key, TrackerSyncChangeKind kind, string target, T sourceValue, T destinationValue)
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

    /// <summary>
    /// The entity a review change is about on one side, paired the way the review paired them, so an
    /// entity with different names on the two sides is found on both.
    /// </summary>
    public static TrackerSyncEntity? Find(TrackerSyncReview review, TrackerSyncChoice side, TrackerSyncChange change)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(change);
        Alignment aligned = Align(review.Source, review.Destination, review.Baseline);
        return (side == TrackerSyncChoice.Source ? aligned.Source : aligned.Destination)
            .Entities.GetValueOrDefault(change.EntityKey);
    }

    public static string Key(string name) => EntitySourceKey.From(name).Value;

    /// <summary>The value of a dependency row on the side that has the dependency.</summary>
    public const string Present = "Present";

    /// <summary>The value of a dependency row on the side that does not have the dependency.</summary>
    public const string Absent = "Absent";

    /// <summary>
    /// The copy's entity as the baseline keeps it: with the source entity's name when the two names
    /// differ, so the next sync still pairs them.
    /// </summary>
    private static TrackerSyncEntity Paired(TrackerSyncEntity destination, TrackerSyncEntity? source) =>
        destination with
        {
            SourceName = source is not null && Key(source.Name) != Key(destination.Name) ? source.Name : null
        };

    /// <summary>
    /// Keys the entities of both sides and of the baseline so one entity has the same key everywhere: the
    /// name key it had in the source at the last sync. A source and a copy entity are paired by that
    /// name, or by the source name the baseline kept for the copy's entity. An entity renamed since the
    /// last sync is found by its ID, so it keeps its key.
    /// </summary>
    private static Alignment Align(
        TrackerSyncStructure source, TrackerSyncStructure destination, TrackerSyncBaseline? baseline)
    {
        Side oldSource = Side.Create(baseline?.Source, null, static entity => Key(entity.Name));
        Side oldDestination = Side.Create(baseline?.Destination, null,
            static entity => Key(entity.SourceName ?? entity.Name));
        return new Alignment(
            Side.Create(source, oldSource, static entity => Key(entity.Name)),
            Side.Create(destination, oldDestination, static entity => Key(entity.Name)),
            oldSource,
            oldDestination);
    }

    private sealed record Alignment(Side Source, Side Destination, Side OldSource, Side OldDestination);

    private sealed class Side
    {
        // Prefixes the key of an entity whose key another entity holds, so the two stay apart. A name
        // can't contain it.
        private const string Unpaired = "\u0001";

        private readonly Dictionary<string, string> _keyByName;

        private Side(Dictionary<string, TrackerSyncEntity> entities, Dictionary<string, string> keyByName)
        {
            Entities = entities;
            _keyByName = keyByName;
        }

        public Dictionary<string, TrackerSyncEntity> Entities { get; }

        /// <param name="structure">The side's entities.</param>
        /// <param name="old">The same side at the last sync: an entity found there by ID keeps its key.</param>
        /// <param name="pairKey">The key of any other entity.</param>
        public static Side Create(TrackerSyncStructure? structure, Side? old, Func<TrackerSyncEntity, string> pairKey)
        {
            Dictionary<Guid, string> oldKeys = new();
            foreach ((string key, TrackerSyncEntity entity) in old?.Entities ?? [])
                if (entity.Id is { } id) oldKeys.TryAdd(id, key);

            // Entities paired for certain (found by ID, or with a recorded source name) take their key
            // first; another entity with the same key gets one of its own.
            var candidates = (structure?.Entities ?? [])
                .Select(entity => entity.Id is { } id && oldKeys.TryGetValue(id, out string? oldKey)
                    ? (Entity: entity, Key: oldKey, Certain: true)
                    : (Entity: entity, Key: pairKey(entity), Certain: entity.SourceName is not null))
                .OrderBy(static item => item.Certain ? 0 : 1);
            Dictionary<string, TrackerSyncEntity> entities = new(StringComparer.Ordinal);
            Dictionary<string, string> keyByName = new(StringComparer.Ordinal);
            foreach ((TrackerSyncEntity entity, string key, bool _) in candidates)
            {
                string unique = entities.ContainsKey(key) ? Unpaired + Key(entity.Name) : key;
                if (!entities.TryAdd(unique, entity)) continue;
                keyByName.TryAdd(Key(entity.Name), unique);
            }
            return new Side(entities, keyByName);
        }

        /// <summary>
        /// An active entity's dependencies by key. One on an entity of this side has that entity's key;
        /// one on a name no entity has keeps its name key, unless an entity holds that key.
        /// </summary>
        public Dictionary<string, TrackerSyncDependency> Dependencies(TrackerSyncEntity? entity)
        {
            Dictionary<string, TrackerSyncDependency> dependencies = new(StringComparer.Ordinal);
            if (entity?.Active != true) return dependencies;
            foreach (TrackerSyncDependency dependency in entity.Dependencies)
            {
                string name = Key(dependency.Name);
                string key = _keyByName.TryGetValue(name, out string? entityKey) ? entityKey
                    : Entities.ContainsKey(name) ? Unpaired + name
                    : name;
                dependencies.TryAdd(key, dependency);
            }
            return dependencies;
        }
    }

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
