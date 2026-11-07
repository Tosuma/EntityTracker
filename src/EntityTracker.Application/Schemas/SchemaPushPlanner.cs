using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>What a push does to one Tracker, as a change set and the Tracker's new baseline.</summary>
public sealed record TrackerPushOutcome(
    IReadOnlyList<SchemaPushChange> Changes,
    TrackedStateChangeSet ChangeSet,
    IReadOnlyList<TrackedEntity> ResultEntities,
    IReadOnlyList<EntityDependency> ResultDependencies,
    TrackerSchemaLink Link);

/// <summary>
/// Compares a schema, a linked Tracker and the Tracker's baseline, entity by entity through the links by ID,
/// and lists each change the schema made since the baseline that the Tracker doesn't have yet. Every change
/// is decided per Tracker: Use schema applies it, Keep tracker declines it until the schema changes the same
/// thing again, and an undecided change is left for a later push. A change only the Tracker made is never
/// offered. Statuses, notes, developers, priorities, due dates and Filter active are never touched.
/// </summary>
public static class SchemaPushPlanner
{
    /// <summary>Every change undecided: what a review lists.</summary>
    public static readonly Func<SchemaPushChange, SchemaPushChoice?> Undecided = static _ => null;

    /// <summary>Every change taken from the schema: a Tracker created from it.</summary>
    public static readonly Func<SchemaPushChange, SchemaPushChoice?> UseSchema = static _ => SchemaPushChoice.Schema;

    /// <param name="choose">The choice for each change: undecided while reviewing, or the review's choices when
    /// applying.</param>
    public static TrackerPushOutcome Plan(
        SchemaContent schema,
        Tracker tracker,
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<EntityDependency> dependencies,
        TrackerSchemaLink? link,
        Func<SchemaPushChange, SchemaPushChoice?> choose)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(choose);
        return new Planning(schema, tracker, entities, dependencies, link, choose).Run();
    }

    /// <summary>The choices of a review, for applying it: a change the review didn't list means it is stale.</summary>
    public static Func<SchemaPushChange, SchemaPushChoice?> ChoicesOf(IReadOnlyList<SchemaPushChange> reviewed) => change =>
        (reviewed.FirstOrDefault(item => item.TrackerId == change.TrackerId && item.Key == change.Key) ??
         throw new InvalidOperationException("The schema or a tracker changed after this review opened. Open a new review."))
        .Choice;

    /// <summary>The schema's changes since a baseline, described for the schema editor.</summary>
    public static IReadOnlyList<string> DescribeChanges(SchemaContent schema, TrackerSchemaLink link)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(link);
        Dictionary<SchemaEntityId, SchemaEntity> now = schema.Entities.ToDictionary(static e => e.Id);
        Dictionary<SchemaEntityId, SchemaBaselineEntity> then = link.Entities.ToDictionary(static e => e.SchemaEntityId);
        ILookup<SchemaEntityId, SchemaEntityId> nowDependencies = schema.Dependencies.ToLookup(
            static d => d.DependentEntityId, static d => d.DependencyEntityId);
        string NameOf(SchemaEntityId id) => now.TryGetValue(id, out SchemaEntity? e) ? e.Name
            : then.TryGetValue(id, out SchemaBaselineEntity? b) ? b.Name : "an entity";

        List<string> changes = [];
        foreach (SchemaEntity entity in schema.Entities.OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!then.TryGetValue(entity.Id, out SchemaBaselineEntity? old))
            {
                changes.Add($"Added {entity.Name}");
                continue;
            }
            if (!string.Equals(old.Name, entity.Name, StringComparison.Ordinal))
                changes.Add($"Renamed {old.Name} to {entity.Name}");
            if (!string.Equals(old.GroupName, entity.GroupName, StringComparison.Ordinal))
                changes.Add($"Moved {entity.Name} to {GroupText(entity.GroupName)}");
            // A declined addition holds no dependencies of its own.
            if (old.EntityId is null) continue;
            HashSet<SchemaEntityId> was = old.Dependencies.ToHashSet();
            HashSet<SchemaEntityId> @is = nowDependencies[entity.Id].ToHashSet();
            foreach (SchemaEntityId added in @is.Except(was))
                changes.Add($"{entity.Name} now depends on {NameOf(added)}");
            foreach (SchemaEntityId removed in was.Except(@is).Where(now.ContainsKey))
                changes.Add($"{entity.Name} no longer depends on {NameOf(removed)}");
        }
        foreach (SchemaBaselineEntity removed in link.Entities.Where(e => !now.ContainsKey(e.SchemaEntityId))
                     .OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase))
            changes.Add($"Removed {removed.Name}");
        return changes;
    }

    internal static string GroupText(string group) => group.Length == 0 ? "no group" : $"group {group}";

    /// <summary>One schema entity in the Tracker's baseline as the push leaves it.</summary>
    private sealed class BaselineEntry(EntityId? entityId, string name, string group, IEnumerable<SchemaEntityId> dependencies)
    {
        public EntityId? EntityId { get; } = entityId;
        public string Name { get; set; } = name;
        public string Group { get; set; } = group;
        public HashSet<SchemaEntityId> Dependencies { get; } = dependencies.ToHashSet();
    }

    private sealed class Planning
    {
        private readonly SchemaContent _schema;
        private readonly Tracker _tracker;
        private readonly IReadOnlyList<EntityDependency> _dependencies;
        private readonly Func<SchemaPushChange, SchemaPushChoice?> _choose;
        private readonly Dictionary<SchemaEntityId, SchemaEntity> _schemaEntities;
        private readonly ILookup<SchemaEntityId, SchemaEntityId> _schemaDependencies;
        private readonly Dictionary<EntityId, TrackedEntity> _byId;
        private readonly Dictionary<EntitySourceKey, TrackedEntity> _byKey;
        private readonly Dictionary<SchemaEntityId, SchemaBaselineEntity> _old = [];
        private readonly Dictionary<SchemaEntityId, (SchemaBaselineEntity Old, TrackedEntity Entity)> _linked = [];
        private readonly Dictionary<SchemaEntityId, TrackedEntity> _targets = [];
        private readonly Dictionary<SchemaEntityId, BaselineEntry> _baseline = [];
        // Entities linked by name in this push: the Tracker's own values stand in for the missing baseline.
        private readonly HashSet<SchemaEntityId> _linkedByName = [];
        private readonly HashSet<EntityId> _created = [];
        private readonly List<SchemaPushChange> _changes = [];
        private readonly List<TrackedEntity> _additions = [];
        private readonly List<TrackedEntity> _renames = [];
        private readonly List<TrackedEntity> _regrouped = [];
        private readonly List<EntityId> _archives = [];
        private readonly Dictionary<EntityId, HashSet<EntityId>> _reconciled = [];
        private readonly Dictionary<EntityId, EntityId> _replacedPlaceholders = [];

        public Planning(SchemaContent schema, Tracker tracker, IReadOnlyList<TrackedEntity> entities,
            IReadOnlyList<EntityDependency> dependencies, TrackerSchemaLink? link,
            Func<SchemaPushChange, SchemaPushChoice?> choose)
        {
            _schema = schema;
            _tracker = tracker;
            _dependencies = dependencies;
            _choose = choose;
            _schemaEntities = schema.Entities.ToDictionary(static e => e.Id);
            _schemaDependencies = schema.Dependencies.ToLookup(static d => d.DependentEntityId,
                static d => d.DependencyEntityId);
            _byId = entities.ToDictionary(static e => e.Id);
            _byKey = entities.ToDictionary(static e => EntitySourceKey.From(e.SourceName));
            foreach (SchemaBaselineEntity old in link?.Entities ?? [])
            {
                _old[old.SchemaEntityId] = old;
                // A link counts while its entity is still a real entity of the Tracker.
                if (old.EntityId is { } id && _byId.TryGetValue(id, out TrackedEntity? entity) && !entity.IsPlaceholder)
                    _linked[old.SchemaEntityId] = (old, entity);
            }
        }

        public TrackerPushOutcome Run()
        {
            foreach ((SchemaEntityId id, (SchemaBaselineEntity old, TrackedEntity entity)) in Ordered(_linked))
                if (_schemaEntities.TryGetValue(id, out SchemaEntity? now)) UpdateLinked(old, now, entity);
            foreach (SchemaEntity added in _schema.Entities
                         .Where(e => !_linked.ContainsKey(e.Id))
                         .OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase))
                AddOrLink(added);
            // Last, so an entity the schema removed and added again under the same name stays, linked to
            // the new schema entity.
            foreach ((SchemaEntityId id, (SchemaBaselineEntity old, TrackedEntity entity)) in Ordered(_linked))
                if (!_schemaEntities.ContainsKey(id) && !_targets.Values.Contains(entity)) Removed(old, entity);
            foreach (SchemaEntity entity in _schema.Entities.OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase))
                if (_targets.ContainsKey(entity.Id)) ReconcileDependencies(entity);
            RedirectReplacedPlaceholders();
            return Outcome();
        }

        private void UpdateLinked(SchemaBaselineEntity old, SchemaEntity now, TrackedEntity entity)
        {
            _targets[now.Id] = entity;
            BaselineEntry entry = new(entity.Id, old.Name, old.GroupName, old.Dependencies);
            _baseline[now.Id] = entry;
            entry.Name = SettleName(now, old.Name, entity) ?? entry.Name;
            entry.Group = SettleGroup(now, old.GroupName, entity) ?? entry.Group;
        }

        /// <summary>Offers the schema's name if it changed it; returns the name the baseline takes, or null to keep it.</summary>
        private string? SettleName(SchemaEntity now, string old, TrackedEntity entity)
        {
            if (string.Equals(old, now.Name, StringComparison.Ordinal) ||
                string.Equals(entity.SourceName, now.Name, StringComparison.Ordinal))
                return now.Name;
            bool conflict = !string.Equals(entity.SourceName, old, StringComparison.Ordinal);
            string current = entity.SourceName;
            SchemaPushChoice? choice = Decide(now.Id, null, SchemaPushChangeKind.Name, current, conflict,
                conflict
                    ? $"The schema renamed {old} to {now.Name}; this tracker calls it {current}."
                    : $"The schema renamed {current} to {now.Name}.",
                $"Rename it {now.Name}", $"Keep {current}");
            if (choice == SchemaPushChoice.Schema) Rename(entity, now.Name);
            return choice is null ? null : now.Name;
        }

        /// <summary>Offers the schema's group if it changed it; returns the group the baseline takes, or null to keep it.</summary>
        private string? SettleGroup(SchemaEntity now, string old, TrackedEntity entity)
        {
            if (string.Equals(old, now.GroupName, StringComparison.Ordinal) ||
                string.Equals(entity.GroupName, now.GroupName, StringComparison.Ordinal))
                return now.GroupName;
            bool conflict = !string.Equals(entity.GroupName, old, StringComparison.Ordinal);
            SchemaPushChoice? choice = Decide(now.Id, null, SchemaPushChangeKind.Group, entity.SourceName, conflict,
                conflict
                    ? $"The schema has {entity.SourceName} in {GroupText(now.GroupName)}; this tracker has it in {GroupText(entity.GroupName)}."
                    : $"The schema moved {entity.SourceName} to {GroupText(now.GroupName)}.",
                $"Use {GroupText(now.GroupName)}", $"Keep {GroupText(entity.GroupName)}");
            if (choice == SchemaPushChoice.Schema) Regroup(entity, now.GroupName);
            return choice is null ? null : now.GroupName;
        }

        private void Removed(SchemaBaselineEntity old, TrackedEntity entity)
        {
            if (entity.LifecycleState != EntityLifecycleState.Active) return;
            HashSet<EntityId> was = old.Dependencies
                .Select(id => _linked.TryGetValue(id, out var target) ? target.Entity.Id : null)
                .OfType<EntityId>().ToHashSet();
            HashSet<EntityId> linkedIds = _linked.Values.Select(static item => item.Entity.Id).ToHashSet();
            HashSet<EntityId> @is = DependenciesOf(entity.Id).Where(linkedIds.Contains).ToHashSet();
            bool changed = !string.Equals(entity.SourceName, old.Name, StringComparison.Ordinal) ||
                           !string.Equals(entity.GroupName, old.GroupName, StringComparison.Ordinal) ||
                           !was.SetEquals(@is);
            SchemaPushChoice? choice = Decide(old.SchemaEntityId, null, SchemaPushChangeKind.Removed, entity.SourceName,
                changed,
                changed
                    ? $"The schema removed {old.Name}, but this tracker changed {entity.SourceName} since the last push."
                    : $"The schema removed {old.Name}.",
                $"Archive {entity.SourceName}", "Keep it in this tracker only");
            if (choice == SchemaPushChoice.Schema) _archives.Add(entity.Id);
            // Undecided, it stays linked as it was, so the removal is offered again.
            if (choice is null)
                _baseline[old.SchemaEntityId] = new BaselineEntry(old.EntityId, old.Name, old.GroupName, old.Dependencies);
        }

        private void AddOrLink(SchemaEntity added)
        {
            // An addition this Tracker declined stays declined until the schema changes the entity.
            if (_old.TryGetValue(added.Id, out SchemaBaselineEntity? declined) && declined.EntityId is null &&
                string.Equals(declined.Name, added.Name, StringComparison.Ordinal) &&
                string.Equals(declined.GroupName, added.GroupName, StringComparison.Ordinal))
            {
                _baseline[added.Id] = new BaselineEntry(null, declined.Name, declined.GroupName, []);
                return;
            }

            EntitySourceKey key = EntitySourceKey.From(added.Name);
            _byKey.TryGetValue(key, out TrackedEntity? existing);
            if (existing is { IsPlaceholder: false } &&
                _targets.Values.Any(target => target.Id == existing.Id))
                throw new InvalidOperationException(
                    $"{_tracker.Name} already has an entity called {existing.SourceName}, linked to another " +
                    $"schema entity, so {added.Name} can't be added to it. Rename one of them first.");

            if (existing is { IsPlaceholder: false })
            {
                // An entity the Tracker already has, but not from the schema, can be linked to it. Its own name,
                // group and dependencies become its baseline, so the next push offers any difference.
                SchemaPushChoice? link = Decide(added.Id, null, SchemaPushChangeKind.Linked, existing.SourceName, false,
                    (existing.LifecycleState == EntityLifecycleState.Active
                        ? $"The schema added {added.Name}, and this tracker already has {existing.SourceName}."
                        : $"The schema added {added.Name}, and this tracker has {existing.SourceName} archived.") +
                    " Once it is linked, the next push offers any difference.",
                    $"Link {existing.SourceName} to the schema", "Leave it unlinked");
                if (link == SchemaPushChoice.Tracker) Decline(added);
                if (link != SchemaPushChoice.Schema) return;
                _targets[added.Id] = existing;
                _linkedByName.Add(added.Id);
                _baseline[added.Id] = new BaselineEntry(existing.Id, existing.SourceName, existing.GroupName, []);
                return;
            }

            SchemaPushChoice? choice = Decide(added.Id, null, SchemaPushChangeKind.Added, added.Name, false,
                _schemaDependencies[added.Id].Any()
                    ? $"The schema added {added.Name} ({GroupText(added.GroupName)}), which depends on " +
                      $"{string.Join(", ", _schemaDependencies[added.Id].Select(NameOf).Order(StringComparer.OrdinalIgnoreCase))}."
                    : $"The schema added {added.Name} ({GroupText(added.GroupName)}).",
                $"Add {added.Name}", "Leave it out");
            if (choice == SchemaPushChoice.Tracker) Decline(added);
            if (choice != SchemaPushChoice.Schema) return;

            // A placeholder with the name becomes the entity, keeping its ID, so what depended on the
            // name depends on the entity.
            TrackedEntity entity = new(existing?.Id ?? EntityId.New(), _tracker.Id, added.Name,
                groupName: added.GroupName);
            _byKey[key] = entity;
            _byId[entity.Id] = entity;
            _additions.Add(entity);
            _created.Add(entity.Id);
            _targets[added.Id] = entity;
            _baseline[added.Id] = new BaselineEntry(entity.Id, added.Name, added.GroupName, []);
        }

        /// <summary>Remembers a declined addition in the baseline, without an entity.</summary>
        private void Decline(SchemaEntity added) =>
            _baseline[added.Id] = new BaselineEntry(null, added.Name, added.GroupName, []);

        private void ReconcileDependencies(SchemaEntity schemaEntity)
        {
            TrackedEntity owner = _targets[schemaEntity.Id];
            BaselineEntry entry = _baseline[schemaEntity.Id];
            HashSet<SchemaEntityId> now = _schemaDependencies[schemaEntity.Id].ToHashSet();
            if (_created.Contains(owner.Id))
            {
                // A new entity comes with the schema's dependencies on the entities this Tracker has from it.
                foreach (SchemaEntityId dependency in now)
                    if (_targets.TryGetValue(dependency, out TrackedEntity? target))
                    {
                        Desired(owner.Id).Add(target.Id);
                        entry.Dependencies.Add(dependency);
                    }
                return;
            }

            HashSet<EntityId> current = DependenciesOf(owner.Id).ToHashSet();
            if (_linkedByName.Contains(schemaEntity.Id))
            {
                // Linked by name: what it depends on among the schema's entities becomes its baseline.
                entry.Dependencies.UnionWith(_targets.Where(pair => current.Contains(pair.Value.Id))
                    .Select(static pair => pair.Key));
                return;
            }
            HashSet<SchemaEntityId> was = _linked[schemaEntity.Id].Old.Dependencies.ToHashSet();
            entry.Dependencies.Clear();
            entry.Dependencies.UnionWith(was);
            foreach (SchemaEntityId dependency in now.Union(was).OrderBy(NameOf, StringComparer.OrdinalIgnoreCase))
            {
                bool has = now.Contains(dependency);
                if (was.Contains(dependency) == has) continue;
                TrackedEntity? target = _targets.GetValueOrDefault(dependency) ??
                    (_linked.TryGetValue(dependency, out var old) ? old.Entity : null);
                if (target is null) continue;
                if (current.Contains(target.Id) == has)
                {
                    Settle(entry, dependency, has);
                    continue;
                }
                SchemaPushChoice? choice = Decide(schemaEntity.Id, dependency,
                    has ? SchemaPushChangeKind.DependencyAdded : SchemaPushChangeKind.DependencyRemoved,
                    owner.SourceName, false,
                    has
                        ? $"In the schema, {owner.SourceName} depends on {target.SourceName}; in this tracker it doesn't."
                        : $"In the schema, {owner.SourceName} no longer depends on {target.SourceName}.",
                    has ? "Add the dependency" : "Remove the dependency",
                    has ? "Leave it out" : "Keep the dependency");
                if (choice == SchemaPushChoice.Schema)
                {
                    if (has) Desired(owner.Id).Add(target.Id);
                    else Desired(owner.Id).Remove(target.Id);
                }
                if (choice is not null) Settle(entry, dependency, has);
            }

            static void Settle(BaselineEntry entry, SchemaEntityId dependency, bool has)
            {
                if (has) entry.Dependencies.Add(dependency);
                else entry.Dependencies.Remove(dependency);
            }
        }

        /// <summary>What depended on a placeholder an entity was renamed onto depends on the entity.</summary>
        private void RedirectReplacedPlaceholders()
        {
            foreach ((EntityId placeholder, EntityId entity) in _replacedPlaceholders)
            foreach (EntityId owner in _dependencies.Where(d => d.DependencyEntityId == placeholder)
                         .Select(static d => d.DependentEntityId).Distinct().ToArray())
            {
                HashSet<EntityId> desired = Desired(owner);
                desired.Remove(placeholder);
                desired.Add(entity);
            }
        }

        private TrackerPushOutcome Outcome()
        {
            string? duplicate = _byId.Values.Where(e => !e.IsPlaceholder && !_replacedPlaceholders.ContainsKey(e.Id))
                .GroupBy(static e => EntitySourceKey.From(e.SourceName))
                .FirstOrDefault(static group => group.Count() > 1)?.First().SourceName;
            if (duplicate is not null)
                throw new InvalidOperationException(
                    $"Two entities would be called {duplicate} in {_tracker.Name}. Keep the tracker's name for one of them.");

            HashSet<EntityId> archived = _archives.ToHashSet();
            TrackedEntity[] result = _byId.Values
                .Where(e => !_replacedPlaceholders.ContainsKey(e.Id))
                .Select(e => archived.Contains(e.Id) ? Copy(e, EntityLifecycleState.Archived) : e)
                .ToArray();
            EntityDependency[] desired = _reconciled
                .SelectMany(pair => pair.Value.Select(target => new EntityDependency(pair.Key, target)))
                .ToArray();
            EntityDependency[] resultDependencies = _dependencies
                .Where(d => !_reconciled.ContainsKey(d.DependentEntityId))
                .Concat(desired).ToArray();
            TrackedStateChangeSet changes = new(_additions, _renames, _archives,
                _reconciled.Keys.ToArray(), desired,
                entitiesWithGroupNameToUpdate: _regrouped);
            // A removed entity stays in the baseline while an undecided dependency still refers to it, so that
            // dependency is offered again.
            foreach (SchemaEntityId referenced in _baseline.Values.SelectMany(static entry => entry.Dependencies)
                         .Where(id => !_baseline.ContainsKey(id)).Distinct().ToArray())
                if (_old.TryGetValue(referenced, out SchemaBaselineEntity? kept))
                    _baseline[referenced] = new BaselineEntry(kept.EntityId, kept.Name, kept.GroupName, []);
            TrackerSchemaLink link = new(_tracker.Id, _schema.Schema.Id, _baseline
                .OrderBy(static pair => pair.Key.Value)
                .Select(pair => new SchemaBaselineEntity(pair.Key, pair.Value.EntityId, pair.Value.Name,
                    pair.Value.Group, pair.Value.Dependencies
                        .Where(d => d != pair.Key && _baseline.ContainsKey(d))
                        .OrderBy(static d => d.Value).ToArray()))
                .ToArray());
            return new TrackerPushOutcome(_changes, changes, result, resultDependencies, link);
        }

        /// <summary>Lists a change, and returns the choice made for it: null while it is undecided.</summary>
        private SchemaPushChoice? Decide(SchemaEntityId id, SchemaEntityId? dependency, SchemaPushChangeKind kind,
            string entityName, bool conflict, string description, string useSchema, string keepTracker)
        {
            SchemaPushChange change = new(_tracker.Id, _tracker.Name, id, dependency, kind, entityName, conflict,
                description, useSchema, keepTracker);
            _changes.Add(change);
            change.Choice = _choose(change);
            return change.Choice;
        }

        private void Rename(TrackedEntity entity, string name)
        {
            EntitySourceKey oldKey = EntitySourceKey.From(entity.SourceName);
            EntitySourceKey key = EntitySourceKey.From(name);
            if (_byKey.GetValueOrDefault(oldKey) == entity) _byKey.Remove(oldKey);
            if (_byKey.TryGetValue(key, out TrackedEntity? holder) && holder != entity && holder.IsPlaceholder)
                _replacedPlaceholders[holder.Id] = entity.Id;
            entity.ChangeSourceName(name);
            _byKey[key] = entity;
            if (!_created.Contains(entity.Id) && !_renames.Contains(entity)) _renames.Add(entity);
        }

        private void Regroup(TrackedEntity entity, string group)
        {
            entity.ChangeGroupName(group);
            if (!_created.Contains(entity.Id) && !_regrouped.Contains(entity)) _regrouped.Add(entity);
        }

        private HashSet<EntityId> Desired(EntityId owner)
        {
            if (!_reconciled.TryGetValue(owner, out HashSet<EntityId>? desired))
                _reconciled[owner] = desired = DependenciesOf(owner).ToHashSet();
            return desired;
        }

        private IEnumerable<EntityId> DependenciesOf(EntityId owner) => _dependencies
            .Where(d => d.DependentEntityId == owner).Select(static d => d.DependencyEntityId);

        private string NameOf(SchemaEntityId id) => _schemaEntities.TryGetValue(id, out SchemaEntity? entity)
            ? entity.Name
            : _old.TryGetValue(id, out SchemaBaselineEntity? old) ? old.Name : "";

        private static IEnumerable<KeyValuePair<SchemaEntityId, (SchemaBaselineEntity Old, TrackedEntity Entity)>> Ordered(
            Dictionary<SchemaEntityId, (SchemaBaselineEntity Old, TrackedEntity Entity)> linked) =>
            linked.OrderBy(static pair => pair.Value.Entity.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static pair => pair.Key.Value);

        private static TrackedEntity Copy(TrackedEntity entity, EntityLifecycleState lifecycle) => new(
            entity.Id, entity.TrackerId, entity.SourceName, entity.Status, entity.Notes, lifecycle,
            entity.RequestedPriority, entity.ResponsibleDeveloper, entity.GroupName, entity.FilterActive,
            entity.SharedNotes, entity.DueDate);
    }
}
