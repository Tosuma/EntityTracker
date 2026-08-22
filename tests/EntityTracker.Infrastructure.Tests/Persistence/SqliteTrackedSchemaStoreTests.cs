using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqliteTrackedSchemaStoreTests
{
    [Fact]
    public async Task ApplyAsync_ArchivesAndReconcilesDependenciesWithoutLosingProgress()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedSchemaStore store = new(database);
        TrackedEntity target = new(EntityId.New(), "Target", DevelopmentStatus.Completed, "Keep target");
        TrackedEntity owner = new(EntityId.New(), "Owner", DevelopmentStatus.InProgress, "Keep owner");
        Assert.True(await entities.TryAddAsync(target));
        Assert.True(await entities.TryAddAsync(owner));
        await dependencies.SaveAsync(new PersistedDependency(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Mandatory));

        TrackedSchemaChangeSet changeSet = new(
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
        Assert.Equal(DevelopmentStatus.Completed, loadedTarget.Status);
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
        SqliteTrackedSchemaStore store = new(database);
        TrackedEntity existing = new(EntityId.New(), "Existing");
        TrackedEntity added = new(EntityId.New(), "Added");
        Assert.True(await entities.TryAddAsync(existing));
        PersistedDependency invalidDependency = new(
            new DependencyEdge(added.Id, EntityId.New()),
            ImportedDependencyKind.Mandatory);
        TrackedSchemaChangeSet changeSet = new(
            [added],
            [],
            [],
            [added.Id],
            [invalidDependency],
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(changeSet));

        TrackedEntity persisted = Assert.Single(await entities.GetAllAsync());
        Assert.Equal(existing.Id, persisted.Id);
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

        await new SqliteTrackedSchemaStore(database).ApplyAsync(
            new TrackedSchemaChangeSet([], [], [owner.Id], [], [], []));

        Assert.Equal(EntityLifecycleState.Archived, (await entities.GetAsync(owner.Id))!.LifecycleState);
        Assert.Single(await dependencies.GetAllAsync());
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
        SqliteTrackedSchemaStore store = new(database);
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

        await store.ApplyAsync(new TrackedSchemaChangeSet(
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
            DevelopmentStatus.Completed,
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

        await new SqliteTrackedSchemaStore(database).ApplyAsync(plan.ChangeSet);

        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            dependencies,
            ranker).GetAsync();
        Assert.Equal(["Zulu", "Alpha"], overview.Items.Select(static item => item.SourceName));
        TrackedEntity loadedAlpha = (await entities.GetAsync(alpha.Id))!;
        TrackedEntity loadedZulu = (await entities.GetAsync(zulu.Id))!;
        Assert.Equal(DevelopmentStatus.Completed, loadedAlpha.Status);
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

        await new SqliteTrackedSchemaStore(database).ApplyAsync(plan.ChangeSet);

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
            new DependencyRanker(),
            new SqliteTrackedSchemaStore(database));

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
            ranker,
            new SqliteTrackedSchemaStore(database));

        ManualEntityCreationResult result = await service.CreateAsync(
            new ManualEntityCreationRequest(
                "Owner",
                [ManualDependencySelection.Unresolved("Missing")]));
        EntityOverviewResult overview = await new EntityOverviewService(
            entities,
            dependencies,
            ranker).GetAsync();

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
        SqliteTrackedSchemaStore store = new(database);
        DependencyRanker ranker = new();
        ManualEntityCreationResult creation = await new ManualEntityCreationService(
            entities,
            dependencies,
            ranker,
            store).CreateAsync(new ManualEntityCreationRequest("Future", []));
        TrackedEntity withProgress = new(
            creation.CreatedEntityId!,
            "Future",
            DevelopmentStatus.InProgress,
            "Keep manual progress",
            provenance: EntityProvenance.ManualOnly);
        Assert.True(await entities.UpdateProgressAsync(withProgress));
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
}
