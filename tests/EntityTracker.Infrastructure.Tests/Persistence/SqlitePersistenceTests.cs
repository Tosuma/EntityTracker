using System.Globalization;

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task InitializeAsync_FreshDatabaseCreatesCurrentSchemaAndIsIdempotent()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(connection, "PRAGMA user_version;"));

        string[] tableNames = await ReadStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        Assert.Contains("tracked_entities", tableNames);
        Assert.Contains("entity_dependencies", tableNames);
        Assert.Contains("entity_status_history", tableNames);
        Assert.Contains("progress_snapshots", tableNames);
        Assert.DoesNotContain("schema_dependencies", tableNames);
        Assert.DoesNotContain("unresolved_schema_dependencies", tableNames);
        Assert.DoesNotContain("manual_dependency_overrides", tableNames);
        Assert.DoesNotContain("schema_import_summary", tableNames);

        string[] entityColumns = await ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;");
        Assert.Contains("created_at_utc", entityColumns);
        Assert.Contains("schema_updated_at_utc", entityColumns);
        Assert.Contains("progress_updated_at_utc", entityColumns);
        Assert.Contains("lifecycle_state", entityColumns);
        Assert.DoesNotContain("provenance", entityColumns);
        Assert.Contains("requested_priority", entityColumns);
        Assert.DoesNotContain("responsible_developer", entityColumns);
        Assert.Contains("responsibility_periods", tableNames);
        Assert.Contains("group_name", entityColumns);
        Assert.DoesNotContain("rank", entityColumns);
    }

    [Fact]
    public async Task EntityAuditReader_ReturnsUtcTimestampsForOnlyTheRequestedTracker()
    {
        await using TemporarySqliteFile file = new();
        DateTimeOffset now = new(2026, 8, 24, 10, 30, 0, TimeSpan.Zero);
        SqliteDatabase database = new(file.DatabasePath, new MutableTimeProvider(now));
        await database.InitializeAsync();
        TrackedEntity entity = new(EntityId.New(), database.GetTrackerId(), "Audit target");
        Assert.True(await new SqliteEntityRepository(database).TryAddAsync(entity));
        SqliteEntityAuditReader reader = new(database);

        EntityAuditTimestamps audit = Assert.Single(await reader.GetAllAsync(
            database.GetTrackerId()));

        Assert.Equal(entity.Id, audit.EntityId);
        Assert.Equal(now, audit.CreatedAtUtc);
        Assert.Equal(now, audit.SchemaUpdatedAtUtc);
        Assert.Equal(now, audit.ProgressUpdatedAtUtc);
        Assert.Empty(await reader.GetAllAsync(TrackerId.New()));
    }

    [Fact]
    public async Task InitializeAsync_NewerSchemaVersion_ThrowsClearException()
    {
        await using TemporarySqliteFile file = new();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {SqliteDatabase.CurrentSchemaVersion + 1};";
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.InitializeAsync());
        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SqliteDatabase.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_DropsTheProvenanceColumnAnEarlyVersion22Kept()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        // As a development build of version 22 left it: provenance still there, NOT NULL.
        await using (SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE tracked_entities ADD COLUMN provenance TEXT NOT NULL DEFAULT 'Manual'
                    CHECK (provenance IN ('Manual', 'Copied'));
                PRAGMA user_version = 22;
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase upgraded = new(file.DatabasePath);
        await upgraded.InitializeAsync();

        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await upgraded.GetStoredSchemaVersionAsync());
        await using (SqliteConnection connection = await upgraded.OpenConnectionAsync(CancellationToken.None))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('tracked_entities') WHERE name = 'provenance';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        // The insert that failed with "NOT NULL constraint failed: tracked_entities.provenance".
        TrackedEntity entity = new(EntityId.New(), upgraded.GetTrackerId(), "customer");
        Assert.True(await new SqliteEntityRepository(upgraded).TryAddAsync(entity));
    }

    [Fact]
    public async Task InitializeAsync_VersionSevenAddsImportSummaryWithoutChangingTrackedData()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity existing = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Existing",
            notes: "Keep notes");
        Assert.True(await repository.TryAddAsync(existing));

        await VersionTwentySchema.RevertAsync(file.DatabasePath);
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
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionEightAddsNullableRequestedPriority()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity existing = new(EntityId.New(), database.GetTrackerId(), "Existing");
        Assert.True(await repository.TryAddAsync(existing));

        await VersionTwentySchema.RevertAsync(file.DatabasePath);
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE tracked_entities DROP COLUMN requested_priority;
                PRAGMA user_version = 8;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(existing.Id));
        Assert.Null(loaded.RequestedPriority);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
        Assert.Contains(
            "requested_priority",
            await ReadStringsAsync(
                migrated,
                "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionNineRetiresScalarResponsibleDeveloper()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity existing = new(EntityId.New(), database.GetTrackerId(), "Existing");
        Assert.True(await repository.TryAddAsync(existing));

        await VersionTwentySchema.RevertAsync(file.DatabasePath);
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA user_version = 9;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(existing.Id));
        Assert.Equal(string.Empty, loaded.ResponsibleDeveloper);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
        Assert.DoesNotContain(
            "responsible_developer",
            await ReadStringsAsync(
                migrated,
                "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionTenAddsEmptyGroupName()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity existing = new(EntityId.New(), database.GetTrackerId(), "Existing");
        Assert.True(await repository.TryAddAsync(existing));

        await VersionTwentySchema.RevertAsync(file.DatabasePath);
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE tracked_entities DROP COLUMN group_name;
                PRAGMA user_version = 10;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(existing.Id));
        Assert.Equal(string.Empty, loaded.GroupName);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
        Assert.Contains(
            "group_name",
            await ReadStringsAsync(
                migrated,
                "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task EntityRepository_RoundTripsRequestedPriority(int? requestedPriority)
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Prioritized",
            requestedPriority: requestedPriority);

        Assert.True(await repository.TryAddAsync(entity));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(entity.Id));
        Assert.Equal(requestedPriority, loaded.RequestedPriority);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ada Lovelace")]
    [InlineData("Platform Team")]
    [InlineData("dEv Enablement")]
    public async Task EntityRepository_DoesNotPersistLegacyResponsibleText(
        string responsibleDeveloper)
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Assigned",
            responsibleDeveloper: responsibleDeveloper);

        Assert.True(await repository.TryAddAsync(entity));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(entity.Id));
        Assert.Empty(loaded.ResponsibleDeveloper);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Core Data")]
    [InlineData("cUSTOMER")]
    public async Task EntityRepository_RoundTripsGroupName(string groupName)
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "Grouped",
            groupName: groupName);

        Assert.True(await repository.TryAddAsync(entity));

        TrackedEntity loaded = Assert.IsType<TrackedEntity>(await repository.GetAsync(entity.Id));
        Assert.Equal(groupName, loaded.GroupName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RequestedPriority_DatabaseConstraintRejectsUnsupportedValues(int priority)
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository repository = new(database);
        TrackedEntity entity = new(EntityId.New(), database.GetTrackerId(), "Prioritized");
        Assert.True(await repository.TryAddAsync(entity));
        await using SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE tracked_entities SET requested_priority = $priority WHERE id = $id;";
        command.Parameters.AddWithValue("$priority", priority);
        command.Parameters.AddWithValue("$id", entity.Id.Value.ToString("D"));

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
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
        await using SqliteConnection migratedConnection =
            await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(
            (long)SqliteDatabase.CurrentSchemaVersion,
            await ExecuteScalarInt64Async(migratedConnection, "PRAGMA user_version;"));
        Assert.All(
            await entityRepository.GetAllAsync(),
            static entity => Assert.Equal(EntityLifecycleState.Active, entity.LifecycleState));
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
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
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
        Assert.Equal(DevelopmentStatus.InProgress, loaded.Status);
        Assert.Equal("Keep notes", loaded.Notes);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
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
            database.GetTrackerId(),
            loaded.SourceName,
            DevelopmentStatus.ReworkNeeded,
            loaded.Notes);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [],
            entitiesWithProgressToUpdate: [rework],
            progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 0, 1, 0, 0)));

        Assert.Equal(DevelopmentStatus.ReworkNeeded, (await repository.GetAsync(id))!.Status);
        EntityStatusHistoryEntry[] history =
            (await new SqliteProgressHistoryRepository(database).GetStatusHistoryAsync()).ToArray();
        Assert.Equal(StatusHistoryEntryKind.Baseline, history[0].Kind);
        Assert.Equal(StatusHistoryEntryKind.Transition, history[1].Kind);
        await using SqliteConnection migrated = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ExecuteScalarInt64Async(migrated, "PRAGMA user_version;"));
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
            firstDatabase.GetTrackerId(),
            "sales.Customer",
            DevelopmentStatus.InProgress,
            "Manual implementation notes");

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
    }

    [Fact]
    public async Task NewStatuses_RoundTripThroughEntitiesHistoryAndProgressSnapshots()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        EntityId id = EntityId.New();
        TrackedEntity blocked = new(id, database.GetTrackerId(), "Blocked entity",
            DevelopmentStatus.Blocked);
        SqliteEntityRepository repository = new(database);
        Assert.True(await repository.TryAddAsync(blocked));

        SqliteTrackedStateStore store = new(database);
        await store.EnsureHistoryBaselineAsync([blocked],
            new ProgressSnapshotState(0, 0, 0, 0, 0, 0, manuallyBlockedCount: 1));
        TrackedEntity reworking = new(id, database.GetTrackerId(), "Blocked entity",
            DevelopmentStatus.Reworking);
        await store.ApplyAsync(new TrackedStateChangeSet(
            [], [], [],
            entitiesWithProgressToUpdate: [reworking],
            progressSnapshotAfterChanges: new ProgressSnapshotState(
                0, 0, 0, 0, 0, 0, reworkingCount: 1)));

        Assert.Equal(DevelopmentStatus.Reworking, (await repository.GetAsync(id))!.Status);
        SqliteProgressHistoryRepository history = new(database);
        Assert.Equal([DevelopmentStatus.Blocked, DevelopmentStatus.Reworking],
            (await history.GetStatusHistoryAsync()).Select(static entry => entry.NewStatus));
        Assert.Equal(1,
            (await history.GetLatestProgressSnapshotAsync(database.GetTrackerId()))!.State.ReworkingCount);
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
            database.GetTrackerId(),
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
            database.GetTrackerId(),
            "OriginalName",
            DevelopmentStatus.DevelopmentCompleted,
            "Keep this manual progress");
        Assert.True(await repository.TryAddAsync(original));
        EntityAudit originalAudit = await ReadEntityAuditAsync(file.DatabasePath, id);

        timeProvider.Advance(TimeSpan.FromHours(1));
        TrackedEntity importedMetadata = new(
            id,
            database.GetTrackerId(),
            "RenamedEntity");
        Assert.True(await repository.UpdateSchemaMetadataAsync(importedMetadata));

        TrackedEntity? afterMetadataUpdate = await repository.GetAsync(id);
        Assert.NotNull(afterMetadataUpdate);
        Assert.Equal("RenamedEntity", afterMetadataUpdate.SourceName);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, afterMetadataUpdate.Status);
        Assert.Equal("Keep this manual progress", afterMetadataUpdate.Notes);
        EntityAudit metadataAudit = await ReadEntityAuditAsync(file.DatabasePath, id);
        Assert.Equal(originalAudit.CreatedAtUtc, metadataAudit.CreatedAtUtc);
        Assert.Equal(originalAudit.ProgressUpdatedAtUtc, metadataAudit.ProgressUpdatedAtUtc);
        Assert.NotEqual(originalAudit.SchemaUpdatedAtUtc, metadataAudit.SchemaUpdatedAtUtc);
        Assert.Equal("RenamedEntity", metadataAudit.SourceName);

        timeProvider.Advance(TimeSpan.FromHours(1));
        TrackedEntity changedProgress = new(
            id,
            database.GetTrackerId(),
            "This name must be ignored",
            DevelopmentStatus.InProgress,
            "Updated manual notes");
        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet(
                [],
                [],
                [],
                entitiesWithProgressToUpdate: [changedProgress]));

        TrackedEntity? afterProgressUpdate = await repository.GetAsync(id);
        Assert.NotNull(afterProgressUpdate);
        Assert.Equal("RenamedEntity", afterProgressUpdate.SourceName);
        Assert.Equal(DevelopmentStatus.InProgress, afterProgressUpdate.Status);
        Assert.Equal("Updated manual notes", afterProgressUpdate.Notes);
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
        TrackedEntity missing = new(EntityId.New(), database.GetTrackerId(), "Missing");

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
            new TrackedEntity(EntityId.New(), database.GetTrackerId(), "Customer")));
        Assert.False(await repository.TryAddAsync(
            new TrackedEntity(EntityId.New(), database.GetTrackerId(), " customer ")));
        Assert.Equal(1L, await CountRowsAsync(file.DatabasePath, "tracked_entities"));
    }

    [Fact]
    public async Task DependencyRepository_RoundTripsAndKeepsItsCreatedTimeWhenSavedAgain()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, timeProvider);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
        TrackedEntity dependent = new(EntityId.New(), database.GetTrackerId(), "Dependent");
        TrackedEntity facility = new(EntityId.New(), database.GetTrackerId(), "Facility");
        Assert.True(await entityRepository.TryAddAsync(dependent));
        Assert.True(await entityRepository.TryAddAsync(facility));

        await dependencyRepository.SaveAsync(new EntityDependency(dependent.Id, facility.Id));
        DependencyAudit originalAudit = await ReadDependencyAuditAsync(
            file.DatabasePath,
            dependent.Id,
            facility.Id);

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await dependencyRepository.SaveAsync(new EntityDependency(dependent.Id, facility.Id));

        SqliteDependencyRepository restartedRepository = new(
            new SqliteDatabase(file.DatabasePath));
        EntityDependency loaded = Assert.Single(await restartedRepository.GetAllAsync());
        Assert.Equal(dependent.Id, loaded.DependentEntityId);
        Assert.Equal(facility.Id, loaded.DependencyEntityId);
        DependencyAudit updatedAudit = await ReadDependencyAuditAsync(
            file.DatabasePath,
            dependent.Id,
            facility.Id);
        Assert.Equal(originalAudit.CreatedAtUtc, updatedAudit.CreatedAtUtc);
        Assert.NotEqual(originalAudit.UpdatedAtUtc, updatedAudit.UpdatedAtUtc);
    }

    [Fact]
    public async Task DependencyRepository_UnknownDependent_ThrowsInfrastructureNeutralException()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteDependencyRepository repository = new(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveAsync(new EntityDependency(EntityId.New(), EntityId.New())));
    }

    [Fact]
    public async Task DependencyRepository_RestartReconstructsUnresolvedOverviewState()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteEntityRepository entityRepository = new(database);
        SqliteDependencyRepository dependencyRepository = new(database);
        TrackedEntity entity = new(
            EntityId.New(),
            database.GetTrackerId(),
            "WarehouseAssignment");
        TrackedEntity facility = TrackedEntity.Placeholder(database.GetTrackerId(), "facility");
        Assert.True(await entityRepository.TryAddAsync(entity));
        Assert.True(await entityRepository.TryAddAsync(facility));
        await dependencyRepository.SaveAsync(new EntityDependency(entity.Id, facility.Id));

        SqliteDatabase restartedDatabase = new(file.DatabasePath);
        await restartedDatabase.InitializeAsync();
        EntityOverviewService overviewService = new(
            new SqliteEntityRepository(restartedDatabase),
            new SqliteEntityAuditReader(restartedDatabase),
            new SqliteDependencyRepository(restartedDatabase),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService());

        EntityOverviewResult result = await overviewService.GetAsync(
            restartedDatabase.GetTrackerId());

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
            database.GetTrackerId(),
            "Alpha",
            DevelopmentStatus.DevelopmentCompleted,
            "Alpha progress");
        TrackedEntity zulu = new(
            EntityId.New(),
            database.GetTrackerId(),
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

        await dependencyRepository.SaveAsync(new EntityDependency(alpha.Id, zulu.Id));
        IReadOnlyList<TrackedEntity> reloadedEntities = await entityRepository.GetAllAsync();
        EffectiveDependencyState reloadedDependencies = new EffectiveDependencyResolver().Resolve(
            reloadedEntities,
            await dependencyRepository.GetAllAsync());
        DependencyRankingResult after = ranker.Rank(
            reloadedEntities,
            reloadedDependencies.ResolvedDependencies.Select(static dependency => dependency.Edge));

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
            SELECT source_name, created_at_utc, schema_updated_at_utc, progress_updated_at_utc
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
        EntityId dependentEntityId,
        EntityId dependencyEntityId)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT created_at_utc, updated_at_utc
            FROM entity_dependencies
            WHERE dependent_entity_id = $dependentEntityId
              AND dependency_entity_id = $dependencyEntityId;
            """;
        command.Parameters.AddWithValue(
            "$dependentEntityId",
            dependentEntityId.Value.ToString("D"));
        command.Parameters.AddWithValue("$dependencyEntityId", dependencyEntityId.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DependencyAudit(reader.GetString(0), reader.GetString(1));
    }

    private sealed record EntityAudit(
        string SourceName,
        string CreatedAtUtc,
        string SchemaUpdatedAtUtc,
        string ProgressUpdatedAtUtc);

    private sealed record DependencyAudit(
        string CreatedAtUtc,
        string UpdatedAtUtc);
}
