using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Groups;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
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
        TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        (await _entityRepository.GetAllAsync(trackerId, cancellationToken))
        .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
        .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
        .ToArray();

    public async Task<ManualDependencySearchResult> SearchDependenciesAsync(
        TrackerId trackerId,
        EntityId ownerId,
        string query,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<EntitySourceKey>? excludedKeys = null)
    {
        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
        return SearchDependencies(trackerId, ownerId, query, snapshot.Entities, excludedKeys);
    }

    public async Task<IReadOnlyList<string>> SearchGroupNamesAsync(
        TrackerId trackerId,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(trackerId, cancellationToken);
        return GroupNameSuggestionSearch.Search(query, entities);
    }

    public ManualDependencySearchResult SearchDependencies(
        TrackerId trackerId,
        EntityId ownerId,
        string query,
        IEnumerable<TrackedEntity> entities,
        IReadOnlyCollection<EntitySourceKey>? excludedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        TrackedEntity[] entityArray = entities.ToArray();
        if (entityArray.Any(entity => entity.TrackerId != trackerId))
        {
            throw new InvalidDataException("Dependency search received entities from another tracker.");
        }
        TrackedEntity owner = RequireActiveOwner(ownerId, entityArray);
        return DependencySearch.Search(query, owner.SourceName, entityArray, excludedKeys);
    }

    public async Task<EntityDependencyEditPlan> LoadAsync(
        TrackerId trackerId,
        EntityId ownerId,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
        ManualDependencyOverride[] ownerOverrides = snapshot.Overrides
            .Where(item => item.DependentEntityId == ownerId)
            .ToArray();
        return CreatePlan(
            trackerId,
            ownerId,
            snapshot.Entities,
            snapshot.ResolvedDependencies,
            snapshot.UnresolvedDependencies,
            snapshot.Overrides,
            ownerOverrides);
    }

    public async Task<EntityDependencyEditPlan> PreviewAsync(
        TrackerId trackerId,
        EntityId ownerId,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
        return CreatePlan(
            trackerId,
            ownerId,
            snapshot.Entities,
            snapshot.ResolvedDependencies,
            snapshot.UnresolvedDependencies,
            snapshot.Overrides,
            desiredOwnerOverrides);
    }

    public async Task<ArchivedEntityDetails> LoadArchivedDetailsAsync(
        TrackerId trackerId,
        EntityId ownerId,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
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
        TrackerId trackerId,
        EntityId ownerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> importedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> importedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> allOverrides,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(ownerId);
        TrackedEntity[] entityArray = entities.ToArray();
        PersistedDependency[] resolvedArray = importedResolvedDependencies.ToArray();
        PersistedUnresolvedDependency[] unresolvedArray = importedUnresolvedDependencies.ToArray();
        ManualDependencyOverride[] currentOverrides = allOverrides.ToArray();
        ManualDependencyOverride[] desiredOverrides = desiredOwnerOverrides.ToArray();
        TrackerStateValidator.EnsureOwned(
            trackerId,
            entityArray,
            resolvedArray,
            unresolvedArray,
            currentOverrides);
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
        TrackerId trackerId,
        EntityDependencyEditPlan plan,
        DevelopmentStatus status,
        string notes,
        int? requestedPriority,
        string? responsibleDeveloper,
        string? groupName,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DeveloperId>? developerIds = null,
        string? filterActive = null,
        string? sharedNotes = null,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(notes);
        filterActive ??= plan.Entity.FilterActive;
        sharedNotes ??= plan.Entity.SharedNotes;
        if (!plan.IsValid)
        {
            throw new InvalidOperationException(
                "Dependency edits cannot be saved while validation errors remain.");
        }
        if (plan.Entity.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The edit plan belongs to another tracker.");
        }

        string name = sourceName?.Trim() ?? plan.Entity.SourceName;
        bool renamed = !string.Equals(name, plan.Entity.SourceName, StringComparison.Ordinal);
        TrackedEntity updatedEntity = new(
            plan.Entity.Id,
            trackerId,
            name,
            status,
            notes,
            plan.Entity.LifecycleState,
            plan.Entity.Provenance,
            requestedPriority,
            responsibleDeveloper,
            groupName,
            filterActive,
            sharedNotes);
        TrackedEntity[] progressUpdates =
            status != plan.Entity.Status || !string.Equals(notes, plan.Entity.Notes, StringComparison.Ordinal) ||
            !string.Equals(filterActive, plan.Entity.FilterActive, StringComparison.Ordinal) ||
            !string.Equals(sharedNotes, plan.Entity.SharedNotes, StringComparison.Ordinal)
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
        RenameEffects rename = renamed
            ? await PlanRenameAsync(trackerId, plan, name, candidateEntities, cancellationToken)
            : new RenameEffects([plan.Entity.Id], plan.DesiredOverrides, plan.EffectiveState);

        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                [],
                renamed ? [updatedEntity] : [],
                [],
                [],
                [],
                [],
                rename.OverrideOwnerIds,
                rename.Overrides,
                progressUpdates,
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    rename.EffectiveState),
                entitiesWithRequestedPriorityToUpdate: priorityUpdates,
                entitiesWithResponsibleDeveloperToUpdate: responsibleDeveloperUpdates,
                entitiesWithGroupNameToUpdate: groupNameUpdates,
                responsibilitySelections: developerIds is null ? [] :
                    [new ResponsibilitySelection(plan.Entity.Id, developerIds)]),
            cancellationToken);
    }

    public PriorityPlanningPreview CreatePriorityPreview(
        TrackerId trackerId,
        EntityDependencyEditPlan plan,
        int? candidateRequestedPriority)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Entity.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The edit plan belongs to another tracker.");
        }
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

    /// <summary>
    /// Gets the name of another entity in the Tracker that already uses this name (ignoring case and
    /// surrounding spaces), or null when the name is free.
    /// </summary>
    public async Task<string?> FindNameConflictAsync(
        TrackerId trackerId,
        EntityId entityId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        EntitySourceKey key = EntitySourceKey.From(name);
        return (await _entityRepository.GetAllAsync(trackerId, cancellationToken))
            .FirstOrDefault(entity => entity.Id != entityId && EntitySourceKey.From(entity.SourceName) == key)
            ?.SourceName;
    }

    /// <summary>
    /// Works out what else a rename changes. Dependencies other entities added by hand, or suppressed,
    /// name this entity, so they follow it to the new name; the dependency graph is then worked out
    /// again, since the new name may now match dependencies that named it before it existed.
    /// </summary>
    private async Task<RenameEffects> PlanRenameAsync(
        TrackerId trackerId,
        EntityDependencyEditPlan plan,
        string newName,
        IReadOnlyList<TrackedEntity> candidateEntities,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("An entity needs a name.");
        string? conflict = await FindNameConflictAsync(trackerId, plan.Entity.Id, newName, cancellationToken);
        if (conflict is not null)
            throw new InvalidOperationException($"Another entity in this Tracker is already called {conflict}.");

        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
        EntitySourceKey oldKey = EntitySourceKey.From(plan.Entity.SourceName);
        bool keyChanged = oldKey != EntitySourceKey.From(newName);
        ManualDependencyOverride[] others = snapshot.Overrides
            .Where(item => item.DependentEntityId != plan.Entity.Id)
            .Select(item => keyChanged && EntitySourceKey.From(item.DependencySourceName) == oldKey
                ? new ManualDependencyOverride(item.DependentEntityId, newName, item.Action)
                : item)
            .ToArray();
        EntityId[] followingOwners = keyChanged
            ? snapshot.Overrides
                .Where(item => item.DependentEntityId != plan.Entity.Id &&
                               EntitySourceKey.From(item.DependencySourceName) == oldKey)
                .Select(static item => item.DependentEntityId)
                .Distinct()
                .ToArray()
            : [];
        ManualDependencyOverride[] allOverrides = [.. others, .. plan.DesiredOverrides];
        EffectiveDependencyState state = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            snapshot.ResolvedDependencies,
            snapshot.UnresolvedDependencies,
            allOverrides);
        return new RenameEffects(
            [plan.Entity.Id, .. followingOwners],
            [.. plan.DesiredOverrides, .. others.Where(item => followingOwners.Contains(item.DependentEntityId))],
            state);
    }

    private sealed record RenameEffects(
        IReadOnlyList<EntityId> OverrideOwnerIds,
        IReadOnlyList<ManualDependencyOverride> Overrides,
        EffectiveDependencyState EffectiveState);

    private async Task<Snapshot> LoadSnapshotAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);
        TrackerStateValidator.EnsureOwned(
            trackerId,
            await entitiesTask,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
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
