using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Groups;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

/// <summary>
/// Validates dependency corrections against the effective graph and persists valid edits.
/// </summary>
public sealed class EntityDependencyEditorService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly ITrackedStateStore _store;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;
    private readonly PriorityPlanningService _priorityPlanningService;

    public EntityDependencyEditorService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        EffectiveDependencyResolver effectiveDependencyResolver,
        IDependencyRankingService dependencyRanker,
        ITrackedStateStore store,
        PriorityPlanningService priorityPlanningService,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(priorityPlanningService);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _dependencyRanker = dependencyRanker;
        _store = store;
        _priorityPlanningService = priorityPlanningService;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<IReadOnlyList<TrackedEntity>> GetEditableEntitiesAsync(
        CancellationToken cancellationToken = default) =>
        (await _entityRepository.GetAllAsync(cancellationToken))
        .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
        .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
        .ToArray();

    public async Task<ManualDependencySearchResult> SearchDependenciesAsync(
        EntityId ownerId,
        string query,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(cancellationToken);
        return SearchDependencies(ownerId, query, snapshot.Entities);
    }

    public async Task<IReadOnlyList<string>> SearchGroupNamesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(cancellationToken);
        return GroupNameSuggestionSearch.Search(query, entities);
    }

    public ManualDependencySearchResult SearchDependencies(
        EntityId ownerId,
        string query,
        IEnumerable<TrackedEntity> entities)
    {
        TrackedEntity[] entityArray = entities.ToArray();
        TrackedEntity owner = RequireActiveOwner(ownerId, entityArray);
        return DependencySearch.Search(query, owner.SourceName, entityArray);
    }

    public async Task<EntityDependencyEditPlan> LoadAsync(
        EntityId ownerId,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(cancellationToken);
        ManualDependencyOverride[] ownerOverrides = snapshot.Overrides
            .Where(item => item.DependentEntityId == ownerId)
            .ToArray();
        return CreatePlan(
            ownerId,
            snapshot.Entities,
            snapshot.ResolvedDependencies,
            snapshot.UnresolvedDependencies,
            snapshot.Overrides,
            ownerOverrides);
    }

    public async Task<EntityDependencyEditPlan> PreviewAsync(
        EntityId ownerId,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(cancellationToken);
        return CreatePlan(
            ownerId,
            snapshot.Entities,
            snapshot.ResolvedDependencies,
            snapshot.UnresolvedDependencies,
            snapshot.Overrides,
            desiredOwnerOverrides);
    }

    public async Task<ArchivedEntityDetails> LoadArchivedDetailsAsync(
        EntityId ownerId,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(cancellationToken);
        TrackedEntity owner = snapshot.Entities.SingleOrDefault(entity => entity.Id == ownerId)
            ?? throw new InvalidOperationException("The selected entity no longer exists.");
        if (owner.LifecycleState != EntityLifecycleState.Archived)
        {
            throw new InvalidOperationException("The selected entity is no longer archived.");
        }

        Dictionary<EntityId, TrackedEntity> entitiesById = snapshot.Entities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> entitiesByKey = snapshot.Entities.ToDictionary(
            static entity => EntitySourceKey.From(entity.SourceName));
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> imported =
            DependencyStateResolver.BuildCurrentDeclarations(
                snapshot.ResolvedDependencies,
                snapshot.UnresolvedDependencies,
                entitiesById);
        Dictionary<EntitySourceKey, DependencyDeclaration> ownerImported = imported.TryGetValue(
            ownerId,
            out Dictionary<EntitySourceKey, DependencyDeclaration>? declarations)
            ? declarations
            : [];
        Dictionary<EntitySourceKey, ManualDependencyOverride> ownerOverrides = snapshot.Overrides
            .Where(item => item.DependentEntityId == ownerId)
            .ToDictionary(static item => EntitySourceKey.From(item.DependencySourceName));

        return new ArchivedEntityDetails(
            owner,
            CreateItems(ownerImported, ownerOverrides, entitiesByKey));
    }

    public EntityDependencyEditPlan CreatePlan(
        EntityId ownerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> importedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> importedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> allOverrides,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        TrackedEntity[] entityArray = entities.ToArray();
        PersistedDependency[] resolvedArray = importedResolvedDependencies.ToArray();
        PersistedUnresolvedDependency[] unresolvedArray = importedUnresolvedDependencies.ToArray();
        ManualDependencyOverride[] currentOverrides = allOverrides.ToArray();
        ManualDependencyOverride[] desiredOverrides = desiredOwnerOverrides.ToArray();
        TrackedEntity owner = RequireActiveOwner(ownerId, entityArray);
        Dictionary<EntityId, TrackedEntity> entitiesById = entityArray.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> entitiesByKey = entityArray.ToDictionary(
            static entity => EntitySourceKey.From(entity.SourceName));
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>> imported =
            DependencyStateResolver.BuildCurrentDeclarations(
                resolvedArray,
                unresolvedArray,
                entitiesById);
        Dictionary<EntitySourceKey, DependencyDeclaration> ownerImported =
            imported.TryGetValue(
                ownerId,
                out Dictionary<EntitySourceKey, DependencyDeclaration>? declarations)
                ? declarations
                : [];

        List<string> warnings = [];
        List<string> errors = [];
        Dictionary<EntitySourceKey, ManualDependencyOverride> desiredByKey = [];
        foreach (ManualDependencyOverride dependencyOverride in desiredOverrides)
        {
            if (dependencyOverride.DependentEntityId != ownerId)
            {
                errors.Add("Every dependency override must belong to the entity being edited.");
                continue;
            }

            EntitySourceKey key = EntitySourceKey.From(dependencyOverride.DependencySourceName);
            if (!desiredByKey.TryAdd(key, dependencyOverride))
            {
                errors.Add($"Dependency '{dependencyOverride.DependencySourceName}' has already been edited.");
                continue;
            }

            if (key == EntitySourceKey.From(owner.SourceName))
            {
                errors.Add("An entity cannot depend on itself.");
                continue;
            }

            if (dependencyOverride.Action == ManualDependencyOverrideAction.Add)
            {
                if (entitiesByKey.TryGetValue(key, out TrackedEntity? target) &&
                    target.LifecycleState == EntityLifecycleState.Archived)
                {
                    errors.Add($"'{target.SourceName}' exists but is archived.");
                }
                else if (!entitiesByKey.TryGetValue(key, out target) ||
                         target.LifecycleState != EntityLifecycleState.Active)
                {
                    warnings.Add(
                        $"'{dependencyOverride.DependencySourceName}' does not exist and will remain unresolved.");
                }
            }
            else if (!ownerImported.ContainsKey(key) &&
                     !currentOverrides.Any(item =>
                         item.DependentEntityId == ownerId &&
                         item.Action == ManualDependencyOverrideAction.Suppress &&
                         EntitySourceKey.From(item.DependencySourceName) == key))
            {
                errors.Add(
                    $"'{dependencyOverride.DependencySourceName}' is not an imported dependency and cannot be suppressed.");
            }
        }

        ManualDependencyOverride[] candidateOverrides = currentOverrides
            .Where(item => item.DependentEntityId != ownerId)
            .Concat(desiredByKey.Values)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            entityArray,
            resolvedArray,
            unresolvedArray,
            candidateOverrides);
        TrackedEntity[] activeEntities = entityArray
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        DependencyRankingResult ranking = _dependencyRanker.Rank(
            activeEntities,
            effectiveState.ResolvedDependencies.Select(static dependency => dependency.Edge),
            effectiveState.UnresolvedDependencies.Select(static dependency => dependency.Dependency));
        errors.AddRange(ranking.Diagnostics.Select(static diagnostic => diagnostic.Message));

        return new EntityDependencyEditPlan(
            owner,
            entityArray,
            CreateItems(ownerImported, desiredByKey, entitiesByKey),
            desiredByKey.Values,
            effectiveState,
            ranking,
            warnings.Distinct(StringComparer.Ordinal),
            errors.Distinct(StringComparer.Ordinal));
    }

    public async Task SaveAsync(
        EntityDependencyEditPlan plan,
        DevelopmentStatus status,
        string notes,
        int? requestedPriority,
        string? responsibleDeveloper,
        string? groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(notes);
        if (!plan.IsValid)
        {
            throw new InvalidOperationException(
                "Dependency edits cannot be saved while validation errors remain.");
        }

        TrackedEntity updatedEntity = new(
            plan.Entity.Id,
            plan.Entity.SourceName,
            status,
            notes,
            plan.Entity.LifecycleState,
            plan.Entity.Provenance,
            requestedPriority,
            responsibleDeveloper,
            groupName);
        TrackedEntity[] progressUpdates =
            status != plan.Entity.Status || !string.Equals(notes, plan.Entity.Notes, StringComparison.Ordinal)
                ? [updatedEntity]
                : [];
        TrackedEntity[] priorityUpdates = requestedPriority != plan.Entity.RequestedPriority
            ? [updatedEntity]
            : [];
        TrackedEntity[] responsibleDeveloperUpdates = !string.Equals(
                updatedEntity.ResponsibleDeveloper,
                plan.Entity.ResponsibleDeveloper,
                StringComparison.Ordinal)
            ? [updatedEntity]
            : [];
        TrackedEntity[] groupNameUpdates = !string.Equals(
                updatedEntity.GroupName,
                plan.Entity.GroupName,
                StringComparison.Ordinal)
            ? [updatedEntity]
            : [];
        TrackedEntity[] candidateEntities = plan.CandidateEntities
            .Select(entity => entity.Id == updatedEntity.Id ? updatedEntity : entity)
            .ToArray();

        await _store.ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                [],
                [],
                [],
                [plan.Entity.Id],
                plan.DesiredOverrides,
                progressUpdates,
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    plan.EffectiveState),
                entitiesWithRequestedPriorityToUpdate: priorityUpdates,
                entitiesWithResponsibleDeveloperToUpdate: responsibleDeveloperUpdates,
                entitiesWithGroupNameToUpdate: groupNameUpdates),
            cancellationToken);
    }

    public PriorityPlanningPreview CreatePriorityPreview(
        EntityDependencyEditPlan plan,
        int? candidateRequestedPriority)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            throw new InvalidOperationException(
                "Priority cannot be previewed while dependency validation errors remain.");
        }

        return _priorityPlanningService.CreatePreview(
            plan.Entity.Id,
            candidateRequestedPriority,
            plan.CandidateEntities,
            plan.EffectiveState);
    }

    private async Task<Snapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);
        return new Snapshot(
            await entitiesTask,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
    }

    private static TrackedEntity RequireActiveOwner(
        EntityId ownerId,
        IEnumerable<TrackedEntity> entities)
    {
        TrackedEntity? owner = entities.SingleOrDefault(entity => entity.Id == ownerId);
        if (owner is null)
        {
            throw new InvalidOperationException("The selected entity no longer exists.");
        }

        if (owner.LifecycleState != EntityLifecycleState.Active)
        {
            throw new InvalidOperationException("Archived entities cannot be edited.");
        }

        return owner;
    }

    private static IEnumerable<EntityDependencyEditItem> CreateItems(
        IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> imported,
        IReadOnlyDictionary<EntitySourceKey, ManualDependencyOverride> overrides,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> entitiesByKey)
    {
        foreach (EntitySourceKey key in imported.Keys.Concat(overrides.Keys)
                     .Distinct()
                     .OrderBy(static item => item.Value, StringComparer.Ordinal))
        {
            imported.TryGetValue(key, out DependencyDeclaration? importedDeclaration);
            overrides.TryGetValue(key, out ManualDependencyOverride? dependencyOverride);
            bool hasImported = importedDeclaration is not null;
            bool isAddition = dependencyOverride?.Action == ManualDependencyOverrideAction.Add;
            bool isSuppression = dependencyOverride?.Action == ManualDependencyOverrideAction.Suppress;
            DependencyEditOrigin origin = (hasImported, isAddition, isSuppression) switch
            {
                (true, true, false) => DependencyEditOrigin.ImportedAndManual,
                (true, false, true) => DependencyEditOrigin.SuppressedImported,
                (false, true, false) => DependencyEditOrigin.Manual,
                (false, false, true) => DependencyEditOrigin.DormantSuppression,
                _ => DependencyEditOrigin.Imported
            };
            string targetName = dependencyOverride?.DependencySourceName ??
                                importedDeclaration!.TargetName;
            bool isEffective = !isSuppression;
            entitiesByKey.TryGetValue(key, out TrackedEntity? target);
            bool isResolved = isEffective &&
                              target?.LifecycleState == EntityLifecycleState.Active;
            yield return new EntityDependencyEditItem(
                targetName,
                key,
                origin,
                importedDeclaration?.Kind,
                isResolved,
                isResolved ? target!.Id : null);
        }
    }

    private sealed record Snapshot(
        IReadOnlyList<TrackedEntity> Entities,
        IReadOnlyList<PersistedDependency> ResolvedDependencies,
        IReadOnlyList<PersistedUnresolvedDependency> UnresolvedDependencies,
        IReadOnlyList<ManualDependencyOverride> Overrides);
}
