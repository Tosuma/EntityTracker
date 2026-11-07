using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>
/// An entity as the schema editor saves it: its name, group and the names of the entities it depends
/// on. <see cref="EntityId"/> is null for a new entity.
/// </summary>
public sealed record SchemaEntityEdit(
    SchemaEntityId? EntityId,
    string Name,
    string GroupName,
    IReadOnlyList<string> DependencyNames);

/// <summary>
/// Edits a schema's entities and dependencies. A schema never holds an unresolved name: a dependency on
/// a name the schema doesn't have yet adds that entity to the schema. Dependencies may not form a cycle.
/// </summary>
public sealed class SchemaEditingService(ISchemaStore store)
{
    public async Task<SchemaContent> GetContentAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        return await store.GetContentAsync(schemaId, cancellationToken)
            ?? throw new InvalidOperationException("The schema no longer exists.");
    }

    /// <summary>The entities that depend on <paramref name="entityId"/>, by name.</summary>
    public static IReadOnlyList<SchemaEntity> Dependants(SchemaContent content, SchemaEntityId entityId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(entityId);
        HashSet<SchemaEntityId> dependants = content.Dependencies
            .Where(dependency => dependency.DependencyEntityId == entityId)
            .Select(static dependency => dependency.DependentEntityId)
            .ToHashSet();
        return content.Entities.Where(entity => dependants.Contains(entity.Id))
            .OrderBy(static entity => entity.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<SchemaEditResult> SaveEntityAsync(
        SchemaId schemaId,
        SchemaEntityEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        SchemaContent content = await GetContentAsync(schemaId, cancellationToken);
        if (content.Schema.LifecycleState != CatalogLifecycleState.Active)
            return SchemaEditResult.Failed("The schema is in the recycle bin. Restore it to edit it.");

        string name = edit.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return SchemaEditResult.Failed("Enter an entity name.");

        Dictionary<SchemaEntityId, SchemaEntity> byId = content.Entities.ToDictionary(static entity => entity.Id);
        SchemaEntity? existing = null;
        if (edit.EntityId is { } entityId && !byId.TryGetValue(entityId, out existing))
            return SchemaEditResult.Failed("The entity is no longer in the schema.");

        EntitySourceKey key = EntitySourceKey.From(name);
        Dictionary<EntitySourceKey, SchemaEntity> byKey = content.Entities
            .ToDictionary(static entity => EntitySourceKey.From(entity.Name));
        if (byKey.TryGetValue(key, out SchemaEntity? sameName) && sameName.Id != existing?.Id)
            return SchemaEditResult.Failed($"Another entity in this schema is already called {sameName.Name}.");

        SchemaEntity saved = new(existing?.Id ?? SchemaEntityId.New(), schemaId, name, edit.GroupName);

        // Each dependency name is an existing entity, or a new one added to the schema.
        List<SchemaEntity> created = [];
        HashSet<SchemaEntityId> targets = [];
        foreach (string rawName in edit.DependencyNames ?? [])
        {
            string dependencyName = rawName?.Trim() ?? string.Empty;
            if (dependencyName.Length == 0) continue;
            EntitySourceKey dependencyKey = EntitySourceKey.From(dependencyName);
            if (dependencyKey == key)
                return SchemaEditResult.Failed("An entity cannot depend on itself.");
            SchemaEntity? target = byKey.GetValueOrDefault(dependencyKey)
                ?? created.FirstOrDefault(entity => EntitySourceKey.From(entity.Name) == dependencyKey);
            if (target is null)
            {
                target = new SchemaEntity(SchemaEntityId.New(), schemaId, dependencyName);
                created.Add(target);
            }

            targets.Add(target.Id);
        }

        SchemaDependency[] current = content.Dependencies
            .Where(dependency => dependency.DependentEntityId == saved.Id).ToArray();
        SchemaDependency[] wanted = targets.Select(target => new SchemaDependency(saved.Id, target)).ToArray();
        SchemaDependency[] added = wanted.Except(current).ToArray();
        SchemaDependency[] removed = current.Except(wanted).ToArray();

        // A cycle can only be closed by a dependency this edit adds: its target must not already lead back.
        Dictionary<SchemaEntityId, SchemaEntity> names = new(byId);
        foreach (SchemaEntity entity in created) names[entity.Id] = entity;
        names[saved.Id] = saved;
        ILookup<SchemaEntityId, SchemaEntityId> graph = content.Dependencies
            .Where(dependency => dependency.DependentEntityId != saved.Id)
            .Concat(wanted)
            .ToLookup(static dependency => dependency.DependentEntityId, static dependency => dependency.DependencyEntityId);
        foreach (SchemaDependency dependency in added)
        {
            if (Reaches(graph, dependency.DependencyEntityId, saved.Id))
            {
                string target = names[dependency.DependencyEntityId].Name;
                return SchemaEditResult.Failed(
                    $"{saved.Name} can't depend on {target}, because {target} already depends on {saved.Name}, " +
                    "directly or through other entities.");
            }
        }

        await store.ApplyAsync(schemaId,
            new SchemaChangeSet([saved, .. created], [], added, removed), cancellationToken);
        return new SchemaEditResult(true, null, saved, created);
    }

    /// <summary>Removes an entity, and every dependency on or of it.</summary>
    public async Task RemoveEntityAsync(
        SchemaId schemaId,
        SchemaEntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        SchemaContent content = await GetContentAsync(schemaId, cancellationToken);
        if (content.Schema.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The schema is in the recycle bin.");
        if (content.Entities.All(entity => entity.Id != entityId))
            return;
        SchemaDependency[] removed = content.Dependencies
            .Where(dependency => dependency.DependentEntityId == entityId || dependency.DependencyEntityId == entityId)
            .ToArray();
        await store.ApplyAsync(schemaId, new SchemaChangeSet([], [entityId], [], removed), cancellationToken);
    }

    private static bool Reaches(
        ILookup<SchemaEntityId, SchemaEntityId> graph,
        SchemaEntityId from,
        SchemaEntityId to)
    {
        HashSet<SchemaEntityId> seen = [];
        Stack<SchemaEntityId> pending = new([from]);
        while (pending.Count > 0)
        {
            SchemaEntityId current = pending.Pop();
            if (current == to) return true;
            if (!seen.Add(current)) continue;
            foreach (SchemaEntityId next in graph[current]) pending.Push(next);
        }

        return false;
    }
}
