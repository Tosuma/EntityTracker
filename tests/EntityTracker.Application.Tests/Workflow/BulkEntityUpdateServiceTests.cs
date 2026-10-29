using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Workflow;

public sealed class BulkEntityUpdateServiceTests
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
        BulkEntityUpdateService service = CreateService(entities, [], store);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            entities.Select(static entity => entity.Id).ToArray(),
            new BulkEntityUpdate(DevelopmentStatus.InProgress));

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
        BulkEntityUpdateService service = CreateService([first, second], [], store);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            [first.Id, first.Id, second.Id, second.Id],
            new BulkEntityUpdate(DevelopmentStatus.InProgress));

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
        BulkEntityUpdateService service = CreateService([first, second], [], store);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            [first.Id, second.Id],
            new BulkEntityUpdate(DevelopmentStatus.Reconciled));

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
        BulkEntityUpdateService service = CreateService([active, archived], [], store);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                [active.Id, archived.Id],
                new BulkEntityUpdate(DevelopmentStatus.InProgress)));

        Assert.Contains("no longer active", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_MissingSelection_ValidatesBeforeWritingAnything()
    {
        TrackedEntity active = Entity(1, "Active");
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([active], [], store);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                [active.Id, Id(99)],
                new BulkEntityUpdate(DevelopmentStatus.InProgress)));

        Assert.Contains("no longer exists", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_StatusChangePreservesManualScalarValues()
    {
        TrackedEntity entity = Entity(
            1,
            "Prioritized",
            requestedPriority: 3,
            responsibleDeveloper: "Core Team",
            groupName: "Core Data");
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([entity], [], store);

        await service.ApplyAsync([entity.Id], new BulkEntityUpdate(DevelopmentStatus.InProgress));

        TrackedEntity update = Assert.Single(
            Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet)
                .EntitiesWithProgressToUpdate);
        Assert.Equal(3, update.RequestedPriority);
        Assert.Equal("Core Team", update.ResponsibleDeveloper);
        Assert.Equal("Core Data", update.GroupName);
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
        BulkEntityUpdateService service = CreateService(
            [firstDependency, secondDependency, owner],
            dependencies,
            store);

        await service.ApplyAsync(
            [firstDependency.Id, secondDependency.Id],
            new BulkEntityUpdate(DevelopmentStatus.DevelopmentCompleted));

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
        BulkEntityUpdateService service = CreateService(
            [firstDependency, secondDependency, owner],
            dependencies,
            store);

        await service.ApplyAsync(
            [firstDependency.Id, secondDependency.Id],
            new BulkEntityUpdate(DevelopmentStatus.InProgress));

        ProgressSnapshotState snapshot = Assert.IsType<ProgressSnapshotState>(
            store.LastChangeSet?.ProgressSnapshotAfterChanges);
        Assert.Equal(0, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.BlockedCount);
        Assert.Equal(2, snapshot.InProgressCount);
    }

    [Fact]
    public async Task ApplyAsync_PrioritySetAndClear_UpdatesOnlyEntitiesThatDiffer()
    {
        TrackedEntity unset = Entity(1, "Unset");
        TrackedEntity matching = Entity(2, "Matching", requestedPriority: 2);
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([unset, matching], [], store);

        BulkEntityUpdateResult set = await service.ApplyAsync(
            [unset.Id, matching.Id],
            new BulkEntityUpdate(Priority: new RequestedPriorityChange(2)));

        Assert.Equal(1, set.ChangedCount);
        Assert.Equal(1, set.UnchangedCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        TrackedEntity update = Assert.Single(changeSet.EntitiesWithRequestedPriorityToUpdate);
        Assert.Equal(unset.Id, update.Id);
        Assert.Equal(2, update.RequestedPriority);
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
        Assert.Empty(changeSet.EntitiesWithDueDateToUpdate);
        Assert.Null(changeSet.ProgressSnapshotAfterChanges);

        await service.ApplyAsync(
            [unset.Id, matching.Id],
            new BulkEntityUpdate(Priority: new RequestedPriorityChange(null)));

        TrackedEntity cleared = Assert.Single(store.LastChangeSet!.EntitiesWithRequestedPriorityToUpdate);
        Assert.Equal(matching.Id, cleared.Id);
        Assert.Null(cleared.RequestedPriority);
    }

    [Fact]
    public async Task ApplyAsync_DueDateSetAndClear_UpdatesOnlyEntitiesThatDiffer()
    {
        DateOnly date = new(2026, 11, 15);
        TrackedEntity unset = Entity(1, "Unset");
        TrackedEntity dated = Entity(2, "Dated", dueDate: date);
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([unset, dated], [], store);

        await service.ApplyAsync(
            [unset.Id, dated.Id],
            new BulkEntityUpdate(DueDate: new DueDateChange(date)));

        TrackedEntity update = Assert.Single(store.LastChangeSet!.EntitiesWithDueDateToUpdate);
        Assert.Equal(unset.Id, update.Id);
        Assert.Equal(date, update.DueDate);

        await service.ApplyAsync(
            [unset.Id, dated.Id],
            new BulkEntityUpdate(DueDate: new DueDateChange(null)));

        TrackedEntity cleared = Assert.Single(store.LastChangeSet!.EntitiesWithDueDateToUpdate);
        Assert.Equal(dated.Id, cleared.Id);
        Assert.Null(cleared.DueDate);
    }

    [Fact]
    public async Task ApplyAsync_AddDeveloper_SkipsEntitiesTheyAreAlreadyResponsibleFor()
    {
        DeveloperId developer = DeveloperId.New();
        TrackedEntity assigned = Entity(1, "Assigned");
        TrackedEntity unassigned = Entity(2, "Unassigned");
        TrackedEntity formerly = Entity(3, "Formerly");
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([assigned, unassigned, formerly], [], store,
        [
            Period(assigned, developer),
            Period(formerly, developer, ended: true)
        ]);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            [assigned.Id, unassigned.Id, formerly.Id],
            new BulkEntityUpdate(Responsibility: new BulkResponsibilityChange(developer, Add: true)));

        Assert.Equal(2, result.ChangedCount);
        Assert.Equal(1, result.UnchangedCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        Assert.Equal(
            [unassigned.Id, formerly.Id],
            changeSet.ResponsibilityAdditions.Select(static addition => addition.EntityId));
        Assert.All(changeSet.ResponsibilityAdditions, addition => Assert.Equal(developer, addition.DeveloperId));
        Assert.Empty(changeSet.ResponsibilityRemovals);
        Assert.Empty(changeSet.ResponsibilitySelections);
    }

    [Fact]
    public async Task ApplyAsync_RemoveDeveloper_OnlyEndsTheirCurrentResponsibility()
    {
        DeveloperId developer = DeveloperId.New();
        DeveloperId colleague = DeveloperId.New();
        TrackedEntity assigned = Entity(1, "Assigned");
        TrackedEntity colleagueOnly = Entity(2, "Colleague only");
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([assigned, colleagueOnly], [], store,
        [
            Period(assigned, developer),
            Period(assigned, colleague),
            Period(colleagueOnly, colleague)
        ]);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            [assigned.Id, colleagueOnly.Id],
            new BulkEntityUpdate(Responsibility: new BulkResponsibilityChange(developer, Add: false)));

        Assert.Equal(1, result.ChangedCount);
        ResponsibilityRemoval removal = Assert.Single(store.LastChangeSet!.ResponsibilityRemovals);
        Assert.Equal(new ResponsibilityRemoval(assigned.Id, developer), removal);
        Assert.Empty(store.LastChangeSet.ResponsibilityAdditions);
    }

    [Fact]
    public async Task ApplyAsync_SeveralFields_WritesThemInOneChangeSet()
    {
        DeveloperId developer = DeveloperId.New();
        DateOnly date = new(2026, 12, 1);
        TrackedEntity first = Entity(1, "First", groupName: "Core Data");
        TrackedEntity second = Entity(2, "Second", DevelopmentStatus.Blocked, requestedPriority: 4);
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([first, second], [], store, []);

        BulkEntityUpdateResult result = await service.ApplyAsync(
            [first.Id, second.Id],
            new BulkEntityUpdate(
                DevelopmentStatus.Blocked,
                new RequestedPriorityChange(1),
                new DueDateChange(date),
                new BulkResponsibilityChange(developer, Add: true)));

        Assert.Equal(2, result.ChangedCount);
        Assert.Equal(1, store.ApplyCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        Assert.Equal(first.Id, Assert.Single(changeSet.EntitiesWithProgressToUpdate).Id);
        Assert.Equal(2, changeSet.EntitiesWithRequestedPriorityToUpdate.Count);
        Assert.Equal(2, changeSet.EntitiesWithDueDateToUpdate.Count);
        Assert.Equal(2, changeSet.ResponsibilityAdditions.Count);
        Assert.NotNull(changeSet.ProgressSnapshotAfterChanges);
        TrackedEntity firstUpdate = changeSet.EntitiesWithDueDateToUpdate.Single(entity => entity.Id == first.Id);
        Assert.Equal(DevelopmentStatus.Blocked, firstUpdate.Status);
        Assert.Equal(1, firstUpdate.RequestedPriority);
        Assert.Equal(date, firstUpdate.DueDate);
        Assert.Equal("Core Data", firstUpdate.GroupName);
    }

    [Fact]
    public async Task ApplyAsync_InvalidRequests_AreRejectedBeforeWriting()
    {
        TrackedEntity entity = Entity(1, "Entity");
        RecordingStore store = new();
        BulkEntityUpdateService service = CreateService([entity], [], store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ApplyAsync([entity.Id], new BulkEntityUpdate()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ApplyAsync([entity.Id], new BulkEntityUpdate(Priority: new RequestedPriorityChange(6))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ApplyAsync([entity.Id], new BulkEntityUpdate((DevelopmentStatus)99)));

        Assert.Equal(0, store.ApplyCount);
    }

    private static BulkEntityUpdateService CreateService(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        RecordingStore store,
        IReadOnlyList<ResponsibilityPeriod> periods) =>
        new(
            new StubEntityRepository(entities),
            new StubDependencyRepository(TestDependencies.Named(entities, dependencies)),
            new EffectiveDependencyResolver(),
            store,
            new StubResponsibilityPeriodRepository(periods));

    private static ResponsibilityPeriod Period(TrackedEntity entity, DeveloperId developer, bool ended = false) =>
        new(Guid.NewGuid(), entity.Id, developer, DateTimeOffset.UnixEpoch,
            ended ? DateTimeOffset.UnixEpoch.AddDays(1) : null);

    private static BulkEntityUpdateService CreateService(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        RecordingStore store) =>
        new(
            new StubEntityRepository(entities),
            new StubDependencyRepository(TestDependencies.Named(entities, dependencies)),
            new EffectiveDependencyResolver(),
            store);

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        EntityLifecycleState lifecycleState = EntityLifecycleState.Active,
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null,
        DateOnly? dueDate = null) =>
        new(
            Id(id),
            TestTrackerId,
            name,
            status,
            lifecycleState: lifecycleState,
            requestedPriority: requestedPriority,
            responsibleDeveloper: responsibleDeveloper,
            groupName: groupName,
            dueDate: dueDate);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target) =>
        new(
            new DependencyEdge(owner.Id, target.Id));

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            TrackerId trackerId,
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) => Task.FromResult(entities);
    }

    private sealed class StubResponsibilityPeriodRepository(IReadOnlyList<ResponsibilityPeriod> periods)
        : IResponsibilityPeriodRepository
    {
        public Task<IReadOnlyList<ResponsibilityPeriod>> GetByEntityAsync(
            EntityId entityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResponsibilityPeriod>>(
                periods.Where(period => period.EntityId == entityId).ToArray());

        public Task<IReadOnlyList<ResponsibilityPeriod>> GetByTrackerAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) => Task.FromResult(periods);
    }

    private sealed class RecordingStore : ITrackedStateStore
    {
        public int ApplyCount { get; private set; }

        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackerId trackerId,
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            TrackerId trackerId,
            IEnumerable<TrackedEntity> entities,
            ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
