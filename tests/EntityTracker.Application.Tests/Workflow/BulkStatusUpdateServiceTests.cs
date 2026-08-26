using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Workflow;

public sealed class BulkStatusUpdateServiceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ApplyAsync_MixedSelection_UpdatesWholeSelectionOnce(int selectedCount)
    {
        TrackedEntity[] entities = Enumerable.Range(1, selectedCount)
            .Select(index => Entity(
                index,
                $"Entity {index}",
                index == selectedCount
                    ? DevelopmentStatus.InProgress
                    : index % 2 == 0
                        ? DevelopmentStatus.DevelopmentCompleted
                        : DevelopmentStatus.NotStarted))
            .ToArray();
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService(entities, [], store);

        BulkStatusUpdateResult result = await service.ApplyAsync(
            entities.Select(static entity => entity.Id).ToArray(),
            DevelopmentStatus.InProgress);

        Assert.Equal(selectedCount - 1, result.ChangedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(selectedCount, result.SelectedCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        Assert.Equal(selectedCount - 1, changeSet.EntitiesWithProgressToUpdate.Count);
        Assert.All(
            changeSet.EntitiesWithProgressToUpdate,
            entity => Assert.Equal(DevelopmentStatus.InProgress, entity.Status));
        Assert.DoesNotContain(
            changeSet.EntitiesWithProgressToUpdate,
            entity => entity.Id == entities[^1].Id);
        Assert.Equal(1, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_DuplicateIds_NormalizesBeforePersistence()
    {
        TrackedEntity first = Entity(1, "First");
        TrackedEntity second = Entity(2, "Second");
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService([first, second], [], store);

        BulkStatusUpdateResult result = await service.ApplyAsync(
            [first.Id, first.Id, second.Id, second.Id],
            DevelopmentStatus.InProgress);

        Assert.Equal(2, result.ChangedCount);
        Assert.Equal(2, result.SelectedCount);
        Assert.Equal(
            2,
            Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet)
                .EntitiesWithProgressToUpdate.Count);
    }

    [Fact]
    public async Task ApplyAsync_AllAlreadyMatch_ReturnsUnchangedWithoutWriting()
    {
        TrackedEntity first = Entity(1, "First", DevelopmentStatus.Reconciled);
        TrackedEntity second = Entity(2, "Second", DevelopmentStatus.Reconciled);
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService([first, second], [], store);

        BulkStatusUpdateResult result = await service.ApplyAsync(
            [first.Id, second.Id],
            DevelopmentStatus.Reconciled);

        Assert.Equal(0, result.ChangedCount);
        Assert.Equal(2, result.UnchangedCount);
        Assert.Equal(0, store.ApplyCount);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task ApplyAsync_ArchivedSelection_ValidatesBeforeWritingAnything()
    {
        TrackedEntity active = Entity(1, "Active");
        TrackedEntity archived = Entity(
            2,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService([active, archived], [], store);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                [active.Id, archived.Id],
                DevelopmentStatus.InProgress));

        Assert.Contains("no longer active", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_MissingSelection_ValidatesBeforeWritingAnything()
    {
        TrackedEntity active = Entity(1, "Active");
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService([active], [], store);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                [active.Id, Id(99)],
                DevelopmentStatus.InProgress));

        Assert.Contains("no longer exists", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_CompletingDependencies_CalculatesOneReadyFinalState()
    {
        TrackedEntity firstDependency = Entity(1, "First dependency");
        TrackedEntity secondDependency = Entity(2, "Second dependency");
        TrackedEntity owner = Entity(3, "Owner");
        PersistedDependency[] dependencies =
        [
            Dependency(owner, firstDependency),
            Dependency(owner, secondDependency)
        ];
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService(
            [firstDependency, secondDependency, owner],
            dependencies,
            store);

        await service.ApplyAsync(
            [firstDependency.Id, secondDependency.Id],
            DevelopmentStatus.DevelopmentCompleted);

        ProgressSnapshotState snapshot = Assert.IsType<ProgressSnapshotState>(
            store.LastChangeSet?.ProgressSnapshotAfterChanges);
        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.BlockedCount);
        Assert.Equal(2, snapshot.DevelopmentCompletedCount);
    }

    [Fact]
    public async Task ApplyAsync_MovingDependenciesBack_CalculatesOneBlockedFinalState()
    {
        TrackedEntity firstDependency = Entity(
            1,
            "First dependency",
            DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity secondDependency = Entity(
            2,
            "Second dependency",
            DevelopmentStatus.Reconciled);
        TrackedEntity owner = Entity(3, "Owner");
        PersistedDependency[] dependencies =
        [
            Dependency(owner, firstDependency),
            Dependency(owner, secondDependency)
        ];
        RecordingStore store = new();
        BulkStatusUpdateService service = CreateService(
            [firstDependency, secondDependency, owner],
            dependencies,
            store);

        await service.ApplyAsync(
            [firstDependency.Id, secondDependency.Id],
            DevelopmentStatus.InProgress);

        ProgressSnapshotState snapshot = Assert.IsType<ProgressSnapshotState>(
            store.LastChangeSet?.ProgressSnapshotAfterChanges);
        Assert.Equal(0, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.BlockedCount);
        Assert.Equal(2, snapshot.InProgressCount);
    }

    private static BulkStatusUpdateService CreateService(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        RecordingStore store) =>
        new(
            new StubEntityRepository(entities),
            new StubDependencyRepository(dependencies),
            new StubOverrideRepository(),
            new EffectiveDependencyResolver(),
            store);

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        EntityLifecycleState lifecycleState = EntityLifecycleState.Active) =>
        new(Id(id), name, status, lifecycleState: lifecycleState);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target) =>
        new(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Mandatory);

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
        IReadOnlyList<PersistedDependency> dependencies) : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedUnresolvedDependency>>([]);
    }

    private sealed class StubOverrideRepository : IManualDependencyOverrideRepository
    {
        public Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManualDependencyOverride>>([]);
    }

    private sealed class RecordingStore : ITrackedStateStore
    {
        public int ApplyCount { get; private set; }

        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            IEnumerable<TrackedEntity> entities,
            ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
