using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

/// <summary>
/// Resolves named dependency declarations against one candidate active entity set.
/// </summary>
internal static class DependencyStateResolver
{
    public static Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
        BuildCurrentDeclarations(
            IEnumerable<PersistedDependency> resolvedDependencies,
            IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies,
            IReadOnlyDictionary<EntityId, TrackedEntity> entitiesById)
    {
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> result = [];

        foreach (PersistedDependency dependency in resolvedDependencies)
        {
            if (!entitiesById.TryGetValue(
                    dependency.Edge.DependencyEntityId,
                    out TrackedEntity? target))
            {
                continue;
            }

            AddDeclaration(
                result,
                dependency.Edge.DependentEntityId,
                new DependencyDeclaration(
                    EntitySourceKey.From(target.SourceName),
                    target.SourceName,
                    dependency.Kind,
                    target.Id));
        }

        foreach (PersistedUnresolvedDependency dependency in unresolvedDependencies)
        {
            AddDeclaration(
                result,
                dependency.Dependency.DependentEntityId,
                new DependencyDeclaration(
                    EntitySourceKey.From(dependency.Dependency.DependencySourceName),
                    dependency.Dependency.DependencySourceName,
                    dependency.Kind,
                    null));
        }

        return result;
    }

    public static void Resolve(
        IDictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> declarations,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> activeEntitiesByKey)
    {
        foreach (Dictionary<EntitySourceKey, DependencyDeclaration> ownerDeclarations
                 in declarations.Values)
        {
            foreach (EntitySourceKey key in ownerDeclarations.Keys.ToArray())
            {
                DependencyDeclaration declaration = ownerDeclarations[key];
                ownerDeclarations[key] = activeEntitiesByKey.TryGetValue(
                    declaration.TargetKey,
                    out TrackedEntity? target)
                    ? declaration with
                    {
                        TargetName = target.SourceName,
                        ResolvedTargetId = target.Id
                    }
                    : declaration with { ResolvedTargetId = null };
            }
        }
    }

    public static PersistedDependency[] CreateResolvedDependencies(
        IReadOnlyDictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            declarations) =>
        declarations
            .SelectMany(static item => item.Value.Values.Select(declaration =>
                (OwnerId: item.Key, Declaration: declaration)))
            .Where(static item => item.Declaration.ResolvedTargetId is not null)
            .Select(static item => new PersistedDependency(
                new DependencyEdge(item.OwnerId, item.Declaration.ResolvedTargetId!),
                item.Declaration.Kind))
            .ToArray();

    public static PersistedUnresolvedDependency[] CreateUnresolvedDependencies(
        IReadOnlyDictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            declarations) =>
        declarations
            .SelectMany(static item => item.Value.Values.Select(declaration =>
                (OwnerId: item.Key, Declaration: declaration)))
            .Where(static item => item.Declaration.ResolvedTargetId is null)
            .Select(static item => new PersistedUnresolvedDependency(
                new UnresolvedDependency(item.OwnerId, item.Declaration.TargetName),
                item.Declaration.Kind))
            .ToArray();

    public static void AddDeclaration(
        IDictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> declarations,
        EntityId ownerId,
        DependencyDeclaration declaration)
    {
        if (!declarations.TryGetValue(
                ownerId,
                out Dictionary<EntitySourceKey, DependencyDeclaration>? ownerDeclarations))
        {
            ownerDeclarations = [];
            declarations.Add(ownerId, ownerDeclarations);
        }

        ownerDeclarations[declaration.TargetKey] = declaration;
    }
}

internal sealed record DependencyDeclaration(
    EntitySourceKey TargetKey,
    string TargetName,
    ImportedDependencyKind Kind,
    EntityId? ResolvedTargetId);
