using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.ManualCreation;

public sealed class ManualEntityCreationServiceTests
{
    [Fact]
    public async Task SearchDependenciesAsync_ReturnsActiveMatchesInPredictableOrder()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity alphabet = Entity(2, "Alphabet");
        TrackedEntity zuluAlpha = Entity(3, "ZuluAlpha");
        TrackedEntity archived = Entity(
            4,
            "ArchivedAlpha",
            lifecycle: EntityLifecycleState.Archived);
        ManualEntityCreationService service = Service(
            [zuluAlpha, archived, alphabet, alpha],
            [],
            [],
            out _);

        ManualDependencySearchResult result =
            await service.SearchDependenciesAsync("alpha");

        Assert.Equal(
            ["Alpha", "Alphabet", "ZuluAlpha"],
            result.Suggestions.Select(static suggestion => suggestion.SourceName));
        Assert.False(result.CanAddAsUnresolved);
        Assert.Null(result.BlockingMessage);
    }

    [Fact]
    public async Task SearchDependenciesAsync_ArchivedExactMatchIsBlockedAndNotSuggested()
    {
        ManualEntityCreationService service = Service(
            [Entity(1, "Legacy", lifecycle: EntityLifecycleState.Archived)],
            [],
            [],
            out _);

        ManualDependencySearchResult result =
            await service.SearchDependenciesAsync(" legacy ");

        Assert.Empty(result.Suggestions);
        Assert.False(result.CanAddAsUnresolved);
        Assert.Contains("archived", result.BlockingMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchDependenciesAsync_ProposedEntityNameCannotBeAddedAsUnresolved()
    {
        ManualEntityCreationService service = Service([], [], [], out _);

        ManualDependencySearchResult result =
            await service.SearchDependenciesAsync(" customer ", "Customer");

        Assert.False(result.CanAddAsUnresolved);
        Assert.Contains("itself", result.BlockingMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchDependenciesAsync_LimitsSuggestionsToTen()
    {
        TrackedEntity[] entities = Enumerable.Range(1, 15)
            .Select(index => Entity(index, $"Table{index:00}"))
            .ToArray();
        ManualEntityCreationService service = Service(entities, [], [], out _);

        ManualDependencySearchResult result =
            await service.SearchDependenciesAsync("Table");

        Assert.Equal(10, result.Suggestions.Count);
        Assert.Equal("Table01", result.Suggestions[0].SourceName);
        Assert.Equal("Table10", result.Suggestions[9].SourceName);
    }

    [Fact]
    public async Task CreateAsync_WithoutDependencies_AddsManualOnlyEntity()
    {
        ManualEntityCreationService service = Service([], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(" NewEntity ", []));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        TrackedEntity added = Assert.Single(store.LastChangeSet!.EntitiesToAdd);
        Assert.Equal("NewEntity", added.SourceName);
        Assert.Equal(DevelopmentStatus.NotStarted, added.Status);
        Assert.Equal(EntityLifecycleState.Active, added.LifecycleState);
        Assert.Equal(EntityProvenance.ManualOnly, added.Provenance);
        Assert.Equal(added.Id, result.CreatedEntityId);
        Assert.Empty(store.LastChangeSet.ResolvedDependencies);
        Assert.Empty(store.LastChangeSet.UnresolvedDependencies);
    }

    [Fact]
    public async Task CreateAsync_WithResolvedAndUnknownDependencies_PreservesBothAsMandatory()
    {
        TrackedEntity known = Entity(1, "Known");
        ManualEntityCreationService service = Service([known], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [
                    ManualDependencySelection.Existing(known.Id, known.SourceName),
                    ManualDependencySelection.Unresolved(" Future ")
                ]));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.UnresolvedDependency &&
            diagnostic.Severity == ManualEntityCreationDiagnosticSeverity.Warning);
        TrackedEntity added = Assert.Single(store.LastChangeSet!.EntitiesToAdd);
        PersistedDependency resolved = Assert.Single(store.LastChangeSet.ResolvedDependencies);
        Assert.Equal(new DependencyEdge(added.Id, known.Id), resolved.Edge);
        Assert.Equal(ImportedDependencyKind.Mandatory, resolved.Kind);
        PersistedUnresolvedDependency unresolved =
            Assert.Single(store.LastChangeSet.UnresolvedDependencies);
        Assert.Equal(added.Id, unresolved.Dependency.DependentEntityId);
        Assert.Equal("Future", unresolved.Dependency.DependencySourceName);
        Assert.Equal(ImportedDependencyKind.Mandatory, unresolved.Kind);
    }

    [Fact]
    public async Task CreateAsync_ResolvesMatchingUnresolvedReferencesElsewhere()
    {
        TrackedEntity owner = Entity(1, "Owner");
        PersistedUnresolvedDependency existing = new(
            new UnresolvedDependency(owner.Id, " Future "),
            ImportedDependencyKind.Optional);
        ManualEntityCreationService service = Service(
            [owner],
            [],
            [existing],
            out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest("future", []));

        Assert.True(result.IsSuccess);
        TrackedEntity created = Assert.Single(store.LastChangeSet!.EntitiesToAdd);
        Assert.Contains(owner.Id, store.LastChangeSet.ReconciledOwnerIds);
        PersistedDependency resolved = Assert.Single(
            store.LastChangeSet.ResolvedDependencies,
            dependency => dependency.Edge.DependentEntityId == owner.Id);
        Assert.Equal(created.Id, resolved.Edge.DependencyEntityId);
        Assert.Equal(ImportedDependencyKind.Optional, resolved.Kind);
        Assert.DoesNotContain(
            store.LastChangeSet.UnresolvedDependencies,
            dependency => dependency.Dependency.DependentEntityId == owner.Id);
    }

    [Fact]
    public async Task CreateAsync_UnresolvedSelectionThatNowExistsIsResolved()
    {
        TrackedEntity known = Entity(1, "Known");
        ManualEntityCreationService service = Service([known], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Unresolved("known")]));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.UnresolvedDependency);
        Assert.Single(store.LastChangeSet!.ResolvedDependencies);
        Assert.Empty(store.LastChangeSet.UnresolvedDependencies);
    }

    [Theory]
    [InlineData(EntityLifecycleState.Active)]
    [InlineData(EntityLifecycleState.Archived)]
    public async Task CreateAsync_NormalizedDuplicateEntityIsRejectedAcrossLifecycleStates(
        EntityLifecycleState lifecycle)
    {
        TrackedEntity existing = Entity(
            1,
            "Customer",
            lifecycle: lifecycle);
        ManualEntityCreationService service = Service([existing], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(" customer ", []));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.DuplicateEntity);
        Assert.Null(store.LastChangeSet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unsupported,Name")]
    public async Task CreateAsync_InvalidEntityNameIsRejected(string entityName)
    {
        ManualEntityCreationService service = Service([], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(entityName, []));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code is ManualEntityCreationDiagnosticCode.MissingEntityName or
                ManualEntityCreationDiagnosticCode.UnsupportedEntityName);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task CreateAsync_StaleExistingSelectionIsRejected()
    {
        ManualEntityCreationService service = Service([], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Existing(EntityId.New(), "Gone")]));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.MissingSelectedEntity);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task CreateAsync_SelfAndNormalizedDuplicateDependenciesAreRejected()
    {
        TrackedEntity known = Entity(1, "Known");
        ManualEntityCreationService service = Service([known], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [
                    ManualDependencySelection.Unresolved(" owner "),
                    ManualDependencySelection.Existing(known.Id, "Known"),
                    ManualDependencySelection.Unresolved(" known ")
                ]));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.SelfDependency);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.DuplicateDependency);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task CreateAsync_ArchivedDependencyIsRejectedRatherThanMadeUnresolved()
    {
        TrackedEntity archived = Entity(
            1,
            "Legacy",
            lifecycle: EntityLifecycleState.Archived);
        ManualEntityCreationService service = Service([archived], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Unresolved("legacy")]));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.ArchivedDependency);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task CreateAsync_NewResolutionThatWouldCreateCycleIsRejectedWithoutWrite()
    {
        TrackedEntity a = Entity(1, "A");
        PersistedUnresolvedDependency aToB = new(
            new UnresolvedDependency(a.Id, "B"),
            ImportedDependencyKind.Mandatory);
        ManualEntityCreationService service = Service(
            [a],
            [],
            [aToB],
            out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "B",
                [ManualDependencySelection.Existing(a.Id, "A")]));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ManualEntityCreationDiagnosticCode.CycleDetected);
        Assert.Null(store.LastChangeSet);
    }

    private static ManualEntityCreationService Service(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved,
        out RecordingStore store)
    {
        store = new RecordingStore();
        return new ManualEntityCreationService(
            new StubEntityRepository(entities),
            new StubDependencyRepository(dependencies, unresolved),
            new DependencyRanker(),
            store);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        EntityLifecycleState lifecycle = EntityLifecycleState.Active) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            lifecycleState: lifecycle);

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(entities);

        public Task<bool> TryAddAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateSchemaMetadataAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateProgressAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved) : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(unresolved);

        public Task SaveAsync(
            PersistedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveUnresolvedAsync(
            PersistedUnresolvedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingStore : ITrackedSchemaStore
    {
        public TrackedSchemaChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackedSchemaChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }
    }
}
