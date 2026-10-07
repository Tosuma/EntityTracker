using Microsoft.Data.Sqlite;

using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
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
            database.GetTrackerId(),
            "Prioritized",
            requestedPriority: 4,
            responsibleDeveloper: "Platform Team",
            groupName: "Core Data");
        Assert.True(await new SqliteEntityRepository(database).TryAddAsync(prioritized));
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloper developer = await new ProjectDeveloperService(
            new SqliteProjectDeveloperStore(database)).CreateAsync(tracker.ProjectId, "PT", "Platform Team");
        await new SqliteTrackedStateStore(database).ApplyAsync(tracker.Id,
            new TrackedStateChangeSet([], [], [], [], [], [],
                responsibilitySelections: [new ResponsibilitySelection(prioritized.Id, [developer.Id])]));
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
        Assert.Equal(2, Directory.GetFiles(backupDirectory, "*.db", SearchOption.AllDirectories).Length);
        Assert.All([.. first.CreatedBackupPaths, .. nextDay.CreatedBackupPaths], path =>
            Assert.Equal($"v{SqliteDatabase.CurrentSchemaVersion}",
                Path.GetFileName(Path.GetDirectoryName(path))));
        Assert.Equal(
            SqliteDatabase.CurrentSchemaVersion,
            await ReadSchemaVersionAsync(first.CreatedBackupPaths[0]));
        Assert.Equal(
            4,
            await ReadRequestedPriorityAsync(first.CreatedBackupPaths[0], prioritized.Id));
        Assert.Equal(1, await ReadResponsibilityCountAsync(first.CreatedBackupPaths[0], prioritized.Id));
        Assert.Equal(
            "Core Data",
            await ReadGroupNameAsync(first.CreatedBackupPaths[0], prioritized.Id));
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
        string[] backupPaths = Directory.GetFiles(backupDirectory, "*.db", SearchOption.AllDirectories);
        Assert.Equal(2, backupPaths.Length);
        Assert.All(backupPaths, path => Assert.Equal("v7",
            Path.GetFileName(Path.GetDirectoryName(path))));
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
    public async Task CreateStartupBackupsAsync_PrunesRotatingBackupsButKeepsMigrationBackup()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(
            Path.GetDirectoryName(file.DatabasePath)!,
            "backups");
        Directory.CreateDirectory(backupDirectory);
        string versionDirectory = Path.Combine(backupDirectory,
            $"v{SqliteDatabase.CurrentSchemaVersion}");
        Directory.CreateDirectory(versionDirectory);
        for (int index = 0; index < 16; index++)
        {
            string path = Path.Combine(versionDirectory, $"entity-tracker-old-{index:00}.db");
            await File.WriteAllTextAsync(path, "old");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 1).AddDays(index));
        }
        string migrationDirectory = Path.Combine(backupDirectory, "v7");
        Directory.CreateDirectory(migrationDirectory);
        string migrationPath = Path.Combine(migrationDirectory,
            "entity-tracker-pre-migration-20260801T000000000Z-v7.db");
        await File.WriteAllTextAsync(migrationPath, "preserved");

        SqliteBackupService service = new(
            database,
            backupDirectory,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        SqliteBackupResult result = await service.CreateStartupBackupsAsync();

        Assert.Empty(result.Warnings);
        Assert.Equal(
            SqliteBackupService.RetainedBackupCount + 1,
            Directory.GetFiles(backupDirectory, "entity-tracker-*.db",
                SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(migrationPath));
    }

    [Fact]
    public async Task Startup_OrganizesLegacyBackupsByDatabaseSchemaRatherThanFilename()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(Path.GetDirectoryName(file.DatabasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        string dailyPath = Path.Combine(backupDirectory, "entity-tracker-daily-20260820.db");
        string migrationPath = Path.Combine(backupDirectory,
            "entity-tracker-pre-migration-20260820T100000000Z-v99.db");
        File.Copy(file.DatabasePath, dailyPath);
        File.Copy(file.DatabasePath, migrationPath);
        await SetSchemaVersionAsync(dailyPath, 7);
        await SetSchemaVersionAsync(migrationPath, 8);

        SqliteBackupService service = new(database, backupDirectory,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));
        SqliteBackupResult result = await service.CreateStartupBackupsAsync();

        Assert.True(result.Warnings.Count == 0, string.Join(Environment.NewLine, result.Warnings));
        Assert.False(File.Exists(dailyPath));
        Assert.False(File.Exists(migrationPath));
        Assert.Equal(7, await ReadSchemaVersionAsync(Path.Combine(backupDirectory,
            "v7", Path.GetFileName(dailyPath))));
        Assert.Equal(8, await ReadSchemaVersionAsync(Path.Combine(backupDirectory,
            "v8", Path.GetFileName(migrationPath))));
        Assert.Single(result.CreatedBackupPaths);
        Assert.Equal($"v{SqliteDatabase.CurrentSchemaVersion}",
            Path.GetFileName(Path.GetDirectoryName(result.CreatedBackupPaths[0])));
    }

    [Fact]
    public async Task Startup_WaitsBrieflyForABackupThatIsStillLocked()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(Path.GetDirectoryName(file.DatabasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        string dailyPath = Path.Combine(backupDirectory, "entity-tracker-daily-20260820.db");
        File.Copy(file.DatabasePath, dailyPath);
        await SetSchemaVersionAsync(dailyPath, 7);
        SqliteBackupService service = new(database, backupDirectory,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        // Another process (such as a virus scanner) holds the file for a moment; reading is allowed.
        FileStream scanner = new(dailyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task release = Task.Delay(120).ContinueWith(_ => scanner.Dispose(), TaskScheduler.Default);
        SqliteBackupResult result = await service.CreateStartupBackupsAsync();
        await release;

        Assert.True(result.Warnings.Count == 0, string.Join(Environment.NewLine, result.Warnings));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "v7", Path.GetFileName(dailyPath))));
    }

    [Fact]
    public async Task PreSyncBackup_UsesCurrentSchemaVersionFolder()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(Path.GetDirectoryName(file.DatabasePath)!, "backups");
        string path = await new SqliteBackupService(database, backupDirectory)
            .CreatePreApplyBackupAsync();

        Assert.Equal($"v{SqliteDatabase.CurrentSchemaVersion}",
            Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await ReadSchemaVersionAsync(path));
    }

    [Fact]
    public async Task Startup_LeavesUnreadableLegacyBackupUntouchedAndWarns()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string backupDirectory = Path.Combine(Path.GetDirectoryName(file.DatabasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        string unreadablePath = Path.Combine(backupDirectory,
            "entity-tracker-daily-20260820.db");
        await File.WriteAllTextAsync(unreadablePath, "not a SQLite database");

        SqliteBackupResult result = await new SqliteBackupService(database, backupDirectory)
            .CreateStartupBackupsAsync();

        Assert.True(File.Exists(unreadablePath));
        Assert.Single(result.Warnings);
        Assert.Single(result.CreatedBackupPaths);
        Assert.Equal($"v{SqliteDatabase.CurrentSchemaVersion}",
            Path.GetFileName(Path.GetDirectoryName(result.CreatedBackupPaths[0])));
    }

    private static async Task SetSchemaVersionAsync(string databasePath, int version)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync();
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
        // Without pooling, no connection keeps the file open after the read.
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
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

    private static async Task<long> ReadResponsibilityCountAsync(
        string databasePath,
        EntityId entityId)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM responsibility_periods WHERE entity_id = $id;";
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadGroupNameAsync(
        string databasePath,
        EntityId entityId)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT group_name FROM tracked_entities WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entityId.Value.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
