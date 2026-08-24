using System.Globalization;

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task InitializeAsync_FreshDatabaseCreatesVersionEightSchemaAndIsIdempotent()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(8L, await ExecuteScalarInt64Async(connection, "PRAGMA user_version;"));

        string[] tableNames = await ReadStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        Assert.Contains("tracked_entities", tableNames);
        Assert.Contains("schema_dependencies", tableNames);
        Assert.Contains("unresolved_schema_dependencies", tableNames);
        Assert.Contains("manual_dependency_overrides", tableNames);
        Assert.Contains("entity_status_history", tableNames);
        Assert.Contains("progress_snapshots", tableNames);
        Assert.Contains("schema_import_summary", tableNames);

        string[] entityColumns = await ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;");
        Assert.Contains("created_at_utc", entityColumns);
        Assert.Contains("schema_updated_at_utc", entityColumns);
        Assert.Contains("progress_updated_at_utc", entityColumns);
        Assert.Contains("lifecycle_state", entityColumns);
        Assert.Contains("provenance", entityColumns);
        Assert.DoesNotContain("rank", entityColumns);
    }

    [Fact]
    public async Task InitializeAsync_NewerSchemaVersion_ThrowsClearException()
    {
        await using TemporarySqliteFile file = new();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 9;";
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.InitializeAsync());
        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_VersionSevenAddsImportSummaryWithoutChangingTrackedData()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity existing = new(EntityId.New(), "Existing", notes: "Keep notes");
        Assert.True(await repository.TryAddAsync(existing));

        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE schema_import_summary;
                PRAGMA user_version = 7;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(existing.Id));
        Assert.Equal("Keep notes", loaded.Notes);
        Assert.Null(await new SqliteTrackedStateStore(database).GetLatestImportAsync());
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(8L, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionOneDatabaseMigratesWithoutLosingExistingData()
    {
        await using TemporarySqliteFile file = new();
        EntityId dependentId = EntityId.New();
        EntityId dependencyId = EntityId.New();

        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE tracked_entities
                (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_key TEXT NOT NULL UNIQUE,
                    source_name TEXT NOT NULL,
                    development_status TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    schema_updated_at_utc TEXT NOT NULL,
                    progress_updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE schema_dependencies
                (
                    dependent_entity_id TEXT NOT NULL,
                    dependency_entity_id TEXT NOT NULL,
                    dependency_kind TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY (dependent_entity_id, dependency_entity_id),
                    FOREIGN KEY (dependent_entity_id) REFERENCES tracked_entities (id),
                    FOREIGN KEY (dependency_entity_id) REFERENCES tracked_entities (id)
                );

                INSERT INTO tracked_entities VALUES
                    ($dependentId, 'DEPENDENT', 'Dependent', 'InProgress', 'Keep notes', $timestamp, $timestamp, $timestamp),
                    ($dependencyId, 'DEPENDENCY', 'Dependency', 'Completed', '', $timestamp, $timestamp, $timestamp);

                INSERT INTO schema_dependencies VALUES
                    ($dependentId, $dependencyId, 'Mandatory', $timestamp, $timestamp);

                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$dependentId", dependentId.Value.ToString("D"));
            command.Parameters.AddWithValue("$dependencyId", dependencyId.Value.ToString("D"));
            command.Parameters.AddWithValue("$timestamp", "2026-01-01T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);

        Assert.Equal(2, (await entityRepository.GetAllAsync()).Count);
        Assert.Single(await dependencyRepository.GetAllAsync());
        Assert.Empty(await dependencyRepository.GetAllUnresolvedAsync());
        await using SqliteConnection migratedConnection =
            await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(
            8L,
            await ExecuteScalarInt64Async(migratedConnection, "PRAGMA user_version;"));
        Assert.All(
            await entityRepository.GetAllAsync(),
            static entity => Assert.Equal(EntityLifecycleState.Active, entity.LifecycleState));
        Assert.All(
            await entityRepository.GetAllAsync(),
            static entity => Assert.Equal(EntityProvenance.Imported, entity.Provenance));
        Assert.Equal(
            DevelopmentStatus.DevelopmentCompleted,
            (await entityRepository.GetAsync(dependencyId))!.Status);
    }

    [Fact]
    public async Task InitializeAsync_VersionTwoEntityMigratesAsActiveWithoutLosingProgress()
    {
        await using TemporarySqliteFile file = new();
        EntityId id = EntityId.New();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE tracked_entities
                (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_key TEXT NOT NULL UNIQUE,
                    source_name TEXT NOT NULL,
                    development_status TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    schema_updated_at_utc TEXT NOT NULL,
                    progress_updated_at_utc TEXT NOT NULL
                );

                INSERT INTO tracked_entities VALUES
                    ($id, 'CUSTOMER', 'Customer', 'InProgress', 'Keep notes',
                     $timestamp, $timestamp, $timestamp);
                PRAGMA user_version = 2;
                """;
            command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
            command.Parameters.AddWithValue("$timestamp", "2026-01-01T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackedEntity loaded = (await new SqliteEntityRepository(database).GetAsync(id))!;

        Assert.Equal(EntityLifecycleState.Active, loaded.LifecycleState);
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(8L, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
        Assert.Equal(EntityProvenance.Imported, loaded.Provenance);
    }

    [Fact]
    public async Task InitializeAsync_VersionThreeEntityMigratesAsImportedWithoutLosingData()
    {
        await using TemporarySqliteFile file = new();
        EntityId id = EntityId.New();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE tracked_entities
                (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_key TEXT NOT NULL UNIQUE,
                    source_name TEXT NOT NULL,
                    development_status TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    schema_updated_at_utc TEXT NOT NULL,
                    progress_updated_at_utc TEXT NOT NULL,
                    lifecycle_state TEXT NOT NULL
                );

                INSERT INTO tracked_entities VALUES
                    ($id, 'CUSTOMER', 'Customer', 'InProgress', 'Keep notes',
                     $timestamp, $timestamp, $timestamp, 'Active');
                PRAGMA user_version = 3;
                """;
            command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
            command.Parameters.AddWithValue("$timestamp", "2026-01-01T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackedEntity loaded = (await new SqliteEntityRepository(database).GetAsync(id))!;

        Assert.Equal(EntityLifecycleState.Active, loaded.LifecycleState);
        Assert.Equal(EntityProvenance.Imported, loaded.Provenance);
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(8L, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionFourPromotesManualOnlyDependenciesToOverrides()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        TrackedEntity manualOwner = new(
            EntityId.New(),
            "ManualOwner",
            provenance: EntityProvenance.ManualOnly);
        TrackedEntity importedOwner = new(EntityId.New(), "ImportedOwner");
        TrackedEntity target = new(EntityId.New(), "Target");
        Assert.True(await entities.TryAddAsync(manualOwner));
        Assert.True(await entities.TryAddAsync(importedOwner));
        Assert.True(await entities.TryAddAsync(target));
        await dependencies.SaveAsync(new PersistedDependency(
            new DependencyEdge(manualOwner.Id, target.Id),
            ImportedDependencyKind.Mandatory));
        await dependencies.SaveUnresolvedAsync(new PersistedUnresolvedDependency(
            new UnresolvedDependency(manualOwner.Id, "Missing"),
            ImportedDependencyKind.Mandatory));
        await dependencies.SaveAsync(new PersistedDependency(
            new DependencyEdge(importedOwner.Id, target.Id),
            ImportedDependencyKind.Optional));

        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE manual_dependency_overrides;
                DROP TABLE entity_status_history;
                DROP TABLE progress_snapshots;
                DROP TABLE schema_import_summary;
                PRAGMA user_version = 4;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        ManualDependencyOverride[] migrated =
            (await new SqliteManualDependencyOverrideRepository(database).GetAllAsync())
            .ToArray();
        Assert.Contains(migrated, item =>
            item.DependentEntityId == manualOwner.Id &&
            item.DependencySourceName == target.SourceName &&
            item.Action == ManualDependencyOverrideAction.Add);
        Assert.Contains(migrated, item =>
            item.DependentEntityId == manualOwner.Id &&
            item.DependencySourceName == "Missing" &&
            item.Action == ManualDependencyOverrideAction.Add);
        PersistedDependency remainingImported = Assert.Single(
            await dependencies.GetAllAsync());
        Assert.Equal(importedOwner.Id, remainingImported.Edge.DependentEntityId);
        Assert.Empty(await dependencies.GetAllUnresolvedAsync());
    }

    [Fact]
    public async Task InitializeAsync_VersionSixAddsHistoryAndSupportsReworkWithoutLosingData()
    {
        await using TemporarySqliteFile file = new();
        EntityId id = EntityId.New();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE tracked_entities
                (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_key TEXT NOT NULL UNIQUE,
                    source_name TEXT NOT NULL,
                    development_status TEXT NOT NULL
                        CHECK (development_status IN
                            ('NotStarted', 'InProgress', 'DevelopmentCompleted', 'Reconciled')),
                    notes TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    schema_updated_at_utc TEXT NOT NULL,
                    progress_updated_at_utc TEXT NOT NULL,
                    lifecycle_state TEXT NOT NULL,
                    provenance TEXT NOT NULL
                );

                INSERT INTO tracked_entities VALUES
                    ($id, 'EXISTING', 'Existing', 'Reconciled', 'Keep history baseline',
                     $timestamp, $timestamp, $timestamp, 'Active', 'Imported');
                PRAGMA user_version = 6;
                """;
            command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
            command.Parameters.AddWithValue("$timestamp", "2026-01-01T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity loaded = (await repository.GetAsync(id))!;
        Assert.Equal(DevelopmentStatus.Reconciled, loaded.Status);
        Assert.Equal("Keep history baseline", loaded.Notes);

        SqliteTrackedStateStore store = new(database);
        await store.EnsureHistoryBaselineAsync(
            [loaded],
            new ProgressSnapshotState(0, 0, 0, 0, 0, 1));

        TrackedEntity rework = new(
            id,
            loaded.SourceName,
            DevelopmentStatus.ReworkNeeded,
            loaded.Notes);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [], [], [], [],
            entitiesWithProgressToUpdate: [rework],
            progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 0, 1, 0, 0)));

        Assert.Equal(DevelopmentStatus.ReworkNeeded, (await repository.GetAsync(id))!.Status);
        EntityStatusHistoryEntry[] history =
            (await new SqliteProgressHistoryRepository(database).GetStatusHistoryAsync()).ToArray();
        Assert.Equal(StatusHistoryEntryKind.Baseline, history[0].Kind);
        Assert.Equal(StatusHistoryEntryKind.Transition, history[1].Kind);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(8L, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task EntityRepository_SeparateInstancesPreserveStableProgressAndNotes()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase firstDatabase = new(file.DatabasePath);
        await firstDatabase.InitializeAsync();
        SqliteEntityRepository firstRepository = new(firstDatabase);
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            DevelopmentStatus.InProgress,
            "Manual implementation notes",
            provenance: EntityProvenance.ManualOnly);

        Assert.True(await firstRepository.TryAddAsync(entity));

        SqliteDatabase restartedDatabase = new(file.DatabasePath);
        await restartedDatabase.InitializeAsync();
        SqliteEntityRepository restartedRepository = new(restartedDatabase);
        TrackedEntity? loaded = await restartedRepository.GetAsync(entity.Id);

        Assert.NotNull(loaded);
        Assert.Equal(entity.Id, loaded.Id);
        Assert.Equal("sales.Customer", loaded.SourceName);
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Manual implementation notes", loaded.Notes);
        Assert.Equal(EntityProvenance.ManualOnly, loaded.Provenance);
    }

    [Fact]
    public async Task EntityRepository_ReconciledStatusRoundTripsThroughVersionSevenSchema()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity reconciled = new(
            EntityId.New(),
            "VerifiedEntity",
            DevelopmentStatus.Reconciled,
            "Verified against the implementation");

        Assert.True(await repository.TryAddAsync(reconciled));

        TrackedEntity loaded = (await repository.GetAsync(reconciled.Id))!;
        Assert.Equal(DevelopmentStatus.Reconciled, loaded.Status);
        Assert.Equal(reconciled.Notes, loaded.Notes);
    }

    [Fact]
    public async Task MetadataAndTrackedStateProgressUpdatesOwnDisjointFields()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, timeProvider);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        EntityId id = EntityId.New();
        TrackedEntity original = new(
            id,
            "OriginalName",
            DevelopmentStatus.DevelopmentCompleted,
            "Keep this manual progress",
            provenance: EntityProvenance.ManualOnly);
        Assert.True(await repository.TryAddAsync(original));
        EntityAudit originalAudit = await ReadEntityAuditAsync(file.DatabasePath, id);

        timeProvider.Advance(TimeSpan.FromHours(1));
        TrackedEntity importedMetadata = new(
            id,
            "RenamedEntity",
            provenance: EntityProvenance.ManualAndImported);
        Assert.True(await repository.UpdateSchemaMetadataAsync(importedMetadata));

        TrackedEntity? afterMetadataUpdate = await repository.GetAsync(id);
        Assert.NotNull(afterMetadataUpdate);
        Assert.Equal("RenamedEntity", afterMetadataUpdate.SourceName);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, afterMetadataUpdate.Status);
        Assert.Equal("Keep this manual progress", afterMetadataUpdate.Notes);
        Assert.Equal(EntityProvenance.ManualAndImported, afterMetadataUpdate.Provenance);
        EntityAudit metadataAudit = await ReadEntityAuditAsync(file.DatabasePath, id);
        Assert.Equal(originalAudit.CreatedAtUtc, metadataAudit.CreatedAtUtc);
        Assert.Equal(originalAudit.ProgressUpdatedAtUtc, metadataAudit.ProgressUpdatedAtUtc);
        Assert.NotEqual(originalAudit.SchemaUpdatedAtUtc, metadataAudit.SchemaUpdatedAtUtc);
        Assert.Equal("RENAMEDENTITY", metadataAudit.SourceKey);

        timeProvider.Advance(TimeSpan.FromHours(1));
        TrackedEntity changedProgress = new(
            id,
            "This name must be ignored",
            DevelopmentStatus.InProgress,
            "Updated manual notes");
        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                [],
                [],
                [],
                entitiesWithProgressToUpdate: [changedProgress]));

        TrackedEntity? afterProgressUpdate = await repository.GetAsync(id);
        Assert.NotNull(afterProgressUpdate);
        Assert.Equal("RenamedEntity", afterProgressUpdate.SourceName);
        Assert.Equal(DevelopmentStatus.InProgress, afterProgressUpdate.Status);
        Assert.Equal("Updated manual notes", afterProgressUpdate.Notes);
        Assert.Equal(EntityProvenance.ManualAndImported, afterProgressUpdate.Provenance);
        EntityAudit progressAudit = await ReadEntityAuditAsync(file.DatabasePath, id);
        Assert.Equal(originalAudit.CreatedAtUtc, progressAudit.CreatedAtUtc);
        Assert.Equal(metadataAudit.SchemaUpdatedAtUtc, progressAudit.SchemaUpdatedAtUtc);
        Assert.NotEqual(metadataAudit.ProgressUpdatedAtUtc, progressAudit.ProgressUpdatedAtUtc);
        Assert.Equal(1L, await CountRowsAsync(file.DatabasePath, "tracked_entities"));
    }

    [Fact]
    public async Task EntityRepository_MissingUpdatesReturnFalse()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity missing = new(EntityId.New(), "Missing");

        Assert.False(await repository.UpdateSchemaMetadataAsync(missing));
    }

    [Fact]
    public async Task EntityRepository_NormalizedSourceKeyPreventsCaseInsensitiveDuplicate()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);

        Assert.True(await repository.TryAddAsync(
            new TrackedEntity(EntityId.New(), "Customer")));
        Assert.False(await repository.TryAddAsync(
            new TrackedEntity(EntityId.New(), " customer ")));
        Assert.Equal(1L, await CountRowsAsync(file.DatabasePath, "tracked_entities"));
    }

    [Fact]
    public async Task DependencyRepository_RoundTripsAndUpdatesImportedKind()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, timeProvider);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
        TrackedEntity dependent = new(EntityId.New(), "Dependent");
        TrackedEntity dependency = new(EntityId.New(), "Dependency");
        Assert.True(await entityRepository.TryAddAsync(dependent));
        Assert.True(await entityRepository.TryAddAsync(dependency));
        DependencyEdge edge = new(dependent.Id, dependency.Id);

        await dependencyRepository.SaveAsync(
            new PersistedDependency(edge, ImportedDependencyKind.Mandatory));
        DependencyAudit originalAudit = await ReadDependencyAuditAsync(
            file.DatabasePath,
            edge);

        timeProvider.Advance(TimeSpan.FromMinutes(30));
        await dependencyRepository.SaveAsync(
            new PersistedDependency(edge, ImportedDependencyKind.Optional));

        SqliteDependencyRepository restartedRepository = new(
            new SqliteDatabase(file.DatabasePath));
        PersistedDependency loaded = Assert.Single(await restartedRepository.GetAllAsync());
        Assert.Equal(edge, loaded.Edge);
        Assert.Equal(ImportedDependencyKind.Optional, loaded.Kind);
        DependencyAudit updatedAudit = await ReadDependencyAuditAsync(
            file.DatabasePath,
            edge);
        Assert.Equal(originalAudit.CreatedAtUtc, updatedAudit.CreatedAtUtc);
        Assert.NotEqual(originalAudit.UpdatedAtUtc, updatedAudit.UpdatedAtUtc);
    }

    [Fact]
    public async Task DependencyRepository_UnknownEndpoint_ThrowsInfrastructureNeutralException()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteDependencyRepository repository = new(database);
        PersistedDependency dependency = new(
            new DependencyEdge(EntityId.New(), EntityId.New()),
            ImportedDependencyKind.Mandatory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveAsync(dependency));
    }

    [Fact]
    public async Task DependencyRepository_UnresolvedReferenceRoundTripsAndUpdatesKindAndName()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, timeProvider);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
        TrackedEntity dependent = new(EntityId.New(), "Dependent");
        Assert.True(await entityRepository.TryAddAsync(dependent));

        await dependencyRepository.SaveUnresolvedAsync(
            new PersistedUnresolvedDependency(
                new UnresolvedDependency(dependent.Id, "facility"),
                ImportedDependencyKind.Mandatory));
        DependencyAudit originalAudit = await ReadUnresolvedDependencyAuditAsync(
            file.DatabasePath,
            dependent.Id,
            "FACILITY");

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await dependencyRepository.SaveUnresolvedAsync(
            new PersistedUnresolvedDependency(
                new UnresolvedDependency(dependent.Id, "Facility"),
                ImportedDependencyKind.Optional));

        SqliteDependencyRepository restartedRepository = new(
            new SqliteDatabase(file.DatabasePath));
        PersistedUnresolvedDependency loaded = Assert.Single(
            await restartedRepository.GetAllUnresolvedAsync());
        Assert.Equal(dependent.Id, loaded.Dependency.DependentEntityId);
        Assert.Equal("Facility", loaded.Dependency.DependencySourceName);
        Assert.Equal(ImportedDependencyKind.Optional, loaded.Kind);
        DependencyAudit updatedAudit = await ReadUnresolvedDependencyAuditAsync(
            file.DatabasePath,
            dependent.Id,
            "FACILITY");
        Assert.Equal(originalAudit.CreatedAtUtc, updatedAudit.CreatedAtUtc);
        Assert.NotEqual(originalAudit.UpdatedAtUtc, updatedAudit.UpdatedAtUtc);
    }

    [Fact]
    public async Task DependencyRepository_UnresolvedUnknownDependent_ThrowsInfrastructureNeutralException()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteDependencyRepository repository = new(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveUnresolvedAsync(new PersistedUnresolvedDependency(
                new UnresolvedDependency(EntityId.New(), "MissingX"),
                ImportedDependencyKind.Mandatory)));
    }

    [Fact]
    public async Task DependencyRepository_RestartReconstructsUnresolvedOverviewState()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
        TrackedEntity entity = new(EntityId.New(), "WarehouseAssignment");
        Assert.True(await entityRepository.TryAddAsync(entity));
        await dependencyRepository.SaveUnresolvedAsync(
            new PersistedUnresolvedDependency(
                new UnresolvedDependency(entity.Id, "facility"),
                ImportedDependencyKind.Mandatory));

        SqliteDatabase restartedDatabase = new(file.DatabasePath);
        await restartedDatabase.InitializeAsync();
        EntityOverviewService overviewService = new(
            new SqliteEntityRepository(restartedDatabase),
            new SqliteDependencyRepository(restartedDatabase),
            new SqliteManualDependencyOverrideRepository(restartedDatabase),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator());

        EntityOverviewResult result = await overviewService.GetAsync();

        Assert.True(result.IsSuccess);
        EntityOverviewItem item = Assert.Single(result.Items);
        Assert.Equal("WarehouseAssignment", item.SourceName);
        Assert.Null(item.Rank);
        Assert.Equal(DependencyResolutionState.Unresolved, item.DependencyState);
        Assert.Equal(["facility"], item.MissingDependencyNames);
    }

    [Fact]
    public async Task RankChanges_DoNotMoveProgressBetweenStableEntities()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
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
        Assert.True(await entityRepository.TryAddAsync(alpha));
        Assert.True(await entityRepository.TryAddAsync(zulu));
        DependencyRanker ranker = new();

        DependencyRankingResult before = ranker.Rank(
            await entityRepository.GetAllAsync(),
            []);
        Assert.Equal(alpha.Id, before.Rankings[0].EntityId);

        await dependencyRepository.SaveAsync(new PersistedDependency(
            new DependencyEdge(alpha.Id, zulu.Id),
            ImportedDependencyKind.Mandatory));
        IReadOnlyList<TrackedEntity> reloadedEntities = await entityRepository.GetAllAsync();
        IReadOnlyList<PersistedDependency> reloadedDependencies =
            await dependencyRepository.GetAllAsync();
        DependencyRankingResult after = ranker.Rank(
            reloadedEntities,
            reloadedDependencies.Select(static dependency => dependency.Edge));

        Assert.Equal(zulu.Id, after.Rankings[0].EntityId);
        TrackedEntity reloadedAlpha = Assert.Single(
            reloadedEntities,
            entity => entity.Id == alpha.Id);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, reloadedAlpha.Status);
        Assert.Equal("Alpha progress", reloadedAlpha.Notes);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            ForeignKeys = true
        };
        SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> ExecuteScalarInt64Async(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string[]> ReadStringsAsync(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<long> CountRowsAsync(string databasePath, string tableName)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(databasePath);
        return await ExecuteScalarInt64Async(connection, $"SELECT COUNT(*) FROM {tableName};");
    }

    private static async Task<EntityAudit> ReadEntityAuditAsync(
        string databasePath,
        EntityId entityId)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_key, created_at_utc, schema_updated_at_utc, progress_updated_at_utc
            FROM tracked_entities
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new EntityAudit(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static async Task<DependencyAudit> ReadDependencyAuditAsync(
        string databasePath,
        DependencyEdge edge)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT created_at_utc, updated_at_utc
            FROM schema_dependencies
            WHERE dependent_entity_id = $dependentEntityId
              AND dependency_entity_id = $dependencyEntityId;
            """;
        command.Parameters.AddWithValue(
            "$dependentEntityId",
            edge.DependentEntityId.Value.ToString("D"));
        command.Parameters.AddWithValue(
            "$dependencyEntityId",
            edge.DependencyEntityId.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DependencyAudit(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<DependencyAudit> ReadUnresolvedDependencyAuditAsync(
        string databasePath,
        EntityId dependentEntityId,
        string dependencySourceKey)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT created_at_utc, updated_at_utc
            FROM unresolved_schema_dependencies
            WHERE dependent_entity_id = $dependentEntityId
              AND dependency_source_key = $dependencySourceKey;
            """;
        command.Parameters.AddWithValue(
            "$dependentEntityId",
            dependentEntityId.Value.ToString("D"));
        command.Parameters.AddWithValue("$dependencySourceKey", dependencySourceKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DependencyAudit(reader.GetString(0), reader.GetString(1));
    }

    private sealed record EntityAudit(
        string SourceKey,
        string CreatedAtUtc,
        string SchemaUpdatedAtUtc,
        string ProgressUpdatedAtUtc);

    private sealed record DependencyAudit(
        string CreatedAtUtc,
        string UpdatedAtUtc);
}
