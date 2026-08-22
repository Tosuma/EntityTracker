using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Lifecycle;

public sealed class EntityArchivalServiceTests
{
    [Fact]
    public async Task TryArchiveAsync_ActiveEntity_WritesOnlyArchiveChange()
    {
        TrackedEntity entity = new(EntityId.New(), "Customer");
        RecordingStore store = new();
        EntityArchivalService service = new(new StubEntityRepository(entity), store);

        bool archived = await service.TryArchiveAsync(entity.Id);

        Assert.True(archived);
        TrackedSchemaChangeSet changeSet = Assert.IsType<TrackedSchemaChangeSet>(
            store.LastChangeSet);
        Assert.Equal(entity.Id, Assert.Single(changeSet.EntityIdsToArchive));
        Assert.Empty(changeSet.EntitiesToAdd);
        Assert.Empty(changeSet.EntitiesToUpdate);
        Assert.Empty(changeSet.ReconciledOwnerIds);
        Assert.Empty(changeSet.ReconciledOverrideOwnerIds);
    }

    [Fact]
    public async Task TryArchiveAsync_MissingEntity_ReturnsFalseWithoutWrite()
    {
        RecordingStore store = new();
        EntityArchivalService service = new(
            new StubEntityRepository(null),
            store);

        bool archived = await service.TryArchiveAsync(EntityId.New());

        Assert.False(archived);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task TryArchiveAsync_AlreadyArchivedEntity_ReturnsFalseWithoutWrite()
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "Legacy",
            lifecycleState: EntityLifecycleState.Archived);
        RecordingStore store = new();
        EntityArchivalService service = new(new StubEntityRepository(entity), store);

        bool archived = await service.TryArchiveAsync(entity.Id);

        Assert.False(archived);
        Assert.Null(store.LastChangeSet);
    }

    private sealed class StubEntityRepository(TrackedEntity? entity) : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entity?.Id == id ? entity : null);

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrackedEntity>>(entity is null ? [] : [entity]);

        public Task<bool> TryAddAsync(
            TrackedEntity entityToAdd,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateSchemaMetadataAsync(
            TrackedEntity entityToUpdate,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateProgressAsync(
            TrackedEntity entityToUpdate,
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
