using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

/// <summary>
/// Compares an imported schema with persisted state without modifying either one.
/// </summary>
public sealed class SchemaSynchronizationPlanner
{
    private readonly DependencyRanker _dependencyRanker;

    public SchemaSynchronizationPlanner(DependencyRanker dependencyRanker)
    {
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        _dependencyRanker = dependencyRanker;
    }

    public SchemaSynchronizationPlan CreatePlan(
        SchemaImportCandidate importCandidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> persistedEntities,
        IEnumerable<PersistedDependency> persistedDependencies,
        IEnumerable<PersistedUnresolvedDependency> persistedUnresolvedDependencies)
    {
        ArgumentNullException.ThrowIfNull(importCandidate);
        ArgumentNullException.ThrowIfNull(persistedEntities);
        ArgumentNullException.ThrowIfNull(persistedDependencies);
        ArgumentNullException.ThrowIfNull(persistedUnresolvedDependencies);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        TrackedEntity[] currentEntities = persistedEntities.ToArray();
        PersistedDependency[] currentResolved = persistedDependencies.ToArray();
        PersistedUnresolvedDependency[] currentUnresolved =
            persistedUnresolvedDependencies.ToArray();
        Dictionary<EntityId, TrackedEntity> currentById = currentEntities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> currentByKey = currentEntities.ToDictionary(
            static entity => EntitySourceKey.From(entity.SourceName));
        Dictionary<EntitySourceKey, ImportedEntity> importedByKey =
            importCandidate.Entities.ToDictionary(static entity => entity.SourceKey);

        Dictionary<EntitySourceKey, TrackedEntity> candidateActiveByKey = [];
        if (mode == SchemaImportMode.Partial)
        {
            foreach (TrackedEntity entity in currentEntities.Where(
                         static entity => entity.LifecycleState == EntityLifecycleState.Active))
            {
                candidateActiveByKey.Add(EntitySourceKey.From(entity.SourceName), entity);
            }
        }

        foreach (ImportedEntity importedEntity in importCandidate.Entities)
        {
            TrackedEntity candidateEntity = currentByKey.TryGetValue(
                importedEntity.SourceKey,
                out TrackedEntity? existingEntity)
                ? new TrackedEntity(
                    existingEntity.Id,
                    importedEntity.SourceName,
                    existingEntity.Status,
                    existingEntity.Notes,
                    EntityLifecycleState.Active)
                : new TrackedEntity(EntityId.New(), importedEntity.SourceName);
            candidateActiveByKey[importedEntity.SourceKey] = candidateEntity;
        }

        Dictionary<EntityId, Dictionary<EntitySourceKey, Declaration>> currentDeclarations =
            BuildCurrentDeclarations(currentResolved, currentUnresolved, currentById);
        Dictionary<EntityId, Dictionary<EntitySourceKey, Declaration>> candidateDeclarations = [];

        foreach (TrackedEntity candidateEntity in candidateActiveByKey.Values)
        {
            EntitySourceKey ownerKey = EntitySourceKey.From(candidateEntity.SourceName);
            Dictionary<EntitySourceKey, Declaration> declarations;

            if (importedByKey.ContainsKey(ownerKey))
            {
                declarations = BuildImportedDeclarations(
                    ownerKey,
                    importCandidate,
                    importedByKey);
            }
            else
            {
                declarations = currentDeclarations.TryGetValue(
                    candidateEntity.Id,
                    out Dictionary<EntitySourceKey, Declaration>? existingDeclarations)
                    ? existingDeclarations.ToDictionary(static item => item.Key, static item => item.Value)
                    : [];
            }

            candidateDeclarations[candidateEntity.Id] = declarations;
        }

        // Resolve only after Complete/Partial membership is settled. This also re-evaluates
        // declarations retained from owners outside a Partial import.
        ResolveDeclarations(candidateDeclarations, candidateActiveByKey);

        TrackedEntity[] candidateEntities = candidateActiveByKey.Values.ToArray();
        PersistedDependency[] candidateResolved = candidateDeclarations
            .SelectMany(static item => item.Value.Values.Select(declaration =>
                (OwnerId: item.Key, Declaration: declaration)))
            .Where(static item => item.Declaration.ResolvedTargetId is not null)
            .Select(static item => new PersistedDependency(
                new DependencyEdge(item.OwnerId, item.Declaration.ResolvedTargetId!),
                item.Declaration.Kind))
            .ToArray();
        PersistedUnresolvedDependency[] candidateUnresolved = candidateDeclarations
            .SelectMany(static item => item.Value.Values.Select(declaration =>
                (OwnerId: item.Key, Declaration: declaration)))
            .Where(static item => item.Declaration.ResolvedTargetId is null)
            .Select(static item => new PersistedUnresolvedDependency(
                new UnresolvedDependency(item.OwnerId, item.Declaration.TargetName),
                item.Declaration.Kind))
            .ToArray();

        DependencyRankingResult ranking = _dependencyRanker.Rank(
            candidateEntities,
            candidateResolved.Select(static dependency => dependency.Edge),
            candidateUnresolved.Select(static dependency => dependency.Dependency));

        List<EntitySynchronizationChange> newChanges = [];
        List<EntitySynchronizationChange> changedChanges = [];
        List<EntitySynchronizationChange> missingChanges = [];
        List<TrackedEntity> entitiesToAdd = [];
        List<TrackedEntity> entitiesToUpdate = [];
        HashSet<EntityId> reconciledOwnerIds = [];
        int unchangedCount = 0;

        Dictionary<EntityId, UnrankedEntity> unrankedById = ranking.UnrankedEntities
            .ToDictionary(static item => item.EntityId);

        foreach (TrackedEntity candidateEntity in candidateEntities)
        {
            EntitySourceKey sourceKey = EntitySourceKey.From(candidateEntity.SourceName);
            bool isImported = importedByKey.ContainsKey(sourceKey);
            currentById.TryGetValue(candidateEntity.Id, out TrackedEntity? currentEntity);
            Dictionary<EntitySourceKey, Declaration> oldDeclarations =
                currentDeclarations.TryGetValue(
                    candidateEntity.Id,
                    out Dictionary<EntitySourceKey, Declaration>? oldValue)
                    ? oldValue
                    : [];
            Dictionary<EntitySourceKey, Declaration> newDeclarations =
                candidateDeclarations[candidateEntity.Id];
            IReadOnlyList<DependencySynchronizationChange> dependencyChanges =
                CompareDeclarations(oldDeclarations, newDeclarations);

            if (currentEntity is null)
            {
                EntitySynchronizationChange change = CreateEntityChange(
                    candidateEntity,
                    EntitySynchronizationChangeKind.New,
                    dependencyChanges,
                    false,
                    unrankedById);
                newChanges.Add(change);
                entitiesToAdd.Add(candidateEntity);
                reconciledOwnerIds.Add(candidateEntity.Id);
                continue;
            }

            bool isReactivation =
                currentEntity.LifecycleState == EntityLifecycleState.Archived;
            bool metadataChanged = !StringComparer.Ordinal.Equals(
                                       currentEntity.SourceName,
                                       candidateEntity.SourceName) ||
                                   isReactivation;
            if (metadataChanged)
            {
                entitiesToUpdate.Add(candidateEntity);
            }

            if (dependencyChanges.Count > 0)
            {
                reconciledOwnerIds.Add(candidateEntity.Id);
            }

            if (metadataChanged || dependencyChanges.Count > 0)
            {
                changedChanges.Add(CreateEntityChange(
                    candidateEntity,
                    EntitySynchronizationChangeKind.Changed,
                    dependencyChanges,
                    isReactivation,
                    unrankedById));
            }
            else if (isImported)
            {
                unchangedCount++;
            }
        }

        EntityId[] idsToArchive = mode == SchemaImportMode.Complete
            ? currentEntities
                .Where(entity =>
                    entity.LifecycleState == EntityLifecycleState.Active &&
                    !importedByKey.ContainsKey(EntitySourceKey.From(entity.SourceName)))
                .Select(static entity => entity.Id)
                .ToArray()
            : [];

        foreach (EntityId entityId in idsToArchive)
        {
            missingChanges.Add(new EntitySynchronizationChange(
                currentById[entityId],
                EntitySynchronizationChangeKind.Missing));
        }

        HashSet<EntityId> ownerIds = reconciledOwnerIds;
        SchemaSynchronizationChangeSet changeSet = new(
            entitiesToAdd,
            entitiesToUpdate,
            idsToArchive,
            ownerIds,
            candidateResolved.Where(dependency =>
                ownerIds.Contains(dependency.Edge.DependentEntityId)),
            candidateUnresolved.Where(dependency =>
                ownerIds.Contains(dependency.Dependency.DependentEntityId)));

        IReadOnlyList<EntitySynchronizationChange> unresolvedChanges =
            ranking.UnrankedEntities
                .Where(static entity =>
                    entity.State == DependencyResolutionState.Unresolved)
                .Select(entity => CreateEntityChange(
                    candidateActiveByKey.Values.Single(candidate => candidate.Id == entity.EntityId),
                    currentById.ContainsKey(entity.EntityId)
                        ? EntitySynchronizationChangeKind.Changed
                        : EntitySynchronizationChangeKind.New,
                    [],
                    false,
                    unrankedById))
                .OrderBy(static change => change.Entity.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new SchemaSynchronizationPlan(
            mode,
            Sort(newChanges),
            Sort(changedChanges),
            Sort(missingChanges),
            unchangedCount,
            unresolvedChanges,
            ranking,
            changeSet);
    }

    private static Dictionary<EntityId, Dictionary<EntitySourceKey, Declaration>>
        BuildCurrentDeclarations(
            IEnumerable<PersistedDependency> resolvedDependencies,
            IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies,
            IReadOnlyDictionary<EntityId, TrackedEntity> entitiesById)
    {
        Dictionary<EntityId, Dictionary<EntitySourceKey, Declaration>> result = [];

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
                new Declaration(
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
                new Declaration(
                    EntitySourceKey.From(dependency.Dependency.DependencySourceName),
                    dependency.Dependency.DependencySourceName,
                    dependency.Kind,
                    null));
        }

        return result;
    }

    private static Dictionary<EntitySourceKey, Declaration> BuildImportedDeclarations(
        EntitySourceKey ownerKey,
        SchemaImportCandidate importCandidate,
        IReadOnlyDictionary<EntitySourceKey, ImportedEntity> importedByKey)
    {
        Dictionary<EntitySourceKey, Declaration> result = [];

        foreach (ImportedDependency dependency in importCandidate.Dependencies.Where(
                     dependency => dependency.DependentSourceKey == ownerKey))
        {
            ImportedEntity target = importedByKey[dependency.DependencySourceKey];
            result.Add(
                dependency.DependencySourceKey,
                new Declaration(
                    dependency.DependencySourceKey,
                    target.SourceName,
                    dependency.Kind,
                    null));
        }

        foreach (UnresolvedImportedDependency dependency in
                 importCandidate.UnresolvedDependencies.Where(
                     dependency => dependency.DependentSourceKey == ownerKey))
        {
            result.Add(
                dependency.DependencySourceKey,
                new Declaration(
                    dependency.DependencySourceKey,
                    dependency.DependencySourceName,
                    dependency.Kind,
                    null));
        }

        return result;
    }

    private static void ResolveDeclarations(
        IDictionary<EntityId, Dictionary<EntitySourceKey, Declaration>> declarations,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> activeEntitiesByKey)
    {
        foreach (Dictionary<EntitySourceKey, Declaration> ownerDeclarations in declarations.Values)
        {
            foreach (EntitySourceKey key in ownerDeclarations.Keys.ToArray())
            {
                Declaration declaration = ownerDeclarations[key];
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

    private static IReadOnlyList<DependencySynchronizationChange> CompareDeclarations(
        IReadOnlyDictionary<EntitySourceKey, Declaration> current,
        IReadOnlyDictionary<EntitySourceKey, Declaration> candidate)
    {
        List<DependencySynchronizationChange> changes = [];
        IEnumerable<EntitySourceKey> keys = current.Keys
            .Concat(candidate.Keys)
            .Distinct()
            .OrderBy(static key => key.Value, StringComparer.Ordinal);

        foreach (EntitySourceKey key in keys)
        {
            bool hasCurrent = current.TryGetValue(key, out Declaration? oldDeclaration);
            bool hasCandidate = candidate.TryGetValue(key, out Declaration? newDeclaration);
            if (!hasCurrent)
            {
                changes.Add(new DependencySynchronizationChange(
                    newDeclaration!.TargetName,
                    DependencySynchronizationChangeKind.Added,
                    null,
                    newDeclaration.Kind));
                continue;
            }

            if (!hasCandidate)
            {
                changes.Add(new DependencySynchronizationChange(
                    oldDeclaration!.TargetName,
                    DependencySynchronizationChangeKind.Removed,
                    oldDeclaration.Kind,
                    null));
                continue;
            }

            if (oldDeclaration!.Kind != newDeclaration!.Kind)
            {
                changes.Add(new DependencySynchronizationChange(
                    newDeclaration.TargetName,
                    DependencySynchronizationChangeKind.KindChanged,
                    oldDeclaration.Kind,
                    newDeclaration.Kind));
            }

            if (oldDeclaration.ResolvedTargetId is null &&
                newDeclaration.ResolvedTargetId is not null)
            {
                changes.Add(new DependencySynchronizationChange(
                    newDeclaration.TargetName,
                    DependencySynchronizationChangeKind.Resolved,
                    oldDeclaration.Kind,
                    newDeclaration.Kind));
            }
            else if (oldDeclaration.ResolvedTargetId is not null &&
                     newDeclaration.ResolvedTargetId is null)
            {
                changes.Add(new DependencySynchronizationChange(
                    newDeclaration.TargetName,
                    DependencySynchronizationChangeKind.BecameUnresolved,
                    oldDeclaration.Kind,
                    newDeclaration.Kind));
            }
            else if (!StringComparer.Ordinal.Equals(
                         oldDeclaration.TargetName,
                         newDeclaration.TargetName))
            {
                changes.Add(new DependencySynchronizationChange(
                    newDeclaration.TargetName,
                    DependencySynchronizationChangeKind.MetadataChanged,
                    oldDeclaration.Kind,
                    newDeclaration.Kind));
            }
        }

        return changes;
    }

    private static EntitySynchronizationChange CreateEntityChange(
        TrackedEntity entity,
        EntitySynchronizationChangeKind kind,
        IEnumerable<DependencySynchronizationChange> dependencyChanges,
        bool isReactivation,
        IReadOnlyDictionary<EntityId, UnrankedEntity> unrankedById)
    {
        return unrankedById.TryGetValue(entity.Id, out UnrankedEntity? unranked)
            ? new EntitySynchronizationChange(
                entity,
                kind,
                dependencyChanges,
                isReactivation,
                unranked.State,
                unranked.MissingDependencyNames)
            : new EntitySynchronizationChange(
                entity,
                kind,
                dependencyChanges,
                isReactivation);
    }

    private static IEnumerable<EntitySynchronizationChange> Sort(
        IEnumerable<EntitySynchronizationChange> changes) =>
        changes.OrderBy(static change => change.Entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static change => change.Entity.SourceName, StringComparer.Ordinal);

    private static void AddDeclaration(
        IDictionary<EntityId, Dictionary<EntitySourceKey, Declaration>> declarations,
        EntityId ownerId,
        Declaration declaration)
    {
        if (!declarations.TryGetValue(
                ownerId,
                out Dictionary<EntitySourceKey, Declaration>? ownerDeclarations))
        {
            ownerDeclarations = [];
            declarations.Add(ownerId, ownerDeclarations);
        }

        ownerDeclarations[declaration.TargetKey] = declaration;
    }

    private sealed record Declaration(
        EntitySourceKey TargetKey,
        string TargetName,
        ImportedDependencyKind Kind,
        EntityId? ResolvedTargetId);
}
