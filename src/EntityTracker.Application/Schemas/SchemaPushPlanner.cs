using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>What a push does to one Tracker, as a change set and the Tracker's new baseline.</summary>
public sealed record TrackerPushOutcome(
    IReadOnlyList<string> AutomaticChanges,
    IReadOnlyList<SchemaPushConflict> Conflicts,
    TrackedStateChangeSet ChangeSet,
    IReadOnlyList<TrackedEntity> ResultEntities,
    IReadOnlyList<EntityDependency> ResultDependencies,
    TrackerSchemaLink Link);

/// <summary>
/// Compares a schema, a linked Tracker and the Tracker's baseline, entity by entity through the links by
/// ID. A change only the schema made is applied; a change only the Tracker made is kept; where both
/// changed the same thing differently, a conflict asks which to keep. Statuses, notes, developers,
/// priorities, due dates and Filter active are never touched.
/// </summary>
public static class SchemaPushPlanner
{
    /// <param name="choices">The choices made in a review, by conflict; null while reviewing, which
    /// keeps the Tracker's side of every conflict.</param>
    public static TrackerPushOutcome Plan(
        SchemaContent schema,
        Tracker tracker,
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<EntityDependency> dependencies,
        TrackerSchemaLink? link,
        IReadOnlyList<SchemaPushConflict>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(tracker);
        return new Planning(schema, tracker, entities, dependencies, link, choices).Run();
    }

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

    private sealed class Planning
    {
        private readonly SchemaContent _schema;
        private readonly Tracker _tracker;
        private readonly IReadOnlyList<EntityDependency> _dependencies;
        private readonly IReadOnlyList<SchemaPushConflict>? _choices;
        private readonly Dictionary<SchemaEntityId, SchemaEntity> _schemaEntities;
        private readonly ILookup<SchemaEntityId, SchemaEntityId> _schemaDependencies;
        private readonly Dictionary<EntityId, TrackedEntity> _byId;
        private readonly Dictionary<EntitySourceKey, TrackedEntity> _byKey;
        private readonly Dictionary<SchemaEntityId, (SchemaBaselineEntity Old, TrackedEntity Entity)> _linked = [];
        private readonly Dictionary<SchemaEntityId, TrackedEntity> _targets = [];
        private readonly HashSet<SchemaEntityId> _linkedByName = [];
        private readonly HashSet<EntityId> _created = [];
        private readonly List<string> _automatic = [];
        private readonly List<SchemaPushConflict> _conflicts = [];
        private readonly List<TrackedEntity> _additions = [];
        private readonly List<TrackedEntity> _renames = [];
        private readonly List<TrackedEntity> _regrouped = [];
        private readonly List<EntityId> _archives = [];
        private readonly Dictionary<EntityId, HashSet<EntityId>> _reconciled = [];
        private readonly Dictionary<EntityId, EntityId> _replacedPlaceholders = [];

        public Planning(SchemaContent schema, Tracker tracker, IReadOnlyList<TrackedEntity> entities,
            IReadOnlyList<EntityDependency> dependencies, TrackerSchemaLink? link,
            IReadOnlyList<SchemaPushConflict>? choices)
        {
            _schema = schema;
            _tracker = tracker;
            _dependencies = dependencies;
            _choices = choices;
            _schemaEntities = schema.Entities.ToDictionary(static e => e.Id);
            _schemaDependencies = schema.Dependencies.ToLookup(static d => d.DependentEntityId,
                static d => d.DependencyEntityId);
            _byId = entities.ToDictionary(static e => e.Id);
            _byKey = entities.ToDictionary(static e => EntitySourceKey.From(e.SourceName));
            // A link counts while its entity is still a real entity of the Tracker; otherwise the schema
            // entity is new to the Tracker again.
            foreach (SchemaBaselineEntity old in link?.Entities ?? [])
                if (old.EntityId is { } id && _byId.TryGetValue(id, out TrackedEntity? entity) && !entity.IsPlaceholder)
                    _linked[old.SchemaEntityId] = (old, entity);
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
                ReconcileDependencies(entity);
            RedirectReplacedPlaceholders();
            return Outcome();
        }

        private void UpdateLinked(SchemaBaselineEntity old, SchemaEntity now, TrackedEntity entity)
        {
            _targets[now.Id] = entity;
            string currentName = entity.SourceName;
            if (Settle(old.Name, now.Name, entity.SourceName) is { } nameConflict)
            {
                if (nameConflict)
                {
                    if (Decide(now.Id, null, SchemaPushConflictKind.Name, entity.SourceName,
                            $"The schema renamed {old.Name} to {now.Name}; this tracker calls it {entity.SourceName}.",
                            $"Rename it {now.Name}", $"Keep {entity.SourceName}"))
                        Rename(entity, now.Name);
                }
                else
                {
                    _automatic.Add($"Renamed {currentName} to {now.Name}");
                    Rename(entity, now.Name);
                }
            }
            if (Settle(old.GroupName, now.GroupName, entity.GroupName) is { } groupConflict)
            {
                if (groupConflict)
                {
                    if (Decide(now.Id, null, SchemaPushConflictKind.Group, entity.SourceName,
                            $"The schema moved {entity.SourceName} to {GroupText(now.GroupName)}; this tracker has it in {GroupText(entity.GroupName)}.",
                            $"Use {GroupText(now.GroupName)}", $"Keep {GroupText(entity.GroupName)}"))
                        Regroup(entity, now.GroupName);
                }
                else
                {
                    _automatic.Add($"Moved {entity.SourceName} to {GroupText(now.GroupName)}");
                    Regroup(entity, now.GroupName);
                }
            }
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
            if (!changed)
            {
                _automatic.Add($"Archived {entity.SourceName}, which was removed from the schema");
                _archives.Add(entity.Id);
            }
            else if (Decide(old.SchemaEntityId, null, SchemaPushConflictKind.Removed, entity.SourceName,
                         $"The schema removed {old.Name}, but this tracker changed {entity.SourceName} since the last push.",
                         $"Archive {entity.SourceName}", "Keep it in this tracker only"))
                _archives.Add(entity.Id);
        }

        private void AddOrLink(SchemaEntity added)
        {
            EntitySourceKey key = EntitySourceKey.From(added.Name);
            _byKey.TryGetValue(key, out TrackedEntity? existing);
            if (existing is { IsPlaceholder: false } &&
                _targets.Values.Any(target => target.Id == existing.Id))
                throw new InvalidOperationException(
                    $"{_tracker.Name} already has an entity called {existing.SourceName}, linked to another " +
                    $"schema entity, so {added.Name} can't be added to it. Rename one of them first.");

            if (existing is { IsPlaceholder: false })
            {
                // An entity the Tracker already has, but not from the schema, is linked to it. With no
                // baseline, every difference is asked about.
                _targets[added.Id] = existing;
                _linkedByName.Add(added.Id);
                _automatic.Add(existing.LifecycleState == EntityLifecycleState.Active
                    ? $"Linked {existing.SourceName} to the schema's {added.Name}"
                    : $"Linked the archived {existing.SourceName} to the schema's {added.Name}");
                if (!string.Equals(existing.SourceName, added.Name, StringComparison.Ordinal) &&
                    Decide(added.Id, null, SchemaPushConflictKind.Name, existing.SourceName,
                        $"The schema calls {existing.SourceName} {added.Name}.",
                        $"Rename it {added.Name}", $"Keep {existing.SourceName}"))
                    Rename(existing, added.Name);
                if (!string.Equals(existing.GroupName, added.GroupName, StringComparison.Ordinal) &&
                    Decide(added.Id, null, SchemaPushConflictKind.Group, existing.SourceName,
                        $"The schema has {existing.SourceName} in {GroupText(added.GroupName)}; this tracker has it in {GroupText(existing.GroupName)}.",
                        $"Use {GroupText(added.GroupName)}", $"Keep {GroupText(existing.GroupName)}"))
                    Regroup(existing, added.GroupName);
                return;
            }

            // A placeholder with the name becomes the entity, keeping its ID, so what depended on the
            // name depends on the entity.
            TrackedEntity entity = new(existing?.Id ?? EntityId.New(), _tracker.Id, added.Name,
                groupName: added.GroupName);
            _byKey[key] = entity;
            _byId[entity.Id] = entity;
            _additions.Add(entity);
            _created.Add(entity.Id);
            _targets[added.Id] = entity;
            _automatic.Add($"Added {added.Name}");
        }

        private void ReconcileDependencies(SchemaEntity schemaEntity)
        {
            TrackedEntity owner = _targets[schemaEntity.Id];
            HashSet<SchemaEntityId> now = _schemaDependencies[schemaEntity.Id].ToHashSet();
            if (_created.Contains(owner.Id))
            {
                foreach (SchemaEntityId dependency in now)
                    Desired(owner.Id).Add(_targets[dependency].Id);
                return;
            }

            HashSet<EntityId> current = DependenciesOf(owner.Id).ToHashSet();
            if (_linkedByName.Contains(schemaEntity.Id))
            {
                foreach (SchemaEntityId dependency in now.OrderBy(NameOf, StringComparer.OrdinalIgnoreCase))
                {
                    TrackedEntity target = _targets[dependency];
                    if (!current.Contains(target.Id) &&
                        Decide(schemaEntity.Id, dependency, SchemaPushConflictKind.Dependency, owner.SourceName,
                            $"In the schema, {owner.SourceName} depends on {target.SourceName}; in this tracker it doesn't.",
                            "Add the dependency", "Leave it out"))
                        Desired(owner.Id).Add(target.Id);
                }
                foreach ((SchemaEntityId other, TrackedEntity target) in _targets
                             .Where(pair => !now.Contains(pair.Key) && current.Contains(pair.Value.Id))
                             .OrderBy(pair => pair.Value.SourceName, StringComparer.OrdinalIgnoreCase))
                {
                    if (Decide(schemaEntity.Id, other, SchemaPushConflictKind.Dependency, owner.SourceName,
                            $"In this tracker, {owner.SourceName} depends on {target.SourceName}; in the schema it doesn't.",
                            "Remove the dependency", "Keep the dependency"))
                        Desired(owner.Id).Remove(target.Id);
                }
                return;
            }

            // A linked entity: a dependency the schema added or removed since the baseline follows the
            // schema unless the Tracker already has it that way. A yes-or-no can't conflict.
            HashSet<SchemaEntityId> was = _linked[schemaEntity.Id].Old.Dependencies.ToHashSet();
            foreach (SchemaEntityId dependency in now.Union(was).OrderBy(NameOf, StringComparer.OrdinalIgnoreCase))
            {
                bool has = now.Contains(dependency);
                if (was.Contains(dependency) == has) continue;
                TrackedEntity? target = _targets.GetValueOrDefault(dependency) ??
                    (_linked.TryGetValue(dependency, out var old) ? old.Entity : null);
                if (target is null || current.Contains(target.Id) == has) continue;
                if (has) Desired(owner.Id).Add(target.Id);
                else Desired(owner.Id).Remove(target.Id);
                _automatic.Add(has
                    ? $"{owner.SourceName} now depends on {target.SourceName}"
                    : $"{owner.SourceName} no longer depends on {target.SourceName}");
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
            TrackerSchemaLink link = new(_tracker.Id, _schema.Schema.Id, _schema.Entities
                .OrderBy(static e => e.Id.Value)
                .Select(e => new SchemaBaselineEntity(e.Id, _targets[e.Id].Id, e.Name, e.GroupName,
                    _schemaDependencies[e.Id].OrderBy(static d => d.Value).ToArray()))
                .ToArray());
            return new TrackerPushOutcome(_automatic, _conflicts, changes, result, resultDependencies, link);
        }

        /// <summary>
        /// Whether a field needs a change: null when the schema didn't change it or the Tracker already has
        /// the schema's value; otherwise whether the Tracker changed it too.
        /// </summary>
        private static bool? Settle(string old, string now, string tracker)
        {
            if (string.Equals(old, now, StringComparison.Ordinal) ||
                string.Equals(tracker, now, StringComparison.Ordinal))
                return null;
            return !string.Equals(tracker, old, StringComparison.Ordinal);
        }

        /// <summary>Records a conflict, and returns whether the schema's side was chosen.</summary>
        private bool Decide(SchemaEntityId id, SchemaEntityId? dependency, SchemaPushConflictKind kind,
            string entityName, string description, string useSchema, string keepTracker)
        {
            SchemaPushConflict conflict = new(_tracker.Id, _tracker.Name, id, dependency, kind, entityName,
                description, useSchema, keepTracker);
            _conflicts.Add(conflict);
            if (_choices is null) return false;
            SchemaPushConflict chosen = _choices.FirstOrDefault(c => c.TrackerId == _tracker.Id && c.Key == conflict.Key) ??
                throw new InvalidOperationException("The schema or a tracker changed after this review opened. Open a new review.");
            conflict.Choice = chosen.Choice ??
                throw new InvalidOperationException("Choose Use schema or Keep tracker for every difference before pushing.");
            return chosen.Choice == SchemaPushChoice.Schema;
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
            : _linked.TryGetValue(id, out var old) ? old.Old.Name : "";

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
