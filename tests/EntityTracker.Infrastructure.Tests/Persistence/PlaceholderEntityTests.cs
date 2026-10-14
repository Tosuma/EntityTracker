using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.Persistence;

/// <summary>
/// Dependencies always refer to entities by ID. A name no entity has yet is a placeholder entity,
/// which becomes the real entity when one with its name is added.
/// </summary>
public sealed class PlaceholderEntityTests
{
    [Fact]
    public async Task AddingAnEntityWithAPlaceholdersIdTurnsThePlaceholderIntoIt()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity owner = new(EntityId.New(), tracker, "Owner");
        TrackedEntity placeholder = TrackedEntity.Placeholder(tracker, "Future");
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet(
            [owner, placeholder], [], [], [owner.Id], [new EntityDependency(owner.Id, placeholder.Id)]));

        TrackedEntity real = new(placeholder.Id, tracker, "future", requestedPriority: 2, groupName: "Core");
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([real], [], []));

        TrackedEntity loaded = (await new SqliteEntityRepository(database).GetAsync(placeholder.Id))!;
        Assert.Equal(EntityLifecycleState.Active, loaded.LifecycleState);
        Assert.Equal("future", loaded.SourceName);
        Assert.Equal(2, loaded.RequestedPriority);
        Assert.Equal(new EntityDependency(owner.Id, placeholder.Id),
            Assert.Single(await new SqliteDependencyRepository(database).GetAllAsync(tracker)));
        EntityStatusHistoryEntry created = Assert.Single(
            await new SqliteProgressHistoryRepository(database).GetStatusHistoryAsync(tracker),
            entry => entry.EntityId == placeholder.Id);
        Assert.Equal(StatusHistoryEntryKind.Created, created.Kind);
    }

    [Fact]
    public async Task AddingAnEntityWithTheIdOfARealEntityIsRefused()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity existing = new(EntityId.New(), tracker, "Existing");
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([existing], [], []));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(tracker,
            new TrackedStateChangeSet([new TrackedEntity(existing.Id, tracker, "Other")], [], [])));

        Assert.Equal("Existing", (await new SqliteEntityRepository(database).GetAsync(existing.Id))!.SourceName);
    }

    [Fact]
    public async Task APlaceholderNothingDependsOnAnyMoreIsRemoved()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity owner = new(EntityId.New(), tracker, "Owner");
        TrackedEntity placeholder = TrackedEntity.Placeholder(tracker, "Future");
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet(
            [owner, placeholder], [], [], [owner.Id], [new EntityDependency(owner.Id, placeholder.Id)]));

        await store.ApplyAsync(tracker, new TrackedStateChangeSet([], [], [], [owner.Id], []));

        Assert.Equal([owner.Id],
            (await new SqliteEntityRepository(database).GetAllAsync(tracker)).Select(entity => entity.Id));
    }

    [Fact]
    public async Task HistoryInitializationSkipsPlaceholdersAndAllowsLastDependencyRemoval()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity owner = new(EntityId.New(), tracker, "Owner");
        TrackedEntity placeholder = TrackedEntity.Placeholder(tracker, "Future");
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        // Older trackers have entities and dependencies but no progress history yet.
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(placeholder));
        await dependencies.SaveAsync(tracker, new EntityDependency(owner.Id, placeholder.Id));
        SqliteTrackedStateStore store = new(database);
        ProgressHistoryInitializer initializer = new(entities, dependencies, store,
            new EffectiveDependencyResolver(), new ProgressSnapshotCalculator());

        await initializer.EnsureInitializedAsync(tracker);
        await initializer.EnsureInitializedAsync(tracker);

        SqliteProgressHistoryRepository history = new(database);
        EntityStatusHistoryEntry baseline = Assert.Single(await history.GetStatusHistoryAsync(tracker));
        Assert.Equal(owner.Id, baseline.EntityId);
        Assert.Equal(StatusHistoryEntryKind.Baseline, baseline.Kind);
        Assert.Single(await history.GetProgressSnapshotsAsync(tracker));
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([], [], [], [owner.Id], []));
        Assert.Null(await entities.GetAsync(placeholder.Id));
        Assert.Empty(await dependencies.GetAllAsync(tracker));
    }

    [Fact]
    public async Task PurgingAnEntityOthersDependOnLeavesAPlaceholderWithItsIdAndName()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity owner = new(EntityId.New(), tracker, "Owner");
        TrackedEntity customer = new(EntityId.New(), tracker, "Customer", DevelopmentStatus.Reconciled,
            "Notes", EntityLifecycleState.Archived, requestedPriority: 1, groupName: "Core");
        TrackedEntity unused = new(EntityId.New(), tracker, "Unused", lifecycleState: EntityLifecycleState.Archived);
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet(
            [owner, customer, unused], [], [], [owner.Id], [new EntityDependency(owner.Id, customer.Id)]));
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        EntityLifecycleService lifecycle = new(entities, dependencies, store,
            new EffectiveDependencyResolver(), new DependencyRanker());

        Assert.True(await lifecycle.PurgeArchivedAsync(tracker, customer.Id));
        Assert.True(await lifecycle.PurgeArchivedAsync(tracker, unused.Id));

        TrackedEntity kept = Assert.Single(await entities.GetAllAsync(tracker), entity => entity.Id == customer.Id);
        Assert.Equal(EntityLifecycleState.Placeholder, kept.LifecycleState);
        Assert.Equal("Customer", kept.SourceName);
        Assert.Equal(string.Empty, kept.Notes);
        Assert.Null(await entities.GetAsync(unused.Id));
        Assert.Equal(new EntityDependency(owner.Id, customer.Id), Assert.Single(await dependencies.GetAllAsync(tracker)));
        Assert.Equal("Customer", Assert.Single(new EffectiveDependencyResolver().Resolve(
            await entities.GetAllAsync(tracker), await dependencies.GetAllAsync(tracker))
            .UnresolvedDependencies).Dependency.DependencySourceName);
    }

    [Fact]
    public async Task RenamingAnEntityOntoAPlaceholdersNameConnectsItsDependents()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity typo = new(EntityId.New(), tracker, "custmer");
        TrackedEntity invoice = new(EntityId.New(), tracker, "Invoice");
        TrackedEntity placeholder = TrackedEntity.Placeholder(tracker, "Customer");
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet(
            [typo, invoice, placeholder], [], [], [invoice.Id], [new EntityDependency(invoice.Id, placeholder.Id)]));
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        EntityDependencyEditorService editor = new(entities, dependencies, new EffectiveDependencyResolver(),
            new DependencyRanker(), store, new PriorityPlanningService());
        EntityDependencyEditPlan plan = await editor.LoadAsync(tracker, typo.Id);

        await editor.SaveAsync(tracker, plan, typo.Status, typo.Notes, typo.RequestedPriority,
            typo.ResponsibleDeveloper, typo.GroupName, sourceName: "Customer");

        Assert.Equal(["Customer", "Invoice"],
            (await entities.GetAllAsync(tracker)).Select(entity => entity.SourceName).Order());
        Assert.Equal(new EntityDependency(invoice.Id, typo.Id), Assert.Single(await dependencies.GetAllAsync(tracker)));
    }

    [Fact]
    public async Task AVersionFiveSnapshotGetsThePlaceholderIdsTheDatabaseMigrationGives()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        DateTimeOffset time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid projectId = Guid.NewGuid();
        Guid trackerId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        SnapshotEntity owner = new(ownerId, trackerId, "Owner", "NotStarted", "", "Active", null, "", "", time, time, time, [], [],
            LegacyDependencies: [new SnapshotLegacyDependency(ownerId, targetId, "Optional", time, time)],
            LegacyUnresolvedDependencies: [new SnapshotLegacyUnresolvedDependency(ownerId, "Future", "Mandatory", time, time),
                new SnapshotLegacyUnresolvedDependency(ownerId, "Dropped", "Mandatory", time, time)],
            LegacyOverrides: [new SnapshotLegacyOverride(ownerId, "Dropped", "Suppress", time, time),
                new SnapshotLegacyOverride(ownerId, "External", "Add", time, time)],
            LegacyProvenance: "ManualAndImported");
        SnapshotEntity target = new(targetId, trackerId, "Target", "NotStarted", "", "Active", null, "", "", time, time, time, [], [],
            LegacyDependencies: [], LegacyUnresolvedDependencies: [], LegacyOverrides: [],
            LegacyProvenance: "Imported");
        ProjectSnapshot older = new(5,
            new SnapshotProject(projectId, "Older", "Active", time, time, null),
            [new SnapshotTracker(trackerId, projectId, "Tracker", "Active", time, time, null, null,
                [owner, target], [], [])], []);
        ProjectSnapshotJsonCodec codec = new();

        await new SqliteProjectSnapshotStore(database).ApplyAsync(codec.Decode(codec.Encode(older).Files), 0);

        TrackerId tracker = new(trackerId);
        TrackedEntity[] entities = (await new SqliteEntityRepository(database).GetAllAsync(tracker)).ToArray();
        Assert.Equal(["External", "Future"], entities.Where(entity => entity.IsPlaceholder)
            .Select(entity => entity.SourceName).Order());
        Assert.All(entities.Where(entity => entity.IsPlaceholder), entity =>
            Assert.Equal(TrackedEntity.PlaceholderId(tracker, entity.SourceName), entity.Id));
        Assert.Equal(
            new[] { targetId, TrackedEntity.PlaceholderId(tracker, "External").Value,
                TrackedEntity.PlaceholderId(tracker, "Future").Value }.Order(),
            (await new SqliteDependencyRepository(database).GetAllAsync(tracker))
                .Select(dependency => dependency.DependencyEntityId.Value).Order());
    }
}
