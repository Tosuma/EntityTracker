using Microsoft.Data.Sqlite;

using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public async Task CreateStartupBackupsAsync_CreatesOneReadableDailyBackupPerUtcDay()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackedEntity prioritized = new(
            EntityId.New(),
            "Prioritized",
            requestedPriority: 4,
            responsibleDeveloper: "Platform Team");
        Assert.True(await new SqliteEntityRepository(database).TryAddAsync(prioritized));
        string backupDirectory = Path.Combine(
            Path.GetDirectoryName(file.DatabasePath)!,
            "backups");
        MutableTimeProvider time = new(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        SqliteBackupService service = new(database, backupDirectory, time);

        SqliteBackupResult first = await service.CreateStartupBackupsAsync();
        SqliteBackupResult sameDay = await service.CreateStartupBackupsAsync();
        time.Advance(TimeSpan.FromDays(1));
        SqliteBackupResult nextDay = await service.CreateStartupBackupsAsync();

        Assert.Single(first.CreatedBackupPaths);
        Assert.Empty(sameDay.CreatedBackupPaths);
        Assert.Single(nextDay.CreatedBackupPaths);
        Assert.Empty(first.Warnings);
        Assert.Equal(2, Directory.GetFiles(backupDirectory, "*.db").Length);
        Assert.Equal(
            SqliteDatabase.CurrentSchemaVersion,
            await ReadSchemaVersionAsync(first.CreatedBackupPaths[0]));
        Assert.Equal(
            4,
            await ReadRequestedPriorityAsync(first.CreatedBackupPaths[0], prioritized.Id));
        Assert.Equal(
            "Platform Team",
            await ReadResponsibleDeveloperAsync(first.CreatedBackupPaths[0], prioritized.Id));
    }

    [Fact]
    public async Task CreateStartupBackupsAsync_WhenMigrationIsPending_CreatesForcedBackup()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={file.DatabasePath}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE schema_import_summary; " +
                                  "CREATE TABLE marker(value TEXT); " +
                                  "INSERT INTO marker(value) VALUES ('before migration'); " +
                                  "PRAGMA user_version = 7;";
            await command.ExecuteNonQueryAsync();
        }

        string backupDirectory = Path.Combine(
            Path.GetDirectoryName(file.DatabasePath)!,
            "backups");
        SqliteBackupService backupService = new(
            database,
            backupDirectory,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));
        SqlitePersistenceInitializer initializer = new(database, backupService);

        PersistenceInitializationResult result = await initializer.InitializeAsync();

        Assert.Empty(result.Warnings);
        string[] backupPaths = Directory.GetFiles(backupDirectory, "*.db");
        Assert.Equal(2, backupPaths.Length);
        string migrationBackup = Assert.Single(
            backupPaths,
            static path => path.Contains("pre-migration", StringComparison.Ordinal));
        Assert.Equal(7, await ReadSchemaVersionAsync(migrationBackup));
        Assert.Equal("before migration", await ReadMarkerAsync(migrationBackup));
        Assert.Equal(
            SqliteDatabase.CurrentSchemaVersion,
            await database.GetStoredSchemaVersionAsync());
    }

    [Fact]
    public async Task CreateStartupBackupsAsync_PrunesToNewestFourteenBackups()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(
            Path.GetDirectoryName(file.DatabasePath)!,
            "backups");
        Directory.CreateDirectory(backupDirectory);
        for (int index = 0; index < 16; index++)
        {
            string path = Path.Combine(backupDirectory, $"entity-tracker-old-{index:00}.db");
            await File.WriteAllTextAsync(path, "old");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 1).AddDays(index));
        }

        SqliteBackupService service = new(
            database,
            backupDirectory,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        SqliteBackupResult result = await service.CreateStartupBackupsAsync();

        Assert.Empty(result.Warnings);
        Assert.Equal(
            SqliteBackupService.RetainedBackupCount,
            Directory.GetFiles(backupDirectory, "entity-tracker-*.db").Length);
    }

    [Fact]
    public async Task Initializer_WhenBackupFails_ReturnsWarningAndContinuesInitialization()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string invalidBackupDirectory = Path.Combine(
            Path.GetDirectoryName(file.DatabasePath)!,
            "not-a-directory");
        await File.WriteAllTextAsync(invalidBackupDirectory, "blocking file");
        SqliteBackupService backupService = new(database, invalidBackupDirectory);
        SqlitePersistenceInitializer initializer = new(database, backupService);

        PersistenceInitializationResult result =
            await initializer.InitializeAsync();

        Assert.Single(result.Warnings);
        Assert.Equal(
            SqliteDatabase.CurrentSchemaVersion,
            await database.GetStoredSchemaVersionAsync());
    }

    private static async Task<int> ReadSchemaVersionAsync(string databasePath)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadMarkerAsync(string databasePath)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM marker;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int?> ReadRequestedPriorityAsync(
        string databasePath,
        EntityId entityId)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT requested_priority FROM tracked_entities WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<string> ReadResponsibleDeveloperAsync(
        string databasePath,
        EntityId entityId)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT responsible_developer FROM tracked_entities WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
