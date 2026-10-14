using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqliteTrackedStateStoreTests
{
    [Fact]
    public async Task HistoryBaseline_IsTruthfulAndIdempotent()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Existing",
            DevelopmentStatus.ReworkNeeded);
        Assert.True(await new SqliteEntityRepository(database).TryAddAsync(entity));
        SqliteTrackedStateStore store = new(database);
        ProgressSnapshotState state = new(0, 0, 0, 1, 0, 0);

        await store.EnsureHistoryBaselineAsync([entity], state);
        time.Advance(TimeSpan.FromDays(1));
        await store.EnsureHistoryBaselineAsync([entity], state);

        SqliteProgressHistoryRepository history = new(database);
        EntityStatusHistoryEntry entry = Assert.Single(await history.GetStatusHistoryAsync());
        Assert.Equal(StatusHistoryEntryKind.Baseline, entry.Kind);
        Assert.Equal(DevelopmentStatus.ReworkNeeded, entry.NewStatus);
        ProgressSnapshot snapshot = Assert.Single(await history.GetProgressSnapshotsAsync());
        Assert.Equal(state, snapshot.State);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), snapshot.RecordedAtUtc);
    }

    [Fact]
    public async Task ApplyAsync_RecordsCreationAndRealStatusTransitionsButNotNotesOnlyChanges()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteTrackedStateStore store = new(database);
        TrackedEntity entity = new(EntityId.New(), database.GetTrackerId(), "New");

        await store.ApplyAsync(new TrackedStateChangeSet(
            [entity], [], [],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0)));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [],
            entitiesWithProgressToUpdate:
            [new TrackedEntity(
                entity.Id,
                database.GetTrackerId(),
                entity.SourceName,
                entity.Status,
                "Notes only")],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0)));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [],
            entitiesWithProgressToUpdate:
            [new TrackedEntity(
                entity.Id,
                database.GetTrackerId(),
                entity.SourceName,
                DevelopmentStatus.InProgress,
                "Notes only")],
            progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 1, 0, 0, 0)));

        SqliteProgressHistoryRepository history = new(database);
        EntityStatusHistoryEntry[] entries = (await history.GetStatusHistoryAsync()).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(StatusHistoryEntryKind.Created, entries[0].Kind);
        Assert.Equal(StatusHistoryEntryKind.Transition, entries[1].Kind);
        Assert.Equal(DevelopmentStatus.NotStarted, entries[1].PreviousStatus);
        Assert.Equal(DevelopmentStatus.InProgress, entries[1].NewStatus);
        ProgressSnapshot[] snapshots = (await history.GetProgressSnapshotsAsync()).ToArray();
        Assert.Equal(2, snapshots.Length);

        SqliteDatabase reopenedDatabase = new(file.DatabasePath, time);
        await reopenedDatabase.InitializeAsync();
        SqliteProgressHistoryRepository reopened = new(reopenedDatabase);
        Assert.Equal(entries, await reopened.GetStatusHistoryAsync());
        Assert.Equal(snapshots, await reopened.GetProgressSnapshotsAsync());
    }

    [Fact]
    public async Task ApplyAsync_BulkProgressChange_UsesOneTimestampAndOneFinalSnapshot()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity[] entities =
        [
            new(EntityId.New(), database.GetTrackerId(), "Not started"),
            new(EntityId.New(), database.GetTrackerId(), "In progress", DevelopmentStatus.InProgress),
            new(EntityId.New(), database.GetTrackerId(), "Rework", DevelopmentStatus.ReworkNeeded),
            new(EntityId.New(), database.GetTrackerId(), "Completed", DevelopmentStatus.DevelopmentCompleted),
            new(EntityId.New(), database.GetTrackerId(), "Reconciled", DevelopmentStatus.Reconciled)
        ];
        foreach (TrackedEntity entity in entities)
        {
            Assert.True(await repository.TryAddAsync(entity));
        }

        await store.EnsureHistoryBaselineAsync(
            entities,
            new ProgressSnapshotState(1, 0, 1, 1, 1, 1));
        time.Advance(TimeSpan.FromHours(3));
        DateTimeOffset operationTime = time.GetUtcNow();
        TrackedEntity[] changed = entities
            .Where(static entity => entity.Status != DevelopmentStatus.InProgress)
            .Select(entity => new TrackedEntity(
                entity.Id,
                database.GetTrackerId(),
                entity.SourceName,
                DevelopmentStatus.InProgress,
                entity.Notes,
                entity.LifecycleState))
            .ToArray();

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithProgressToUpdate: changed,
            progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 5, 0, 0, 0)));

        Assert.All(
            await repository.GetAllAsync(),
            entity => Assert.Equal(DevelopmentStatus.InProgress, entity.Status));
        SqliteProgressHistoryRepository history = new(database);
        EntityStatusHistoryEntry[] transitions = (await history.GetStatusHistoryAsync())
            .Where(static entry => entry.Kind == StatusHistoryEntryKind.Transition)
            .ToArray();
        Assert.Equal(4, transitions.Length);
        Assert.All(transitions, entry => Assert.Equal(operationTime, entry.OccurredAtUtc));
        Assert.Equal(4, transitions.Select(static entry => entry.EntityId).Distinct().Count());
        ProgressSnapshot[] snapshots = (await history.GetProgressSnapshotsAsync()).ToArray();
        Assert.Equal(2, snapshots.Length);
        Assert.Equal(operationTime, snapshots[^1].RecordedAtUtc);
        Assert.Equal(new ProgressSnapshotState(0, 0, 5, 0, 0, 0), snapshots[^1].State);
    }

    [Fact]
    public async Task ApplyAsync_ArchivesAndReconcilesDependenciesWithoutLosingProgress()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity target = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Target",
            DevelopmentStatus.DevelopmentCompleted,
            "Keep target");
        TrackedEntity owner = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Owner",
            DevelopmentStatus.InProgress,
            "Keep owner");
        Assert.True(await entities.TryAddAsync(target));
        Assert.True(await entities.TryAddAsync(owner));
        await dependencies.SaveAsync(new EntityDependency(owner.Id, target.Id));

        TrackedStateChangeSet changeSet = new(
            [],
            [],
            [target.Id],
            [owner.Id],
            [new EntityDependency(owner.Id, target.Id)]);

        await store.ApplyAsync(changeSet);

        TrackedEntity loadedTarget = (await entities.GetAsync(target.Id))!;
        TrackedEntity loadedOwner = (await entities.GetAsync(owner.Id))!;
        Assert.Equal(EntityLifecycleState.Archived, loadedTarget.LifecycleState);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, loadedTarget.Status);
        Assert.Equal("Keep target", loadedTarget.Notes);
        Assert.Equal(EntityLifecycleState.Active, loadedOwner.LifecycleState);
        Assert.Equal(DevelopmentStatus.InProgress, loadedOwner.Status);
        Assert.Equal("Keep owner", loadedOwner.Notes);
        // The dependency keeps its target; with the target archived it is an unresolved reference.
        EntityDependency kept = Assert.Single(await dependencies.GetAllAsync());
        Assert.Equal(target.Id, kept.DependencyEntityId);
        EffectiveDependencyState state = new EffectiveDependencyResolver().Resolve(
            await entities.GetAllAsync(), [kept]);
        Assert.Empty(state.ResolvedDependencies);
        Assert.Equal("Target", Assert.Single(state.UnresolvedDependencies).Dependency.DependencySourceName);
    }

    [Fact]
    public async Task ApplyAsync_FailureRollsBackEarlierChanges()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity existing = new(EntityId.New(), database.GetTrackerId(), "Existing");
        TrackedEntity changedProgress = new(
            existing.Id,
            database.GetTrackerId(),
            existing.SourceName,
            DevelopmentStatus.Reconciled,
            "Must roll back");
        TrackedEntity added = new(EntityId.New(), database.GetTrackerId(), "Added");
        Assert.True(await entities.TryAddAsync(existing));
        // A dependency owned by an entity outside the Tracker makes the whole change set fail.
        EntityDependency invalidDependency = new(EntityId.New(), added.Id);
        TrackedStateChangeSet changeSet = new(
            [added],
            [],
            [],
            [added.Id],
            [invalidDependency],
            entitiesWithProgressToUpdate: [changedProgress]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(changeSet));

        TrackedEntity persisted = Assert.Single(await entities.GetAllAsync());
        Assert.Equal(existing.Id, persisted.Id);
        Assert.Equal(DevelopmentStatus.NotStarted, persisted.Status);
        Assert.Equal(string.Empty, persisted.Notes);
        SqliteProgressHistoryRepository history = new(database);
        Assert.Empty(await history.GetStatusHistoryAsync());
        Assert.Empty(await history.GetProgressSnapshotsAsync());
    }

    [Fact]
    public async Task ApplyAsync_UnchangedProgressDoesNotAdvanceProgressTimestamp()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Entity",
            DevelopmentStatus.InProgress,
            "Same notes");
        Assert.True(await entities.TryAddAsync(entity));
        string originalTimestamp = await ReadProgressTimestampAsync(file.DatabasePath, entity.Id);
        time.Advance(TimeSpan.FromHours(1));

        await new SqliteTrackedStateStore(database).ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithProgressToUpdate: [entity]));

        Assert.Equal(
            originalTimestamp,
            await ReadProgressTimestampAsync(file.DatabasePath, entity.Id));
    }

    [Fact]
    public async Task ApplyAsync_ArchivedOwnerRetainsItsHistoricalOutgoingDependencies()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity owner = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Owner",
            groupName: "Core Data");
        TrackedEntity target = new(EntityId.New(), database.GetTrackerId(), "Target");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(target));
        await dependencies.SaveAsync(new EntityDependency(owner.Id, target.Id));

        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet([], [], [owner.Id]));

        TrackedEntity archived = Assert.IsType<TrackedEntity>(await entities.GetAsync(owner.Id));
        Assert.Equal(EntityLifecycleState.Archived, archived.LifecycleState);
        Assert.Equal("Core Data", archived.GroupName);
        Assert.Single(await dependencies.GetAllAsync());
    }

    [Fact]
    public async Task RestoreAsync_PreservesStateAndResolvesMatchingEffectiveReference()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity target = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Target",
            DevelopmentStatus.Reconciled,
            "Preserved notes",
            EntityLifecycleState.Archived,
            requestedPriority: 3,
            responsibleDeveloper: "Legacy Team",
            groupName: "Legacy Data");
        TrackedEntity owner = new(EntityId.New(), database.GetTrackerId(), "Owner");
        Assert.True(await entities.TryAddAsync(target));
        Assert.True(await entities.TryAddAsync(owner));
        EntityDependency preservedDependency = new(owner.Id, target.Id);
        await dependencies.SaveAsync(preservedDependency);
        EntityLifecycleService lifecycle = new(
            entities,
            dependencies,
            store,
            new EffectiveDependencyResolver(),
            new DependencyRanker());

        EntityRestorationResult restoration = await lifecycle.RestoreAsync(
            database.GetTrackerId(),
            target.Id);

        Assert.True(restoration.IsSuccess);
        TrackedEntity loaded = (await entities.GetAsync(target.Id))!;
        Assert.Equal(target.Id, loaded.Id);
        Assert.Equal(EntityLifecycleState.Active, loaded.LifecycleState);
        Assert.Equal(DevelopmentStatus.Reconciled, loaded.Status);
        Assert.Equal("Preserved notes", loaded.Notes);
        Assert.Equal(3, loaded.RequestedPriority);
        Assert.Empty(loaded.ResponsibleDeveloper);
        Assert.Equal("Legacy Data", loaded.GroupName);
        Assert.Equal(preservedDependency, Assert.Single(await dependencies.GetAllAsync()));

        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            new SqliteEntityAuditReader(database),
            dependencies,
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService()).GetAsync(database.GetTrackerId());
        EntityOverviewItem ownerItem = overview.Items.Single(item => item.EntityId == owner.Id);
        Assert.Equal(DependencyResolutionState.Resolved, ownerItem.DependencyState);
        Assert.Equal(EntityWorkflowState.Ready, ownerItem.WorkflowState);
        Assert.Empty(ownerItem.MissingDependencyNames);
    }

    [Fact]
    public async Task ApplyAsync_PriorityOnlyUpdatePreservesProgressAndHistory()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteTrackedStateStore store = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteProgressHistoryRepository history = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Prioritized",
            DevelopmentStatus.InProgress,
            "Keep notes");
        await store.ApplyAsync(new TrackedStateChangeSet([entity], [], []));
        int historyCount = (await history.GetStatusHistoryAsync()).Count;
        TrackedEntity prioritized = new(
            entity.Id,
            database.GetTrackerId(),
            entity.SourceName,
            entity.Status,
            entity.Notes,
            entity.LifecycleState,
            requestedPriority: 2);

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithRequestedPriorityToUpdate: [prioritized]));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await entities.GetAsync(entity.Id));
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        Assert.Equal(2, loaded.RequestedPriority);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);

        TrackedEntity cleared = new(
            entity.Id,
            database.GetTrackerId(),
            entity.SourceName,
            entity.Status,
            entity.Notes,
            entity.LifecycleState);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithRequestedPriorityToUpdate: [cleared]));

        Assert.Null((await entities.GetAsync(entity.Id))!.RequestedPriority);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);
    }

    [Fact]
    public async Task ApplyAsync_ResponsibilitySelectionPreservesProgressAndTimestamps()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteTrackedStateStore store = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteProgressHistoryRepository history = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Assigned",
            DevelopmentStatus.InProgress,
            "Keep notes",
            requestedPriority: 4);
        await store.ApplyAsync(new TrackedStateChangeSet([entity], [], []));
        int historyCount = (await history.GetStatusHistoryAsync()).Count;
        (string schemaTimestamp, string progressTimestamp) =
            await ReadEntityTimestampsAsync(file.DatabasePath, entity.Id);
        time.Advance(TimeSpan.FromHours(1));
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloper developer = await new EntityTracker.Application.Projects.ProjectDeveloperService(
            new SqliteProjectDeveloperStore(database)).CreateAsync(tracker.ProjectId, "PT", "Platform Team");
        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            responsibilitySelections: [new ResponsibilitySelection(entity.Id, [developer.Id])]));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await entities.GetAsync(entity.Id));
        Assert.Single(await new SqliteResponsibilityPeriodRepository(database).GetByEntityAsync(entity.Id));
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        Assert.Equal(4, loaded.RequestedPriority);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);
        Assert.Equal(
            (schemaTimestamp, progressTimestamp),
            await ReadEntityTimestampsAsync(file.DatabasePath, entity.Id));

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            responsibilitySelections: [new ResponsibilitySelection(entity.Id, [])]));

        Assert.Empty((await entities.GetAsync(entity.Id))!.ResponsibleDeveloper);
        Assert.NotNull(Assert.Single(await new SqliteResponsibilityPeriodRepository(database)
            .GetByEntityAsync(entity.Id)).EndedAtUtc);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);
    }

    [Fact]
    public async Task ApplyAsync_GroupNameOnlyUpdateCanSetAndClearWithoutChangingProgress()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteTrackedStateStore store = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteProgressHistoryRepository history = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Grouped",
            DevelopmentStatus.InProgress,
            "Keep notes",
            requestedPriority: 3,
            responsibleDeveloper: "Platform Team");
        await store.ApplyAsync(new TrackedStateChangeSet([entity], [], []));
        int historyCount = (await history.GetStatusHistoryAsync()).Count;
        (string schemaTimestamp, string progressTimestamp) =
            await ReadEntityTimestampsAsync(file.DatabasePath, entity.Id);
        time.Advance(TimeSpan.FromHours(1));
        entity.ChangeGroupName("  Core Data  ");

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithGroupNameToUpdate: [entity]));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await entities.GetAsync(entity.Id));
        Assert.Equal("Core Data", loaded.GroupName);
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        Assert.Equal(3, loaded.RequestedPriority);
        Assert.Empty(loaded.ResponsibleDeveloper);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);
        Assert.Equal(
            (schemaTimestamp, progressTimestamp),
            await ReadEntityTimestampsAsync(file.DatabasePath, entity.Id));

        entity.ChangeGroupName(null);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            entitiesWithGroupNameToUpdate: [entity]));

        Assert.Equal(string.Empty, (await entities.GetAsync(entity.Id))!.GroupName);
        Assert.Equal(historyCount, (await history.GetStatusHistoryAsync()).Count);
    }

    [Fact]
    public async Task ApplyAsync_ReconciledOwnerPreservesTimestampOfUnchangedDependency()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(
            new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity owner = new(EntityId.New(), database.GetTrackerId(), "Owner");
        TrackedEntity a = new(EntityId.New(), database.GetTrackerId(), "A");
        TrackedEntity b = new(EntityId.New(), database.GetTrackerId(), "B");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(a));
        Assert.True(await entities.TryAddAsync(b));
        EntityDependency unchanged = new EntityDependency(owner.Id, a.Id);
        await dependencies.SaveAsync(unchanged);
        string originalTimestamp = await ReadUpdatedTimestampAsync(file.DatabasePath, unchanged);
        time.Advance(TimeSpan.FromHours(1));

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            [owner.Id],
            [unchanged, new EntityDependency(owner.Id, b.Id)]));

        Assert.Equal(2, (await dependencies.GetAllAsync()).Count);
        Assert.Equal(
            originalTimestamp,
            await ReadUpdatedTimestampAsync(file.DatabasePath, unchanged));
    }

    [Fact]
    public async Task ApplyAsync_ReplacesOnlyTheListedOwnersDependencies()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity owner = new(EntityId.New(), database.GetTrackerId(), "Owner");
        TrackedEntity other = new(EntityId.New(), database.GetTrackerId(), "Other");
        TrackedEntity target = new(EntityId.New(), database.GetTrackerId(), "Target");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(other));
        Assert.True(await entities.TryAddAsync(target));
        await dependencies.SaveAsync(new EntityDependency(owner.Id, target.Id));
        EntityDependency kept = new EntityDependency(other.Id, target.Id);
        await dependencies.SaveAsync(kept);

        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet([], [], [], [owner.Id], []));

        Assert.Equal(kept, Assert.Single(await dependencies.GetAllAsync()));
    }

    [Fact]
    public async Task ManualCreation_PersistsEntityAndResolvesExistingReferenceAtomically()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity owner = new(EntityId.New(), database.GetTrackerId(), "Owner");
        Assert.True(await entities.TryAddAsync(owner));
        TrackedEntity future = TrackedEntity.Placeholder(database.GetTrackerId(), "Future");
        Assert.True(await entities.TryAddAsync(future));
        await dependencies.SaveAsync(new EntityDependency(owner.Id, future.Id));
        ManualEntityCreationService service = new(
            entities,
            dependencies,
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new SqliteTrackedStateStore(database));

        ManualEntityCreationResult result = await service.CreateAsync(
            database.GetTrackerId(),
            new ManualEntityCreationRequest("future", []));

        Assert.True(result.IsSuccess);
        TrackedEntity created = (await entities.GetAsync(result.CreatedEntityId!))!;
        // The new entity took the placeholder's ID, so the owner's dependency now refers to it.
        Assert.Equal(future.Id, created.Id);
        Assert.Equal(EntityLifecycleState.Active, created.LifecycleState);
        Assert.Equal("future", created.SourceName);
        EffectiveDependencyState state = new EffectiveDependencyResolver().Resolve(
            await entities.GetAllAsync(), await dependencies.GetAllAsync());
        Assert.Empty(state.UnresolvedDependencies);
        Assert.Equal(new DependencyEdge(owner.Id, created.Id), Assert.Single(state.ResolvedDependencies).Edge);
    }

    [Fact]
    public async Task ManualCreation_UnknownDependencyPersistsAsUnrankedOverviewEntity()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        DependencyRanker ranker = new();
        ManualEntityCreationService service = new(
            entities,
            dependencies,
            ranker,
            new EffectiveDependencyResolver(),
            new SqliteTrackedStateStore(database));

        ManualEntityCreationResult result = await service.CreateAsync(
            database.GetTrackerId(),
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Unresolved("Missing")]));
        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            new SqliteEntityAuditReader(database),
            dependencies,
            ranker,
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService()).GetAsync(database.GetTrackerId());

        Assert.True(result.IsSuccess);
        EntityOverviewItem item = Assert.Single(overview.Items);
        Assert.Null(item.Rank);
        Assert.Equal(DependencyResolutionState.Unresolved, item.DependencyState);
        Assert.Equal(["Missing"], item.MissingDependencyNames);
    }

    private static async Task<string> ReadUpdatedTimestampAsync(
        string databasePath,
        EntityDependency dependency)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            ForeignKeys = true
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT updated_at_utc
            FROM entity_dependencies
            WHERE dependent_entity_id = $ownerId AND dependency_entity_id = $targetId;
            """;
        command.Parameters.AddWithValue("$ownerId", dependency.DependentEntityId.Value.ToString("D"));
        command.Parameters.AddWithValue("$targetId", dependency.DependencyEntityId.Value.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadProgressTimestampAsync(
        string databasePath,
        EntityId entityId)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            ForeignKeys = true
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT progress_updated_at_utc
            FROM tracked_entities
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string SchemaTimestamp, string ProgressTimestamp)>
        ReadEntityTimestampsAsync(
            string databasePath,
            EntityId entityId)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            ForeignKeys = true
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_updated_at_utc, progress_updated_at_utc
            FROM tracked_entities
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }
}
