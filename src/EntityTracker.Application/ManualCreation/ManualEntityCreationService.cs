using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationService
{
    private const int MaximumSearchSuggestions = 10;

    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly DependencyRanker _dependencyRanker;
    private readonly ITrackedSchemaStore _store;

    public ManualEntityCreationService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        DependencyRanker dependencyRanker,
        ITrackedSchemaStore store)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(dependencyRanker);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _dependencyRanker = dependencyRanker;
        _store = store;
    }

    public async Task<ManualDependencySearchResult> SearchDependenciesAsync(
        string query,
        string? proposedEntityName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string enteredName = query.Trim();
        if (enteredName.Length == 0)
        {
            return new ManualDependencySearchResult(string.Empty, null, [], false, null);
        }

        if (!IsSupportedSourceName(enteredName))
        {
            return new ManualDependencySearchResult(
                enteredName,
                null,
                [],
                false,
                "Dependency names cannot contain commas.");
        }

        EntitySourceKey queryKey = EntitySourceKey.From(enteredName);
        EntitySourceKey? proposedEntityKey = string.IsNullOrWhiteSpace(proposedEntityName)
            ? null
            : EntitySourceKey.From(proposedEntityName);
        IReadOnlyList<TrackedEntity> entities =
            await _entityRepository.GetAllAsync(cancellationToken);

        TrackedEntity? archivedExactMatch = entities.SingleOrDefault(entity =>
            entity.LifecycleState == EntityLifecycleState.Archived &&
            EntitySourceKey.From(entity.SourceName) == queryKey);
        TrackedEntity? activeExactMatch = entities.SingleOrDefault(entity =>
            entity.LifecycleState == EntityLifecycleState.Active &&
            EntitySourceKey.From(entity.SourceName) == queryKey);
        bool isSelfMatch = proposedEntityKey == queryKey;

        ManualDependencySuggestion[] suggestions = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .Where(entity => proposedEntityKey is null ||
                             EntitySourceKey.From(entity.SourceName) != proposedEntityKey)
            .Where(entity => entity.SourceName.Contains(
                enteredName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(entity => MatchPriority(entity.SourceName, enteredName))
            .ThenBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .Take(MaximumSearchSuggestions)
            .Select(static entity => new ManualDependencySuggestion(
                entity.Id,
                entity.SourceName))
            .ToArray();

        if (isSelfMatch)
        {
            return new ManualDependencySearchResult(
                enteredName,
                queryKey,
                suggestions,
                false,
                "An entity cannot depend on itself.");
        }

        if (archivedExactMatch is not null)
        {
            return new ManualDependencySearchResult(
                enteredName,
                queryKey,
                suggestions,
                false,
                $"'{archivedExactMatch.SourceName}' exists but is archived.");
        }

        return new ManualDependencySearchResult(
            enteredName,
            queryKey,
            suggestions,
            activeExactMatch is null,
            null);
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
        await Task.WhenAll(entitiesTask, dependenciesTask, unresolvedTask);

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

        if (HasErrors(diagnostics))
        {
            return ManualEntityCreationResult.Failure(diagnostics);
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
            candidateDeclarations = candidateActiveByKey.Values.ToDictionary(
                static entity => entity.Id,
                entity => currentDeclarations.TryGetValue(
                    entity.Id,
                    out Dictionary<EntitySourceKey, DependencyDeclaration>? declarations)
                    ? declarations.ToDictionary(static item => item.Key, static item => item.Value)
                    : []);

        // Milestone 6.1 intentionally treats every manual dependency as mandatory.
        // These are initial declarations, not durable overrides of a later CSV row.
        candidateDeclarations[createdEntity.Id] = validatedDependencies.ToDictionary(
            static dependency => dependency.TargetKey,
            static dependency => new DependencyDeclaration(
                dependency.TargetKey,
                dependency.TargetName,
                ImportedDependencyKind.Mandatory,
                dependency.ResolvedTargetId));
        DependencyStateResolver.Resolve(candidateDeclarations, candidateActiveByKey);

        PersistedDependency[] candidateResolved =
            DependencyStateResolver.CreateResolvedDependencies(candidateDeclarations);
        PersistedUnresolvedDependency[] candidateUnresolved =
            DependencyStateResolver.CreateUnresolvedDependencies(candidateDeclarations);
        TrackedEntity[] candidateEntities = candidateActiveByKey.Values.ToArray();
        DependencyRankingResult ranking = _dependencyRanker.Rank(
            candidateEntities,
            candidateResolved.Select(static dependency => dependency.Edge),
            candidateUnresolved.Select(static dependency => dependency.Dependency));

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
            if (!AreEquivalent(current, candidateDeclarations[entity.Id]))
            {
                reconciledOwnerIds.Add(entity.Id);
            }
        }

        TrackedSchemaChangeSet changeSet = new(
            [createdEntity],
            [],
            [],
            reconciledOwnerIds,
            candidateResolved.Where(dependency =>
                reconciledOwnerIds.Contains(dependency.Edge.DependentEntityId)),
            candidateUnresolved.Where(dependency =>
                reconciledOwnerIds.Contains(dependency.Dependency.DependentEntityId)));
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

    private static int MatchPriority(string sourceName, string query)
    {
        if (sourceName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return sourceName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

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
