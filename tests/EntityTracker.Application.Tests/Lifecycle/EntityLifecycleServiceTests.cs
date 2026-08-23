using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Lifecycle;

public sealed class EntityLifecycleServiceTests
{
    [Fact]
    public async Task TryArchiveAsync_ActiveEntity_WritesOnlyArchiveChange()
    {
        TrackedEntity entity = new(EntityId.New(), "Customer");
        RecordingStore store = new();
        EntityLifecycleService service = CreateService([entity], store);

        bool archived = await service.TryArchiveAsync(entity.Id);

        Assert.True(archived);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(
            store.LastChangeSet);
        Assert.Equal(entity.Id, Assert.Single(changeSet.EntityIdsToArchive));
        Assert.Empty(changeSet.EntityIdsToRestore);
        Assert.Empty(changeSet.EntitiesToAdd);
        Assert.Empty(changeSet.EntitiesToUpdate);
        Assert.Empty(changeSet.ReconciledOwnerIds);
        Assert.Empty(changeSet.ReconciledOverrideOwnerIds);
    }

    [Fact]
    public async Task TryArchiveAsync_MissingOrArchivedEntity_ReturnsFalseWithoutWrite()
    {
        TrackedEntity archivedEntity = new(
            EntityId.New(),
            "Legacy",
            lifecycleState: EntityLifecycleState.Archived);
        RecordingStore store = new();
        EntityLifecycleService service = CreateService([archivedEntity], store);

        Assert.False(await service.TryArchiveAsync(EntityId.New()));
        Assert.False(await service.TryArchiveAsync(archivedEntity.Id));
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task RestoreAsync_ArchivedEntity_RestoresSameIdentityAfterCandidateValidation()
    {
        TrackedEntity archivedEntity = new(
            EntityId.New(),
            "Legacy",
            DevelopmentStatus.Reconciled,
            "Keep notes",
            EntityLifecycleState.Archived,
            EntityProvenance.ManualAndImported);
        RecordingStore store = new();
        EntityLifecycleService service = CreateService([archivedEntity], store);

        EntityRestorationResult result = await service.RestoreAsync(archivedEntity.Id);

        Assert.True(result.IsSuccess);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(
            store.LastChangeSet);
        Assert.Equal(archivedEntity.Id, Assert.Single(changeSet.EntityIdsToRestore));
        Assert.Empty(changeSet.EntitiesToAdd);
    }

    [Fact]
    public async Task RestoreAsync_ActiveOrMissingEntity_ReturnsExpectedFailureWithoutWrite()
    {
        TrackedEntity activeEntity = new(EntityId.New(), "Active");
        RecordingStore store = new();
        EntityLifecycleService service = CreateService([activeEntity], store);

        EntityRestorationResult activeResult = await service.RestoreAsync(activeEntity.Id);
        EntityRestorationResult missingResult = await service.RestoreAsync(EntityId.New());

        Assert.False(activeResult.IsSuccess);
        Assert.False(missingResult.IsSuccess);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task RestoreAsync_CandidateWouldIntroduceCycle_ReturnsFailureWithoutWrite()
    {
        TrackedEntity archived = new(
            EntityId.New(),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        TrackedEntity active = new(EntityId.New(), "Active");
        PersistedDependency[] dependencies =
        [
            new(new DependencyEdge(archived.Id, active.Id), ImportedDependencyKind.Mandatory),
            new(new DependencyEdge(active.Id, archived.Id), ImportedDependencyKind.Mandatory)
        ];
        RecordingStore store = new();
        EntityLifecycleService service = CreateService([archived, active], store, dependencies);

        EntityRestorationResult result = await service.RestoreAsync(archived.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error =>
            error.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Null(store.LastChangeSet);
    }

    private static EntityLifecycleService CreateService(
        IReadOnlyList<TrackedEntity> entities,
        RecordingStore store,
        IReadOnlyList<PersistedDependency>? dependencies = null,
        IReadOnlyList<PersistedUnresolvedDependency>? unresolved = null,
        IReadOnlyList<ManualDependencyOverride>? overrides = null) =>
        new(
            new StubEntityRepository(entities),
            new StubDependencyRepository(dependencies ?? [], unresolved ?? []),
            new StubOverrideRepository(overrides ?? []),
            store,
            new EffectiveDependencyResolver(),
            new DependencyRanker());

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

    private sealed class StubOverrideRepository(IReadOnlyList<ManualDependencyOverride> overrides)
        : IManualDependencyOverrideRepository
    {
        public Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(overrides);
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
    }
}
