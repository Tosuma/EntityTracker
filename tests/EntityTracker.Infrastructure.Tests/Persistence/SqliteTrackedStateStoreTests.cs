using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
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
        TrackedEntity entity = new(EntityId.New(), "New");

        await store.ApplyAsync(new TrackedStateChangeSet(
            [entity], [], [], [], [], [],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0)));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [], [], [], [],
            entitiesWithProgressToUpdate:
            [new TrackedEntity(entity.Id, entity.SourceName, entity.Status, "Notes only")],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0)));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [], [], [], [],
            entitiesWithProgressToUpdate:
            [new TrackedEntity(
                entity.Id,
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
    public async Task ApplyAsync_ArchivesAndReconcilesDependenciesWithoutLosingProgress()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity target = new(EntityId.New(), "Target", DevelopmentStatus.DevelopmentCompleted, "Keep target");
        TrackedEntity owner = new(EntityId.New(), "Owner", DevelopmentStatus.InProgress, "Keep owner");
        Assert.True(await entities.TryAddAsync(target));
        Assert.True(await entities.TryAddAsync(owner));
        await dependencies.SaveAsync(new PersistedDependency(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Mandatory));

        TrackedStateChangeSet changeSet = new(
            [],
            [],
            [target.Id],
            [owner.Id],
            [],
            [new PersistedUnresolvedDependency(
                new UnresolvedDependency(owner.Id, "Target"),
                ImportedDependencyKind.Mandatory)]);

        await store.ApplyAsync(changeSet);

        TrackedEntity loadedTarget = (await entities.GetAsync(target.Id))!;
        TrackedEntity loadedOwner = (await entities.GetAsync(owner.Id))!;
        Assert.Equal(EntityLifecycleState.Archived, loadedTarget.LifecycleState);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, loadedTarget.Status);
        Assert.Equal("Keep target", loadedTarget.Notes);
        Assert.Equal(EntityLifecycleState.Active, loadedOwner.LifecycleState);
        Assert.Equal(DevelopmentStatus.InProgress, loadedOwner.Status);
        Assert.Equal("Keep owner", loadedOwner.Notes);
        Assert.Empty(await dependencies.GetAllAsync());
        Assert.Equal("Target", Assert.Single(await dependencies.GetAllUnresolvedAsync())
            .Dependency.DependencySourceName);
    }

    [Fact]
    public async Task ApplyAsync_FailureRollsBackEarlierChanges()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity existing = new(EntityId.New(), "Existing");
        TrackedEntity changedProgress = new(
            existing.Id,
            existing.SourceName,
            DevelopmentStatus.Reconciled,
            "Must roll back");
        TrackedEntity added = new(EntityId.New(), "Added");
        Assert.True(await entities.TryAddAsync(existing));
        PersistedDependency invalidDependency = new(
            new DependencyEdge(added.Id, EntityId.New()),
            ImportedDependencyKind.Mandatory);
        TrackedStateChangeSet changeSet = new(
            [added],
            [],
            [],
            [added.Id],
            [invalidDependency],
            [],
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
        TrackedEntity owner = new(EntityId.New(), "Owner");
        TrackedEntity target = new(EntityId.New(), "Target");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(target));
        await dependencies.SaveAsync(new PersistedDependency(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Mandatory));

        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet([], [], [owner.Id], [], [], []));

        Assert.Equal(EntityLifecycleState.Archived, (await entities.GetAsync(owner.Id))!.LifecycleState);
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
        SqliteManualDependencyOverrideRepository overrides = new(database);
        SqliteTrackedStateStore store = new(database);
        TrackedEntity target = new(
            EntityId.New(),
            "Target",
            DevelopmentStatus.Reconciled,
            "Preserved notes",
            EntityLifecycleState.Archived,
            EntityProvenance.ManualAndImported);
        TrackedEntity owner = new(EntityId.New(), "Owner");
        Assert.True(await entities.TryAddAsync(target));
        Assert.True(await entities.TryAddAsync(owner));
        await dependencies.SaveUnresolvedAsync(new PersistedUnresolvedDependency(
            new UnresolvedDependency(owner.Id, " target "),
            ImportedDependencyKind.Mandatory));
        ManualDependencyOverride preservedOverride = new(
            target.Id,
            "Future",
            ManualDependencyOverrideAction.Suppress);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            [],
            [],
            [],
            [target.Id],
            [preservedOverride]));
        EntityLifecycleService lifecycle = new(
            entities,
            dependencies,
            overrides,
            store,
            new EffectiveDependencyResolver(),
            new DependencyRanker());

        EntityRestorationResult restoration = await lifecycle.RestoreAsync(target.Id);

        Assert.True(restoration.IsSuccess);
        TrackedEntity loaded = (await entities.GetAsync(target.Id))!;
        Assert.Equal(target.Id, loaded.Id);
        Assert.Equal(EntityLifecycleState.Active, loaded.LifecycleState);
        Assert.Equal(DevelopmentStatus.Reconciled, loaded.Status);
        Assert.Equal("Preserved notes", loaded.Notes);
        Assert.Equal(EntityProvenance.ManualAndImported, loaded.Provenance);
        Assert.Equal(preservedOverride, Assert.Single(await overrides.GetAllAsync()));
        Assert.Single(await dependencies.GetAllUnresolvedAsync());

        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            dependencies,
            overrides,
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator()).GetAsync();
        EntityOverviewItem ownerItem = overview.Items.Single(item => item.EntityId == owner.Id);
        Assert.Equal(DependencyResolutionState.Resolved, ownerItem.DependencyState);
        Assert.Equal(EntityWorkflowState.Ready, ownerItem.WorkflowState);
        Assert.Empty(ownerItem.MissingDependencyNames);
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
        TrackedEntity owner = new(EntityId.New(), "Owner");
        TrackedEntity a = new(EntityId.New(), "A");
        TrackedEntity b = new(EntityId.New(), "B");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(a));
        Assert.True(await entities.TryAddAsync(b));
        PersistedDependency unchanged = new(
            new DependencyEdge(owner.Id, a.Id),
            ImportedDependencyKind.Mandatory);
        await dependencies.SaveAsync(unchanged);
        string originalTimestamp = await ReadUpdatedTimestampAsync(file.DatabasePath, unchanged.Edge);
        time.Advance(TimeSpan.FromHours(1));

        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            [owner.Id],
            [
                unchanged,
                new PersistedDependency(
                    new DependencyEdge(owner.Id, b.Id),
                    ImportedDependencyKind.Optional)
            ],
            []));

        Assert.Equal(2, (await dependencies.GetAllAsync()).Count);
        Assert.Equal(
            originalTimestamp,
            await ReadUpdatedTimestampAsync(file.DatabasePath, unchanged.Edge));
    }

    [Fact]
    public async Task ApplyAsync_ReconcilesOverridesWithoutChangingImportedDependencies()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteManualDependencyOverrideRepository overrides = new(database);
        TrackedEntity owner = new(EntityId.New(), "Owner");
        TrackedEntity target = new(EntityId.New(), "Target");
        Assert.True(await entities.TryAddAsync(owner));
        Assert.True(await entities.TryAddAsync(target));
        PersistedDependency imported = new(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Optional);
        await dependencies.SaveAsync(imported);

        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                [],
                [],
                [],
                [owner.Id],
                [new ManualDependencyOverride(
                    owner.Id,
                    target.SourceName,
                    ManualDependencyOverrideAction.Suppress)]));

        Assert.Equal(imported, Assert.Single(await dependencies.GetAllAsync()));
        ManualDependencyOverride persisted = Assert.Single(await overrides.GetAllAsync());
        Assert.Equal(ManualDependencyOverrideAction.Suppress, persisted.Action);
        Assert.Equal(target.SourceName, persisted.DependencySourceName);
    }

    [Fact]
    public async Task PlannedSynchronization_PreservesProgressWhenDependencyChangesShiftRanks()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity alpha = new(
            EntityId.New(),
            "Alpha",
            DevelopmentStatus.DevelopmentCompleted,
            "Alpha progress");
        TrackedEntity zulu = new(
            EntityId.New(),
            "Zulu",
            DevelopmentStatus.InProgress,
            "Zulu progress");
        Assert.True(await entities.TryAddAsync(alpha));
        Assert.True(await entities.TryAddAsync(zulu));
        SchemaImportCandidate candidate = new(
            [
                new ImportedEntity(EntitySourceKey.From("Alpha"), "Alpha"),
                new ImportedEntity(EntitySourceKey.From("Zulu"), "Zulu")
            ],
            [new ImportedDependency(
                EntitySourceKey.From("Alpha"),
                EntitySourceKey.From("Zulu"),
                ImportedDependencyKind.Mandatory)]);
        DependencyRanker ranker = new();
        SchemaSynchronizationPlan plan = new SchemaSynchronizationPlanner(ranker).CreatePlan(
            candidate,
            SchemaImportMode.Complete,
            await entities.GetAllAsync(),
            await dependencies.GetAllAsync(),
            await dependencies.GetAllUnresolvedAsync());

        await new SqliteTrackedStateStore(database).ApplyAsync(plan.ChangeSet);

        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            dependencies,
            new SqliteManualDependencyOverrideRepository(database),
            ranker,
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator()).GetAsync();
        Assert.Equal(["Zulu", "Alpha"], overview.Items.Select(static item => item.SourceName));
        TrackedEntity loadedAlpha = (await entities.GetAsync(alpha.Id))!;
        TrackedEntity loadedZulu = (await entities.GetAsync(zulu.Id))!;
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, loadedAlpha.Status);
        Assert.Equal("Alpha progress", loadedAlpha.Notes);
        Assert.Equal(DevelopmentStatus.InProgress, loadedZulu.Status);
        Assert.Equal("Zulu progress", loadedZulu.Notes);
    }

    [Fact]
    public async Task PlannedSynchronization_FiveNewTablesPersistAsFiveTrackedEntities()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SchemaImportCandidate candidate = new(
            Enumerable.Range(1, 5).Select(index =>
            {
                string name = $"Table{index}";
                return new ImportedEntity(EntitySourceKey.From(name), name);
            }),
            []);
        SchemaSynchronizationPlan plan = new SchemaSynchronizationPlanner(
            new DependencyRanker()).CreatePlan(
                candidate,
                SchemaImportMode.Complete,
                [],
                [],
                []);

        await new SqliteTrackedStateStore(database).ApplyAsync(plan.ChangeSet);

        Assert.Equal(5, (await entities.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ManualCreation_PersistsEntityAndResolvesExistingReferenceAtomically()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity owner = new(EntityId.New(), "Owner");
        Assert.True(await entities.TryAddAsync(owner));
        await dependencies.SaveUnresolvedAsync(new PersistedUnresolvedDependency(
            new UnresolvedDependency(owner.Id, "Future"),
            ImportedDependencyKind.Optional));
        ManualEntityCreationService service = new(
            entities,
            dependencies,
            new SqliteManualDependencyOverrideRepository(database),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new SqliteTrackedStateStore(database));

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest("future", []));

        Assert.True(result.IsSuccess);
        TrackedEntity created = (await entities.GetAsync(result.CreatedEntityId!))!;
        Assert.Equal(EntityProvenance.ManualOnly, created.Provenance);
        Assert.Empty(await dependencies.GetAllUnresolvedAsync());
        PersistedDependency resolved = Assert.Single(await dependencies.GetAllAsync());
        Assert.Equal(new DependencyEdge(owner.Id, created.Id), resolved.Edge);
        Assert.Equal(ImportedDependencyKind.Optional, resolved.Kind);
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
            new SqliteManualDependencyOverrideRepository(database),
            ranker,
            new EffectiveDependencyResolver(),
            new SqliteTrackedStateStore(database));

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Unresolved("Missing")]));
        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            dependencies,
            new SqliteManualDependencyOverrideRepository(database),
            ranker,
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator()).GetAsync();

        Assert.True(result.IsSuccess);
        EntityOverviewItem item = Assert.Single(overview.Items);
        Assert.Null(item.Rank);
        Assert.Equal(DependencyResolutionState.Unresolved, item.DependencyState);
        Assert.Equal(["Missing"], item.MissingDependencyNames);
    }

    [Fact]
    public async Task PartialSynchronization_MatchesManualEntityAndPreservesProgress()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore store = new(database);
        DependencyRanker ranker = new();
        ManualEntityCreationResult creation = await new ManualEntityCreationService(
            entities,
            dependencies,
            new SqliteManualDependencyOverrideRepository(database),
            ranker,
            new EffectiveDependencyResolver(),
            store).CreateAsync(new ManualEntityCreationRequest("Future", []));
        TrackedEntity withProgress = new(
            creation.CreatedEntityId!,
            "Future",
            DevelopmentStatus.InProgress,
            "Keep manual progress",
            provenance: EntityProvenance.ManualOnly);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [],
            [],
            [],
            [],
            [],
            [],
            entitiesWithProgressToUpdate: [withProgress]));
        SchemaImportCandidate candidate = new(
            [new ImportedEntity(EntitySourceKey.From("future"), "future")],
            []);
        SchemaSynchronizationPlan plan = new SchemaSynchronizationPlanner(ranker).CreatePlan(
            candidate,
            SchemaImportMode.Partial,
            await entities.GetAllAsync(),
            await dependencies.GetAllAsync(),
            await dependencies.GetAllUnresolvedAsync());

        await store.ApplyAsync(plan.ChangeSet);

        TrackedEntity loaded = (await entities.GetAsync(creation.CreatedEntityId!))!;
        Assert.Equal(creation.CreatedEntityId, loaded.Id);
        Assert.Equal("future", loaded.SourceName);
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep manual progress", loaded.Notes);
        Assert.Equal(EntityProvenance.ManualAndImported, loaded.Provenance);
        Assert.Single(await entities.GetAllAsync());
    }

    private static async Task<string> ReadUpdatedTimestampAsync(
        string databasePath,
        DependencyEdge edge)
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
            FROM schema_dependencies
            WHERE dependent_entity_id = $ownerId AND dependency_entity_id = $targetId;
            """;
        command.Parameters.AddWithValue("$ownerId", edge.DependentEntityId.Value.ToString("D"));
        command.Parameters.AddWithValue("$targetId", edge.DependencyEntityId.Value.ToString("D"));
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
}
