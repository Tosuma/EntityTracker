using System.Globalization;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task InitializeAsync_FreshDatabaseCreatesVersionOneSchemaAndIsIdempotent()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath);
        Assert.Equal(1L, await ExecuteScalarInt64Async(connection, "PRAGMA user_version;"));

        string[] tableNames = await ReadStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        Assert.Contains("tracked_entities", tableNames);
        Assert.Contains("schema_dependencies", tableNames);

        string[] entityColumns = await ReadStringsAsync(
            connection,
            "SELECT name FROM pragma_table_info('tracked_entities') ORDER BY cid;");
        Assert.Contains("created_at_utc", entityColumns);
        Assert.Contains("schema_updated_at_utc", entityColumns);
        Assert.Contains("progress_updated_at_utc", entityColumns);
        Assert.DoesNotContain("rank", entityColumns);
    }

    [Fact]
    public async Task InitializeAsync_NewerSchemaVersion_ThrowsClearException()
    {
        await using TemporarySqliteFile file = new();
        await using (SqliteConnection connection = await OpenConnectionAsync(file.DatabasePath))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        SqliteDatabase database = new(file.DatabasePath);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.InitializeAsync());
        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", exception.Message, StringComparison.Ordinal);
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
    public async Task EntityRepository_MetadataAndProgressUpdatesOwnDisjointFields()
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
            DevelopmentStatus.Completed,
            "Keep this manual progress");
        Assert.True(await repository.TryAddAsync(original));
        EntityAudit originalAudit = await ReadEntityAuditAsync(file.DatabasePath, id);

        timeProvider.Advance(TimeSpan.FromHours(1));
        TrackedEntity importedMetadata = new(id, "RenamedEntity");
        Assert.True(await repository.UpdateSchemaMetadataAsync(importedMetadata));

        TrackedEntity? afterMetadataUpdate = await repository.GetAsync(id);
        Assert.NotNull(afterMetadataUpdate);
        Assert.Equal("RenamedEntity", afterMetadataUpdate.SourceName);
        Assert.Equal(DevelopmentStatus.Completed, afterMetadataUpdate.Status);
        Assert.Equal("Keep this manual progress", afterMetadataUpdate.Notes);
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
        Assert.True(await repository.UpdateProgressAsync(changedProgress));

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
        TrackedEntity missing = new(EntityId.New(), "Missing");

        Assert.False(await repository.UpdateSchemaMetadataAsync(missing));
        Assert.False(await repository.UpdateProgressAsync(missing));
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
            DevelopmentStatus.Completed,
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
        Assert.Equal(DevelopmentStatus.Completed, reloadedAlpha.Status);
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

    private sealed record EntityAudit(
        string SourceKey,
        string CreatedAtUtc,
        string SchemaUpdatedAtUtc,
        string ProgressUpdatedAtUtc);

    private sealed record DependencyAudit(
        string CreatedAtUtc,
        string UpdatedAtUtc);
}
