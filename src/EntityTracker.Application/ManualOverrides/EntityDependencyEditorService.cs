using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Groups;
using EntityTracker.Application.History;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

/// <summary>
/// Validates an entity's dependency changes against the resulting graph and saves valid edits.
/// </summary>
public sealed class EntityDependencyEditorService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly ITrackedStateStore _store;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;
    private readonly PriorityPlanningService _priorityPlanningService;
    private readonly ITrackerRepository? _trackerRepository;

    public EntityDependencyEditorService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        EffectiveDependencyResolver effectiveDependencyResolver,
        IDependencyRankingService dependencyRanker,
        ITrackedStateStore store,
        PriorityPlanningService priorityPlanningService,
        ProgressSnapshotCalculator? snapshotCalculator = null,
        ITrackerRepository? trackerRepository = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(priorityPlanningService);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _dependencyRanker = dependencyRanker;
        _store = store;
        _priorityPlanningService = priorityPlanningService;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
        _trackerRepository = trackerRepository;
    }

    /// <summary>
    /// Whether the Tracker was copied from another. Sync from source matches entities by name, so a
    /// rename there may not match the source.
    /// </summary>
    public async Task<bool> IsCopiedTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default) =>
        _trackerRepository is not null &&
        (await _trackerRepository.GetAsync(trackerId, cancellationToken))?.CopiedFromTrackerId is not null;

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
        return CreatePlan(
            trackerId,
            ownerId,
            snapshot.Entities,
            snapshot.Dependencies,
            snapshot.Dependencies
                .Where(item => item.DependentEntityId == ownerId)
                .Select(static item => DependencyTarget.Entity(item.DependencyEntityId)));
    }

    public async Task<EntityDependencyEditPlan> PreviewAsync(
        TrackerId trackerId,
        EntityId ownerId,
        IEnumerable<DependencyTarget> desiredTargets,
        CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = await LoadSnapshotAsync(trackerId, cancellationToken);
        return CreatePlan(
            trackerId,
            ownerId,
            snapshot.Entities,
            snapshot.Dependencies,
            desiredTargets);
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

        Dictionary<EntityId, TrackedEntity> byId = snapshot.Entities.ToDictionary(static entity => entity.Id);
        return new ArchivedEntityDetails(
            owner,
            CreateItems(snapshot.Dependencies
                .Where(item => item.DependentEntityId == ownerId)
                .Select(item => (byId[item.DependencyEntityId], DependencyTarget.Entity(item.DependencyEntityId)))));
    }

    /// <summary>
    /// Works out what the owner's dependency list would be with <paramref name="desiredTargets"/>: the
    /// resolved graph, its ranking, and anything that stops it from being saved. A name no entity has
    /// gets a placeholder entity, which is added when the plan is saved.
    /// </summary>
    public EntityDependencyEditPlan CreatePlan(
        TrackerId trackerId,
        EntityId ownerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<EntityDependency> allDependencies,
        IEnumerable<DependencyTarget> desiredTargets)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(desiredTargets);
        TrackedEntity[] entityArray = entities.ToArray();
        EntityDependency[] currentDependencies = allDependencies.ToArray();
        TrackerStateValidator.EnsureOwned(trackerId, entityArray, currentDependencies);
        TrackedEntity owner = RequireActiveOwner(ownerId, entityArray);
        Dictionary<EntityId, TrackedEntity> entitiesById = entityArray.ToDictionary(
            static entity => entity.Id);
        HashSet<EntityId> currentTargets = currentDependencies
            .Where(item => item.DependentEntityId == ownerId)
            .Select(static item => item.DependencyEntityId)
            .ToHashSet();
        DependencyTargets targets = new(trackerId, entityArray);

        List<string> warnings = [];
        List<string> errors = [];
        List<(EntityDependency Dependency, TrackedEntity Target, DependencyTarget Requested)> desired = [];
        foreach (DependencyTarget requested in desiredTargets)
        {
            TrackedEntity? target = null;
            if (requested.EntityId is { } targetId)
            {
                if (!entitiesById.TryGetValue(targetId, out target))
                {
                    errors.Add("A dependency refers to an entity that no longer exists.");
                    continue;
                }
            }
            else
            {
                target = targets.For(requested.Name!);
            }

            if (target.Id == ownerId)
            {
                errors.Add("An entity cannot depend on itself.");
                continue;
            }

            if (desired.Any(item => item.Target.Id == target.Id))
            {
                errors.Add($"Dependency '{target.SourceName}' has already been added.");
                continue;
            }

            if (target.LifecycleState == EntityLifecycleState.Archived)
            {
                if (currentTargets.Contains(target.Id))
                    warnings.Add($"'{target.SourceName}' is archived; this dependency stays unresolved until it is restored.");
                else
                    errors.Add($"'{target.SourceName}' exists but is archived.");
            }
            else if (target.IsPlaceholder)
            {
                warnings.Add($"'{target.SourceName}' does not exist and will remain unresolved.");
            }

            // A placeholder that does not exist yet is asked for by name again in the next preview.
            bool isNew = targets.NewPlaceholders.Contains(target);
            desired.Add((new EntityDependency(ownerId, target.Id), target,
                isNew ? DependencyTarget.Named(target.SourceName) : DependencyTarget.Entity(target.Id)));
        }

        TrackedEntity[] candidateEntities = [.. entityArray, .. targets.NewPlaceholders];
        EntityDependency[] candidateDependencies = currentDependencies
            .Where(item => item.DependentEntityId != ownerId)
            .Concat(desired.Select(static item => item.Dependency))
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            candidateDependencies);
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
            candidateEntities,
            CreateItems(desired.Select(static item => (item.Target, item.Requested))),
            desired.Select(static item => item.Dependency),
            targets.NewPlaceholders,
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
        string? sourceName = null,
        DueDateChange? dueDateChange = null)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(notes);
        filterActive ??= plan.Entity.FilterActive;
        sharedNotes ??= plan.Entity.SharedNotes;
        // Without a change, the entity keeps its due date.
        DateOnly? dueDate = dueDateChange is null ? plan.Entity.DueDate : dueDateChange.DueDate;
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
            requestedPriority,
            responsibleDeveloper,
            groupName,
            filterActive,
            sharedNotes,
            dueDate);
        TrackedEntity[] progressUpdates =
            status != plan.Entity.Status || !string.Equals(notes, plan.Entity.Notes, StringComparison.Ordinal) ||
            !string.Equals(filterActive, plan.Entity.FilterActive, StringComparison.Ordinal) ||
            !string.Equals(sharedNotes, plan.Entity.SharedNotes, StringComparison.Ordinal)
                ? [updatedEntity]
                : [];
        TrackedEntity[] priorityUpdates = requestedPriority != plan.Entity.RequestedPriority
            ? [updatedEntity]
            : [];
        TrackedEntity[] dueDateUpdates = dueDate != plan.Entity.DueDate
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
            : new RenameEffects([plan.Entity.Id], plan.DesiredDependencies, plan.EffectiveState);

        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                plan.NewPlaceholders,
                renamed ? [updatedEntity] : [],
                [],
                rename.DependencyOwnerIds,
                rename.Dependencies,
                progressUpdates,
                progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                    candidateEntities,
                    rename.EffectiveState),
                entitiesWithRequestedPriorityToUpdate: priorityUpdates,
                entitiesWithResponsibleDeveloperToUpdate: responsibleDeveloperUpdates,
                entitiesWithGroupNameToUpdate: groupNameUpdates,
                responsibilitySelections: developerIds is null ? [] :
                    [new ResponsibilitySelection(plan.Entity.Id, developerIds)],
                entitiesWithDueDateToUpdate: dueDateUpdates),
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
        // A placeholder is only an unresolved reference: renaming an entity to its name connects it.
        return (await _entityRepository.GetAllAsync(trackerId, cancellationToken))
            .FirstOrDefault(entity => entity.Id != entityId && !entity.IsPlaceholder &&
                                      EntitySourceKey.From(entity.SourceName) == key)
            ?.SourceName;
    }

    /// <summary>
    /// Works out what else a rename changes. Dependencies on the entity refer to its ID and need no
    /// change. If a placeholder has the new name, the dependencies on it move to the entity, and the
    /// placeholder, no longer referred to, goes.
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
        EntitySourceKey key = EntitySourceKey.From(newName);
        TrackedEntity? placeholder = candidateEntities.FirstOrDefault(entity =>
            entity.IsPlaceholder && EntitySourceKey.From(entity.SourceName) == key);
        EntityDependency[] ownDependencies = plan.DesiredDependencies
            .Where(item => item.DependencyEntityId != placeholder?.Id)
            .ToArray();
        EntityDependency[] others = snapshot.Dependencies
            .Where(item => item.DependentEntityId != plan.Entity.Id)
            .ToArray();
        EntityId[] followingOwners = placeholder is null
            ? []
            : others.Where(item => item.DependencyEntityId == placeholder.Id)
                .Select(static item => item.DependentEntityId).Distinct().ToArray();
        EntityDependency[] followingLists = others
            .Where(item => followingOwners.Contains(item.DependentEntityId))
            .Select(item => item.DependencyEntityId == placeholder!.Id
                ? new EntityDependency(item.DependentEntityId, plan.Entity.Id)
                : item)
            .Distinct()
            .ToArray();
        TrackedEntity[] entities = candidateEntities.Where(entity => entity.Id != placeholder?.Id).ToArray();
        EffectiveDependencyState state = _effectiveDependencyResolver.Resolve(
            entities,
            [.. others.Where(item => !followingOwners.Contains(item.DependentEntityId)), .. followingLists,
             .. ownDependencies]);
        return new RenameEffects(
            [plan.Entity.Id, .. followingOwners],
            [.. ownDependencies, .. followingLists],
            state);
    }

    private sealed record RenameEffects(
        IReadOnlyList<EntityId> DependencyOwnerIds,
        IReadOnlyList<EntityDependency> Dependencies,
        EffectiveDependencyState EffectiveState);

    private async Task<Snapshot> LoadSnapshotAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);
        TrackerStateValidator.EnsureOwned(
            trackerId,
            await entitiesTask,
            await dependenciesTask);
        return new Snapshot(
            await entitiesTask,
            await dependenciesTask);
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
        IEnumerable<(TrackedEntity Target, DependencyTarget Requested)> dependencies)
    {
        foreach ((TrackedEntity target, DependencyTarget requested) in dependencies
                     .OrderBy(static item => EntitySourceKey.From(item.Target.SourceName).Value, StringComparer.Ordinal))
        {
            bool isResolved = target.LifecycleState == EntityLifecycleState.Active;
            yield return new EntityDependencyEditItem(
                target.SourceName,
                EntitySourceKey.From(target.SourceName),
                isResolved,
                isResolved ? target.Id : null,
                requested);
        }
    }

    private sealed record Snapshot(
        IReadOnlyList<TrackedEntity> Entities,
        IReadOnlyList<EntityDependency> Dependencies);
}
