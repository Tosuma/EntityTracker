using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
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
    public async Task SearchGroupNamesAsync_MatchesOrdersAndDeduplicatesActiveAndArchivedGroups()
    {
        ManualEntityCreationService service = Service(
            [
                Entity(1, "One", groupName: "Data"),
                Entity(2, "Two", groupName: "database"),
                Entity(3, "Three", groupName: "Metadata"),
                Entity(4, "Four", groupName: "data"),
                Entity(
                    5,
                    "Archived",
                    EntityLifecycleState.Archived,
                    "Legacy Data")
            ],
            [],
            [],
            out _);

        IReadOnlyList<string> suggestions = await service.SearchGroupNamesAsync(" DATA ");

        Assert.Equal(["Data", "database", "Legacy Data", "Metadata"], suggestions);
    }

    [Fact]
    public async Task SearchGroupNamesAsync_ReturnsArchivedOnlyGroupAndLimitsResultsToTen()
    {
        TrackedEntity[] entities = Enumerable.Range(1, 12)
            .Select(index => Entity(index, $"Entity{index:00}", groupName: $"Team{index:00}"))
            .Append(Entity(
                20,
                "Archived",
                EntityLifecycleState.Archived,
                "Team00"))
            .ToArray();
        ManualEntityCreationService service = Service(entities, [], [], out _);

        IReadOnlyList<string> suggestions = await service.SearchGroupNamesAsync("team");

        Assert.Equal(10, suggestions.Count);
        Assert.Equal("Team00", suggestions[0]);
        Assert.Equal("Team09", suggestions[9]);
        Assert.Empty(await service.SearchGroupNamesAsync("   "));
    }

    [Fact]
    public async Task CreateAsync_WithoutDependencies_AddsManualOnlyEntity()
    {
        ManualEntityCreationService service = Service([], [], [], out RecordingStore store);

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                " NewEntity ",
                [],
                "  Platform Team  ",
                "  Core Data  "));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        TrackedEntity added = Assert.Single(store.LastChangeSet!.EntitiesToAdd);
        Assert.Equal("NewEntity", added.SourceName);
        Assert.Equal(DevelopmentStatus.NotStarted, added.Status);
        Assert.Equal(EntityLifecycleState.Active, added.LifecycleState);
        Assert.Equal(EntityProvenance.ManualOnly, added.Provenance);
        Assert.Equal("Platform Team", added.ResponsibleDeveloper);
        Assert.Equal("Core Data", added.GroupName);
        Assert.Equal(added.Id, result.CreatedEntityId);
        Assert.Empty(store.LastChangeSet.ResolvedDependencies);
        Assert.Empty(store.LastChangeSet.UnresolvedDependencies);
        ProgressSnapshotState snapshot = Assert.IsType<ProgressSnapshotState>(
            store.LastChangeSet.ProgressSnapshotAfterChanges);
        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.TotalActiveCount);
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
        Assert.Empty(store.LastChangeSet.ResolvedDependencies);
        Assert.Empty(store.LastChangeSet.UnresolvedDependencies);
        ManualDependencyOverride[] overrides =
            store.LastChangeSet.ManualDependencyOverrides.ToArray();
        Assert.Contains(overrides, item =>
            item.DependentEntityId == added.Id &&
            item.DependencySourceName == known.SourceName &&
            item.Action == ManualDependencyOverrideAction.Add);
        Assert.Contains(overrides, item =>
            item.DependentEntityId == added.Id &&
            item.DependencySourceName == "Future" &&
            item.Action == ManualDependencyOverrideAction.Add);
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
        ManualDependencyOverride dependencyOverride = Assert.Single(
            store.LastChangeSet!.ManualDependencyOverrides);
        Assert.Equal(ManualDependencyOverrideAction.Add, dependencyOverride.Action);
        Assert.Equal("Known", dependencyOverride.DependencySourceName);
        Assert.Empty(store.LastChangeSet.ResolvedDependencies);
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
        if (lifecycle == EntityLifecycleState.Archived)
        {
            Assert.Equal(existing.Id, result.ArchivedEntityMatch?.EntityId);
            Assert.Equal(existing.SourceName, result.ArchivedEntityMatch?.SourceName);
        }
        else
        {
            Assert.Null(result.ArchivedEntityMatch);
        }

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
            new StubManualDependencyOverrideRepository(),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            store);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        EntityLifecycleState lifecycle = EntityLifecycleState.Active,
        string? groupName = null) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            lifecycleState: lifecycle,
            groupName: groupName);

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(entities);

    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved) : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(unresolved);

    }

    private sealed class RecordingStore : ITrackedStateStore
    {
        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            IEnumerable<TrackedEntity> entities,
            EntityTracker.Application.History.ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
