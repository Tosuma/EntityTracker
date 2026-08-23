using EntityTracker.Application.Dependencies;
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
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;

    public SchemaSynchronizationPlanner(
        DependencyRanker dependencyRanker,
        EffectiveDependencyResolver? effectiveDependencyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        _dependencyRanker = dependencyRanker;
        _effectiveDependencyResolver = effectiveDependencyResolver ?? new EffectiveDependencyResolver();
    }

    public SchemaSynchronizationPlan CreatePlan(
        SchemaImportCandidate importCandidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> persistedEntities,
        IEnumerable<PersistedDependency> persistedDependencies,
        IEnumerable<PersistedUnresolvedDependency> persistedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride>? manualDependencyOverrides = null) =>
        CreatePlanCore(
            importCandidate,
            mode,
            persistedEntities,
            persistedDependencies,
            persistedUnresolvedDependencies,
            manualDependencyOverrides ?? [],
            manualDependencyOverrides ?? [],
            null);

    public SchemaSynchronizationPlan ReviseManualOverrides(
        SchemaSynchronizationPlan plan,
        EntityId ownerId,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(desiredOwnerOverrides);

        ManualDependencyOverride[] revisedOverrides = plan.CandidateManualOverrides
            .Where(item => item.DependentEntityId != ownerId)
            .Concat(desiredOwnerOverrides)
            .ToArray();
        return CreatePlanCore(
            plan.ImportCandidate,
            plan.Mode,
            plan.PersistedEntities,
            plan.PersistedResolvedDependencies,
            plan.PersistedUnresolvedDependencies,
            plan.PersistedManualOverrides,
            revisedOverrides,
            plan.PlannedNewEntityIds);
    }

    private SchemaSynchronizationPlan CreatePlanCore(
        SchemaImportCandidate importCandidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> persistedEntities,
        IEnumerable<PersistedDependency> persistedDependencies,
        IEnumerable<PersistedUnresolvedDependency> persistedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> persistedManualOverrides,
        IEnumerable<ManualDependencyOverride> candidateManualOverrides,
        IReadOnlyDictionary<EntitySourceKey, EntityId>? plannedNewEntityIds)
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
        ManualDependencyOverride[] persistedOverrides = persistedManualOverrides.ToArray();
        ManualDependencyOverride[] candidateOverrides = candidateManualOverrides.ToArray();
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
        else
        {
            // Manual-only entities describe planned schema that CSV has not confirmed yet.
            // Keep them active until either import mode matches them for the first time.
            foreach (TrackedEntity entity in currentEntities.Where(static entity =>
                         entity.LifecycleState == EntityLifecycleState.Active &&
                         entity.Provenance == EntityProvenance.ManualOnly))
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
                    EntityLifecycleState.Active,
                    existingEntity.Provenance == EntityProvenance.ManualOnly
                        ? EntityProvenance.ManualAndImported
                        : existingEntity.Provenance)
                : new TrackedEntity(
                    plannedNewEntityIds is not null &&
                    plannedNewEntityIds.TryGetValue(
                        importedEntity.SourceKey,
                        out EntityId? plannedId)
                        ? plannedId
                        : EntityId.New(),
                    importedEntity.SourceName,
                    provenance: EntityProvenance.Imported);
            candidateActiveByKey[importedEntity.SourceKey] = candidateEntity;
        }

        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            currentImportedDeclarations = DependencyStateResolver.BuildCurrentDeclarations(
                currentResolved,
                currentUnresolved,
                currentById);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            candidateImportedDeclarations = [];

        foreach (TrackedEntity candidateEntity in candidateActiveByKey.Values)
        {
            EntitySourceKey ownerKey = EntitySourceKey.From(candidateEntity.SourceName);
            Dictionary<EntitySourceKey, DependencyDeclaration> declarations;

            if (importedByKey.ContainsKey(ownerKey))
            {
                declarations = BuildImportedDeclarations(
                    ownerKey,
                    importCandidate,
                    importedByKey);
            }
            else
            {
                declarations = currentImportedDeclarations.TryGetValue(
                    candidateEntity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? existingDeclarations)
                    ? existingDeclarations.ToDictionary(static item => item.Key, static item => item.Value)
                    : [];
            }

            candidateImportedDeclarations[candidateEntity.Id] = declarations;
        }

        // Resolve only after Complete/Partial membership is settled. This also re-evaluates
        // declarations retained from owners outside a Partial import.
        DependencyStateResolver.Resolve(candidateImportedDeclarations, candidateActiveByKey);

        TrackedEntity[] candidateEntities = candidateActiveByKey.Values.ToArray();
        PersistedDependency[] candidateImportedResolved =
            DependencyStateResolver.CreateResolvedDependencies(candidateImportedDeclarations);
        PersistedUnresolvedDependency[] candidateImportedUnresolved =
            DependencyStateResolver.CreateUnresolvedDependencies(candidateImportedDeclarations);

        EffectiveDependencyState currentEffective = _effectiveDependencyResolver.Resolve(
            currentEntities,
            currentResolved,
            currentUnresolved,
            persistedOverrides);
        EffectiveDependencyState candidateEffective = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            candidateImportedResolved,
            candidateImportedUnresolved,
            candidateOverrides);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            currentEffectiveDeclarations = DependencyStateResolver.BuildCurrentDeclarations(
                currentEffective.ResolvedDependencies,
                currentEffective.UnresolvedDependencies,
                currentById);
        Dictionary<EntityId, TrackedEntity> candidateById = candidateEntities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            candidateEffectiveDeclarations = DependencyStateResolver.BuildCurrentDeclarations(
                candidateEffective.ResolvedDependencies,
                candidateEffective.UnresolvedDependencies,
                candidateById);

        DependencyRankingResult ranking = _dependencyRanker.Rank(
            candidateEntities,
            candidateEffective.ResolvedDependencies.Select(static dependency => dependency.Edge),
            candidateEffective.UnresolvedDependencies.Select(static dependency => dependency.Dependency));

        List<EntitySynchronizationChange> newChanges = [];
        List<EntitySynchronizationChange> changedChanges = [];
        List<EntitySynchronizationChange> missingChanges = [];
        List<EntitySynchronizationChange> manualOnlyChanges = [];
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
            Dictionary<EntitySourceKey, DependencyDeclaration> oldDeclarations =
                currentEffectiveDeclarations.TryGetValue(
                    candidateEntity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? oldValue)
                    ? oldValue
                    : [];
            Dictionary<EntitySourceKey, DependencyDeclaration> newDeclarations =
                candidateEffectiveDeclarations.TryGetValue(
                    candidateEntity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? effectiveValue)
                    ? effectiveValue
                    : [];
            IReadOnlyList<DependencySynchronizationChange> dependencyChanges =
                CompareDeclarations(oldDeclarations, newDeclarations);
            IReadOnlyList<DependencySynchronizationChange> importedDependencyChanges =
                CompareDeclarations(
                    currentImportedDeclarations.TryGetValue(
                        candidateEntity.Id,
                        out Dictionary<EntitySourceKey, DependencyDeclaration>? oldImported)
                        ? oldImported
                        : [],
                    candidateImportedDeclarations[candidateEntity.Id]);

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
            bool wasFirstObservedInImport =
                currentEntity.Provenance == EntityProvenance.ManualOnly &&
                candidateEntity.Provenance == EntityProvenance.ManualAndImported;
            bool isProtectedManualOnly =
                mode == SchemaImportMode.Complete &&
                currentEntity.Provenance == EntityProvenance.ManualOnly &&
                !isImported;
            bool metadataChanged = !StringComparer.Ordinal.Equals(
                                       currentEntity.SourceName,
                                       candidateEntity.SourceName) ||
                                   isReactivation ||
                                   currentEntity.Provenance != candidateEntity.Provenance;
            if (metadataChanged)
            {
                entitiesToUpdate.Add(candidateEntity);
            }

            if (importedDependencyChanges.Count > 0)
            {
                reconciledOwnerIds.Add(candidateEntity.Id);
            }

            if (isProtectedManualOnly)
            {
                manualOnlyChanges.Add(CreateEntityChange(
                    candidateEntity,
                    EntitySynchronizationChangeKind.Changed,
                    dependencyChanges,
                    false,
                    unrankedById));
            }
            else if (metadataChanged || dependencyChanges.Count > 0)
            {
                changedChanges.Add(CreateEntityChange(
                    candidateEntity,
                    EntitySynchronizationChangeKind.Changed,
                    dependencyChanges,
                    isReactivation,
                    unrankedById,
                    wasFirstObservedInImport));
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
                    entity.Provenance != EntityProvenance.ManualOnly &&
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
        EntityId[] reconciledOverrideOwnerIds = FindChangedOverrideOwners(
            persistedOverrides,
            candidateOverrides);
        TrackedStateChangeSet changeSet = new(
            entitiesToAdd,
            entitiesToUpdate,
            idsToArchive,
            ownerIds,
            candidateImportedResolved.Where(dependency =>
                ownerIds.Contains(dependency.Edge.DependentEntityId)),
            candidateImportedUnresolved.Where(dependency =>
                ownerIds.Contains(dependency.Dependency.DependentEntityId)),
            reconciledOverrideOwnerIds,
            candidateOverrides.Where(item =>
                reconciledOverrideOwnerIds.Contains(item.DependentEntityId)));

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
            Sort(manualOnlyChanges),
            unchangedCount,
            unresolvedChanges,
            ranking,
            changeSet,
            importCandidate,
            currentEntities,
            currentResolved,
            currentUnresolved,
            persistedOverrides,
            candidateEntities,
            candidateImportedResolved,
            candidateImportedUnresolved,
            candidateOverrides,
            candidateEntities
                .Where(entity => !currentById.ContainsKey(entity.Id))
                .ToDictionary(
                    entity => EntitySourceKey.From(entity.SourceName),
                    static entity => entity.Id));
    }

    private static EntityId[] FindChangedOverrideOwners(
        IEnumerable<ManualDependencyOverride> current,
        IEnumerable<ManualDependencyOverride> candidate)
    {
        Dictionary<EntityId, ManualDependencyOverride[]> currentByOwner = current
            .GroupBy(static item => item.DependentEntityId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        Dictionary<EntityId, ManualDependencyOverride[]> candidateByOwner = candidate
            .GroupBy(static item => item.DependentEntityId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());

        return currentByOwner.Keys.Concat(candidateByOwner.Keys)
            .Distinct()
            .Where(ownerId => !OverridesAreEquivalent(
                currentByOwner.GetValueOrDefault(ownerId) ?? [],
                candidateByOwner.GetValueOrDefault(ownerId) ?? []))
            .ToArray();
    }

    private static bool OverridesAreEquivalent(
        IEnumerable<ManualDependencyOverride> left,
        IEnumerable<ManualDependencyOverride> right)
    {
        Dictionary<EntitySourceKey, ManualDependencyOverrideAction> leftByKey = left.ToDictionary(
            item => EntitySourceKey.From(item.DependencySourceName),
            static item => item.Action);
        Dictionary<EntitySourceKey, ManualDependencyOverrideAction> rightByKey = right.ToDictionary(
            item => EntitySourceKey.From(item.DependencySourceName),
            static item => item.Action);
        return leftByKey.Count == rightByKey.Count && leftByKey.All(item =>
            rightByKey.TryGetValue(item.Key, out ManualDependencyOverrideAction action) &&
            action == item.Value);
    }

    private static Dictionary<EntitySourceKey, DependencyDeclaration> BuildImportedDeclarations(
        EntitySourceKey ownerKey,
        SchemaImportCandidate importCandidate,
        IReadOnlyDictionary<EntitySourceKey, ImportedEntity> importedByKey)
    {
        Dictionary<EntitySourceKey, DependencyDeclaration> result = [];

        foreach (ImportedDependency dependency in importCandidate.Dependencies.Where(
                     dependency => dependency.DependentSourceKey == ownerKey))
        {
            ImportedEntity target = importedByKey[dependency.DependencySourceKey];
            result.Add(
                dependency.DependencySourceKey,
                new DependencyDeclaration(
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
                new DependencyDeclaration(
                    dependency.DependencySourceKey,
                    dependency.DependencySourceName,
                    dependency.Kind,
                    null));
        }

        return result;
    }

    private static IReadOnlyList<DependencySynchronizationChange> CompareDeclarations(
        IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> current,
        IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> candidate)
    {
        List<DependencySynchronizationChange> changes = [];
        IEnumerable<EntitySourceKey> keys = current.Keys
            .Concat(candidate.Keys)
            .Distinct()
            .OrderBy(static key => key.Value, StringComparer.Ordinal);

        foreach (EntitySourceKey key in keys)
        {
            bool hasCurrent = current.TryGetValue(
                key,
                out DependencyDeclaration? oldDeclaration);
            bool hasCandidate = candidate.TryGetValue(
                key,
                out DependencyDeclaration? newDeclaration);
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
        IReadOnlyDictionary<EntityId, UnrankedEntity> unrankedById,
        bool wasFirstObservedInImport = false)
    {
        return unrankedById.TryGetValue(entity.Id, out UnrankedEntity? unranked)
            ? new EntitySynchronizationChange(
                entity,
                kind,
                dependencyChanges,
                isReactivation,
                unranked.State,
                unranked.MissingDependencyNames,
                wasFirstObservedInImport)
            : new EntitySynchronizationChange(
                entity,
                kind,
                dependencyChanges,
                isReactivation,
                wasFirstObservedInImport: wasFirstObservedInImport);
    }

    private static IEnumerable<EntitySynchronizationChange> Sort(
        IEnumerable<EntitySynchronizationChange> changes) =>
        changes.OrderBy(static change => change.Entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static change => change.Entity.SourceName, StringComparer.Ordinal);

}
