using EntityTracker.Application.Importing;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Overview;

public sealed class EntityOverviewServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsRankedPersistedDetailsAndDependencyCounts()
    {
        TrackedEntity foundation = Entity(1, "Foundation", DevelopmentStatus.Completed, "Stable");
        TrackedEntity service = Entity(2, "Service", DevelopmentStatus.InProgress, "API underway");
        TrackedEntity screen = Entity(3, "Screen");
        PersistedDependency[] dependencies =
        [
            Dependency(service, foundation, ImportedDependencyKind.Optional),
            Dependency(screen, service, ImportedDependencyKind.Mandatory)
        ];
        EntityOverviewService serviceUnderTest = CreateService(
            [screen, service, foundation],
            dependencies.Reverse());

        EntityOverviewResult result = await serviceUnderTest.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["Foundation", "Service", "Screen"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, 2, 3], result.Items.Select(static item => item.Rank));
        Assert.Equal([0, 1, 1], result.Items.Select(static item => item.DependencyCount));
        Assert.Empty(result.Items[0].DependencyNames);
        Assert.Equal(["Foundation"], result.Items[1].DependencyNames);
        Assert.Equal(["Service"], result.Items[2].DependencyNames);
        Assert.Equal(DevelopmentStatus.Completed, result.Items[0].Status);
        Assert.Equal("Stable", result.Items[0].Notes);
        Assert.Equal(DevelopmentStatus.InProgress, result.Items[1].Status);
        Assert.Equal("API underway", result.Items[1].Notes);
    }

    [Fact]
    public async Task GetAsync_EmptyRepositories_ReturnsSuccessfulEmptyResult()
    {
        EntityOverviewService service = CreateService([], []);

        EntityOverviewResult result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task GetAsync_DependencyNamesReflectEffectiveManualOverrides()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity suppressed = Entity(2, "Suppressed");
        TrackedEntity retained = Entity(3, "Retained");
        EntityOverviewService service = CreateService(
            [owner, suppressed, retained],
            [
                Dependency(owner, suppressed, ImportedDependencyKind.Mandatory),
                Dependency(owner, retained, ImportedDependencyKind.Optional)
            ],
            overrides:
            [
                new ManualDependencyOverride(
                    owner.Id,
                    suppressed.SourceName,
                    ManualDependencyOverrideAction.Suppress),
                new ManualDependencyOverride(
                    owner.Id,
                    "Manual Missing",
                    ManualDependencyOverrideAction.Add)
            ]);

        EntityOverviewResult result = await service.GetAsync();

        EntityOverviewItem ownerItem = result.Items.Single(item => item.EntityId == owner.Id);
        Assert.Equal(["Manual Missing", "Retained"], ownerItem.DependencyNames);
        Assert.Equal(2, ownerItem.DependencyCount);
    }

    [Fact]
    public async Task GetAsync_ArchivedEntitiesAreExcludedFromTheEffectiveOverview()
    {
        TrackedEntity active = Entity(1, "Active");
        TrackedEntity archived = new(
            new EntityId(new Guid(2, 0, 0, new byte[8])),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        EntityOverviewService service = CreateService([active, archived], []);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal("Active", Assert.Single(result.Items).SourceName);
    }

    [Fact]
    public async Task GetAsync_Cycle_ReturnsDiagnosticAndNoRows()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        EntityOverviewService service = CreateService(
            [alpha, beta],
            [
                Dependency(alpha, beta, ImportedDependencyKind.Mandatory),
                Dependency(beta, alpha, ImportedDependencyKind.Mandatory)
            ]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
    }

    [Fact]
    public async Task GetAsync_UnknownEntityReference_ReturnsDiagnosticAndNoRows()
    {
        TrackedEntity known = Entity(1, "Known");
        TrackedEntity unknown = Entity(2, "Unknown");
        EntityOverviewService service = CreateService(
            [known],
            [Dependency(known, unknown, ImportedDependencyKind.Optional)]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.UnknownEntity);
    }

    [Fact]
    public async Task GetAsync_ReturnsDirectlyUnresolvedAndTransitivelyBlockedRowsWithoutRanks()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity safe = Entity(3, "Safe", DevelopmentStatus.Completed);
        EntityOverviewService service = CreateService(
            [beta, safe, alpha],
            [Dependency(beta, alpha, ImportedDependencyKind.Mandatory)],
            [Unresolved(alpha, "MissingX", ImportedDependencyKind.Optional)]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Safe", "Alpha", "Beta"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, null, null], result.Items.Select(static item => item.Rank));
        Assert.Equal(
            [
                DependencyResolutionState.Resolved,
                DependencyResolutionState.Unresolved,
                DependencyResolutionState.Blocked
            ],
            result.Items.Select(static item => item.DependencyState));
        Assert.Equal([0, 1, 1], result.Items.Select(static item => item.DependencyCount));
        Assert.Empty(result.Items[0].DependencyNames);
        Assert.Equal(["MissingX"], result.Items[1].DependencyNames);
        Assert.Equal(["Alpha"], result.Items[2].DependencyNames);
        Assert.Empty(result.Items[0].MissingDependencyNames);
        Assert.Equal(["MissingX"], result.Items[1].MissingDependencyNames);
        Assert.Equal(["MissingX"], result.Items[2].MissingDependencyNames);
    }

    private static EntityOverviewService CreateService(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> dependencies,
        IEnumerable<PersistedUnresolvedDependency>? unresolvedDependencies = null,
        IEnumerable<ManualDependencyOverride>? overrides = null)
    {
        return new EntityOverviewService(
            new StubEntityRepository(entities.ToArray()),
            new StubDependencyRepository(
                dependencies.ToArray(),
                unresolvedDependencies?.ToArray() ?? []),
            new StubManualDependencyOverrideRepository(overrides?.ToArray()),
            new DependencyRanker(),
            new EffectiveDependencyResolver());
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "")
    {
        return new TrackedEntity(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            status,
            notes);
    }

    private static PersistedDependency Dependency(
        TrackedEntity dependent,
        TrackedEntity dependency,
        ImportedDependencyKind kind)
    {
        return new PersistedDependency(
            new DependencyEdge(dependent.Id, dependency.Id),
            kind);
    }

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity dependent,
        string dependencySourceName,
        ImportedDependencyKind kind)
    {
        return new PersistedUnresolvedDependency(
            new UnresolvedDependency(dependent.Id, dependencySourceName),
            kind);
    }

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
        IReadOnlyList<PersistedUnresolvedDependency> unresolvedDependencies)
        : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(unresolvedDependencies);

        public Task SaveAsync(
            PersistedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveUnresolvedAsync(
            PersistedUnresolvedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
