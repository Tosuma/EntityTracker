using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Groups;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ITrackedStateStore _store;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public ManualEntityCreationService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IDependencyRankingService dependencyRanker,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ITrackedStateStore store,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _dependencyRanker = dependencyRanker;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _store = store;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<ManualDependencySearchResult> SearchDependenciesAsync(
        TrackerId trackerId,
        string query,
        string? proposedEntityName = null,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<EntitySourceKey>? excludedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(trackerId, cancellationToken);
        return DependencySearch.Search(query, proposedEntityName, entities, excludedKeys);
    }

    public async Task<IReadOnlyList<string>> SearchGroupNamesAsync(
        TrackerId trackerId,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(trackerId, cancellationToken);
        return GroupNameSuggestionSearch.Search(query, entities);
    }

    public async Task<ManualEntityCreationResult> CreateAsync(
        TrackerId trackerId,
        ManualEntityCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(request);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        TrackedEntity[] currentEntities = (await entitiesTask).ToArray();
        EntityDependency[] currentDependencies = (await dependenciesTask).ToArray();
        TrackerStateValidator.EnsureOwned(trackerId, currentEntities, currentDependencies);
        Dictionary<EntityId, TrackedEntity> currentById = currentEntities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> currentByKey = currentEntities.ToDictionary(
            static entity => EntitySourceKey.From(entity.SourceName));
        Dictionary<EntitySourceKey, TrackedEntity> activeByKey = currentEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToDictionary(static entity => EntitySourceKey.From(entity.SourceName));

        List<ManualEntityCreationDiagnostic> diagnostics = [];
        string entityName = request.EntityName.Trim();
        ValidateRequestedPriority(request.RequestedPriority, diagnostics);
        EntitySourceKey? entityKey = ValidateEntityName(
            entityName,
            currentByKey,
            diagnostics);
        List<ValidatedDependency> validatedDependencies = ValidateDependencies(
            request.Dependencies,
            entityKey,
            currentById,
            currentByKey,
            activeByKey,
            diagnostics);

        // A placeholder with this name (an unresolved reference to it) becomes the new entity, keeping
        // its ID, so the dependencies on it now refer to the new entity.
        TrackedEntity? placeholder = entityKey is not null &&
            currentByKey.TryGetValue(entityKey, out TrackedEntity? named) && named.IsPlaceholder
            ? named
            : null;
        ArchivedEntityMatch? archivedEntityMatch = null;
        if (entityKey is not null &&
            currentByKey.TryGetValue(entityKey, out TrackedEntity? existingEntity) &&
            existingEntity.LifecycleState == EntityLifecycleState.Archived)
        {
            archivedEntityMatch = new ArchivedEntityMatch(
                existingEntity.Id,
                existingEntity.SourceName);
        }

        if (HasErrors(diagnostics))
        {
            return ManualEntityCreationResult.Failure(diagnostics, archivedEntityMatch);
        }

        TrackedEntity createdEntity = new(
            placeholder?.Id ?? EntityId.New(),
            trackerId,
            entityName,
            provenance: EntityProvenance.Manual,
            requestedPriority: request.RequestedPriority,
            responsibleDeveloper: request.ResponsibleDeveloper,
            groupName: request.GroupName);
        TrackedEntity[] withCreated = currentEntities
            .Where(entity => entity.Id != createdEntity.Id)
            .Append(createdEntity)
            .ToArray();
        DependencyTargets targets = new(trackerId, withCreated);
        EntityDependency[] createdDependencies = validatedDependencies
            .Select(dependency => new EntityDependency(
                createdEntity.Id, dependency.TargetId ?? targets.For(dependency.TargetName).Id))
            .ToArray();
        TrackedEntity[] allEntities = [.. withCreated, .. targets.NewPlaceholders];
        TrackedEntity[] candidateEntities = allEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            allEntities,
            [.. currentDependencies, .. createdDependencies]);
        DependencyRankingResult ranking = _dependencyRanker.Rank(
            candidateEntities,
            effectiveState.ResolvedDependencies.Select(static dependency => dependency.Edge),
            effectiveState.UnresolvedDependencies.Select(static dependency => dependency.Dependency));

        if (!ranking.IsSuccess)
        {
            diagnostics.AddRange(ranking.Diagnostics.Select(static diagnostic =>
                new ManualEntityCreationDiagnostic(
                    diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected
                        ? ManualEntityCreationDiagnosticCode.CycleDetected
                        : ManualEntityCreationDiagnosticCode.InvalidDependency,
                    diagnostic.Message)));
            return ManualEntityCreationResult.Failure(diagnostics);
        }

        TrackedStateChangeSet changeSet = new(
            [createdEntity, .. targets.NewPlaceholders],
            [],
            [],
            [createdEntity.Id],
            createdDependencies,
            responsibilitySelections: [new ResponsibilitySelection(createdEntity.Id, request.DeveloperIds)],
            progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                candidateEntities,
                effectiveState));
        await _store.ApplyAsync(trackerId, changeSet, cancellationToken);

        return ManualEntityCreationResult.Success(createdEntity.Id, diagnostics);
    }

    private static EntitySourceKey? ValidateEntityName(
        string entityName,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> currentByKey,
        ICollection<ManualEntityCreationDiagnostic> diagnostics)
    {
        if (entityName.Length == 0)
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.MissingEntityName,
                "An entity name is required."));
            return null;
        }

        if (!IsSupportedSourceName(entityName))
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.UnsupportedEntityName,
                "Entity names cannot contain commas."));
            return null;
        }

        EntitySourceKey key = EntitySourceKey.From(entityName);
        if (currentByKey.TryGetValue(key, out TrackedEntity? existing) && !existing.IsPlaceholder)
        {
            string state = existing.LifecycleState == EntityLifecycleState.Archived
                ? "archived"
                : "active";
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.DuplicateEntity,
                $"'{existing.SourceName}' already exists as an {state} tracked entity."));
        }

        return key;
    }

    private static List<ValidatedDependency> ValidateDependencies(
        IEnumerable<ManualDependencySelection> selections,
        EntitySourceKey? entityKey,
        IReadOnlyDictionary<EntityId, TrackedEntity> currentById,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> currentByKey,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> activeByKey,
        ICollection<ManualEntityCreationDiagnostic> diagnostics)
    {
        List<ValidatedDependency> result = [];
        HashSet<EntitySourceKey> seenKeys = [];

        foreach (ManualDependencySelection selection in selections)
        {
            if (!IsSupportedSourceName(selection.SourceName))
            {
                diagnostics.Add(new ManualEntityCreationDiagnostic(
                    ManualEntityCreationDiagnosticCode.InvalidDependency,
                    $"Dependency '{selection.SourceName}' cannot contain a comma."));
                continue;
            }

            ValidatedDependency? dependency = selection.Kind switch
            {
                ManualDependencySelectionKind.ExistingEntity => ValidateExistingSelection(
                    selection,
                    currentById,
                    diagnostics),
                ManualDependencySelectionKind.Unresolved => ValidateUnresolvedSelection(
                    selection,
                    currentByKey,
                    activeByKey,
                    diagnostics),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(selection),
                    selection.Kind,
                    "The dependency selection kind is not defined.")
            };

            if (dependency is null)
            {
                continue;
            }

            if (entityKey is not null && dependency.TargetKey == entityKey)
            {
                diagnostics.Add(new ManualEntityCreationDiagnostic(
                    ManualEntityCreationDiagnosticCode.SelfDependency,
                    "An entity cannot depend on itself."));
                continue;
            }

            if (!seenKeys.Add(dependency.TargetKey))
            {
                diagnostics.Add(new ManualEntityCreationDiagnostic(
                    ManualEntityCreationDiagnosticCode.DuplicateDependency,
                    $"Dependency '{dependency.TargetName}' has already been added."));
                continue;
            }

            result.Add(dependency);
        }

        return result;
    }

    private static ValidatedDependency? ValidateExistingSelection(
        ManualDependencySelection selection,
        IReadOnlyDictionary<EntityId, TrackedEntity> currentById,
        ICollection<ManualEntityCreationDiagnostic> diagnostics)
    {
        if (selection.EntityId is null ||
            !currentById.TryGetValue(selection.EntityId, out TrackedEntity? selectedEntity))
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.MissingSelectedEntity,
                $"Selected dependency '{selection.SourceName}' no longer exists."));
            return null;
        }

        if (selectedEntity.LifecycleState == EntityLifecycleState.Archived)
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.ArchivedDependency,
                $"'{selectedEntity.SourceName}' exists but is archived."));
            return null;
        }

        return new ValidatedDependency(
            EntitySourceKey.From(selectedEntity.SourceName),
            selectedEntity.SourceName,
            selectedEntity.Id);
    }

    private static ValidatedDependency? ValidateUnresolvedSelection(
        ManualDependencySelection selection,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> currentByKey,
        IReadOnlyDictionary<EntitySourceKey, TrackedEntity> activeByKey,
        ICollection<ManualEntityCreationDiagnostic> diagnostics)
    {
        if (activeByKey.TryGetValue(selection.SourceKey, out TrackedEntity? activeEntity))
        {
            return new ValidatedDependency(
                selection.SourceKey,
                activeEntity.SourceName,
                activeEntity.Id);
        }

        currentByKey.TryGetValue(selection.SourceKey, out TrackedEntity? existing);
        if (existing is { IsPlaceholder: false } archivedEntity)
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.ArchivedDependency,
                $"'{archivedEntity.SourceName}' exists but is archived."));
            return null;
        }

        diagnostics.Add(new ManualEntityCreationDiagnostic(
            ManualEntityCreationDiagnosticCode.UnresolvedDependency,
            $"'{selection.SourceName}' does not exist and will be added as an unresolved dependency.",
            ManualEntityCreationDiagnosticSeverity.Warning));
        // A placeholder for the name is reused; otherwise one is created.
        return new ValidatedDependency(
            selection.SourceKey,
            existing?.SourceName ?? selection.SourceName,
            existing?.Id);
    }

    private static bool IsSupportedSourceName(string sourceName) =>
        !sourceName.Contains(',', StringComparison.Ordinal);

    private static void ValidateRequestedPriority(
        int? requestedPriority,
        ICollection<ManualEntityCreationDiagnostic> diagnostics)
    {
        if (requestedPriority is not null and (< 1 or > 5))
        {
            diagnostics.Add(new ManualEntityCreationDiagnostic(
                ManualEntityCreationDiagnosticCode.InvalidRequestedPriority,
                "Requested priority must be between 1 and 5."));
        }
    }

    private static bool HasErrors(IEnumerable<ManualEntityCreationDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic =>
            diagnostic.Severity == ManualEntityCreationDiagnosticSeverity.Error);

    /// <param name="TargetId">The entity depended on; null when a placeholder must be created for the name.</param>
    private sealed record ValidatedDependency(
        EntitySourceKey TargetKey,
        string TargetName,
        EntityId? TargetId);
}
