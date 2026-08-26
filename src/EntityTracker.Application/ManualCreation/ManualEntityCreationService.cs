using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly IDependencyRankingService _dependencyRanker;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ITrackedStateStore _store;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public ManualEntityCreationService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        IDependencyRankingService dependencyRanker,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ITrackedStateStore store,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _dependencyRanker = dependencyRanker;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _store = store;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<ManualDependencySearchResult> SearchDependenciesAsync(
        string query,
        string? proposedEntityName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(cancellationToken);
        return DependencySearch.Search(query, proposedEntityName, entities);
    }

    public async Task<ManualEntityCreationResult> CreateAsync(
        ManualEntityCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask, unresolvedTask, overridesTask);

        TrackedEntity[] currentEntities = (await entitiesTask).ToArray();
        PersistedDependency[] currentResolved = (await dependenciesTask).ToArray();
        PersistedUnresolvedDependency[] currentUnresolved = (await unresolvedTask).ToArray();
        Dictionary<EntityId, TrackedEntity> currentById = currentEntities.ToDictionary(
            static entity => entity.Id);
        Dictionary<EntitySourceKey, TrackedEntity> currentByKey = currentEntities.ToDictionary(
            static entity => EntitySourceKey.From(entity.SourceName));
        Dictionary<EntitySourceKey, TrackedEntity> activeByKey = currentEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToDictionary(static entity => EntitySourceKey.From(entity.SourceName));

        List<ManualEntityCreationDiagnostic> diagnostics = [];
        string entityName = request.EntityName.Trim();
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
            EntityId.New(),
            entityName,
            provenance: EntityProvenance.ManualOnly);
        Dictionary<EntitySourceKey, TrackedEntity> candidateActiveByKey = new(activeByKey)
        {
            [entityKey!] = createdEntity
        };

        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            currentDeclarations = DependencyStateResolver.BuildCurrentDeclarations(
                currentResolved,
                currentUnresolved,
                currentById);
        Dictionary<EntityId, Dictionary<EntitySourceKey, DependencyDeclaration>>
            candidateImportedDeclarations = candidateActiveByKey.Values.ToDictionary(
                static entity => entity.Id,
                entity => currentDeclarations.TryGetValue(
                    entity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? declarations)
                    ? declarations.ToDictionary(static item => item.Key, static item => item.Value)
                    : []);

        DependencyStateResolver.Resolve(candidateImportedDeclarations, candidateActiveByKey);

        PersistedDependency[] candidateImportedResolved =
            DependencyStateResolver.CreateResolvedDependencies(candidateImportedDeclarations);
        PersistedUnresolvedDependency[] candidateImportedUnresolved =
            DependencyStateResolver.CreateUnresolvedDependencies(candidateImportedDeclarations);
        ManualDependencyOverride[] createdOverrides = validatedDependencies
            .Select(dependency => new ManualDependencyOverride(
                createdEntity.Id,
                dependency.TargetName,
                ManualDependencyOverrideAction.Add))
            .ToArray();
        ManualDependencyOverride[] candidateOverrides = (await overridesTask)
            .Concat(createdOverrides)
            .ToArray();
        TrackedEntity[] candidateEntities = candidateActiveByKey.Values.ToArray();
        EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
            candidateEntities,
            candidateImportedResolved,
            candidateImportedUnresolved,
            candidateOverrides);
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

        HashSet<EntityId> reconciledOwnerIds = [createdEntity.Id];
        foreach (TrackedEntity entity in candidateEntities.Where(entity =>
                     entity.Id != createdEntity.Id))
        {
            IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> current =
                currentDeclarations.TryGetValue(
                    entity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? declarations)
                    ? declarations
                    : new Dictionary<EntitySourceKey, DependencyDeclaration>();
            if (!AreEquivalent(current, candidateImportedDeclarations[entity.Id]))
            {
                reconciledOwnerIds.Add(entity.Id);
            }
        }

        TrackedStateChangeSet changeSet = new(
            [createdEntity],
            [],
            [],
            reconciledOwnerIds,
            candidateImportedResolved.Where(dependency =>
                reconciledOwnerIds.Contains(dependency.Edge.DependentEntityId)),
            candidateImportedUnresolved.Where(dependency =>
                reconciledOwnerIds.Contains(dependency.Dependency.DependentEntityId)),
            [createdEntity.Id],
            createdOverrides,
            progressSnapshotAfterChanges: _snapshotCalculator.Calculate(
                candidateEntities,
                effectiveState));
        await _store.ApplyAsync(changeSet, cancellationToken);

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
        if (currentByKey.TryGetValue(key, out TrackedEntity? existing))
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

        if (currentByKey.TryGetValue(selection.SourceKey, out TrackedEntity? archivedEntity))
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
        return new ValidatedDependency(
            selection.SourceKey,
            selection.SourceName,
            null);
    }

    private static bool AreEquivalent(
        IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> current,
        IReadOnlyDictionary<EntitySourceKey, DependencyDeclaration> candidate) =>
        current.Count == candidate.Count && current.All(item =>
            candidate.TryGetValue(item.Key, out DependencyDeclaration? value) &&
            item.Value == value);

    private static bool IsSupportedSourceName(string sourceName) =>
        !sourceName.Contains(',', StringComparison.Ordinal);

    private static bool HasErrors(IEnumerable<ManualEntityCreationDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic =>
            diagnostic.Severity == ManualEntityCreationDiagnosticSeverity.Error);

    private sealed record ValidatedDependency(
        EntitySourceKey TargetKey,
        string TargetName,
        EntityId? ResolvedTargetId);
}
