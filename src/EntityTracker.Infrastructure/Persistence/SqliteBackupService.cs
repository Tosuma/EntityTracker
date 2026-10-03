using Microsoft.Data.Sqlite;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteBackupService : IProjectSyncBackup
{
    public const int RetainedBackupCount = 14;

    private readonly SqliteDatabase _database;
    private readonly string _backupDirectory;
    private readonly TimeProvider _timeProvider;

    public SqliteBackupService(
        SqliteDatabase database,
        string backupDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        _database = database;
        _backupDirectory = Path.GetFullPath(backupDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SqliteBackupResult> CreateStartupBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> createdPaths = [];
        List<string> warnings = [];

        try
        {
            Directory.CreateDirectory(_backupDirectory);
            await OrganizeExistingBackupsAsync(warnings, cancellationToken);

            FileInfo databaseFile = new(_database.DatabasePath);
            if (!databaseFile.Exists || databaseFile.Length == 0)
            {
                return new SqliteBackupResult(warnings: warnings);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            int storedSchemaVersion = await _database.GetStoredSchemaVersionAsync(cancellationToken);
            string versionDirectory = GetVersionDirectory(storedSchemaVersion);
            Directory.CreateDirectory(versionDirectory);

            string dailyPath = Path.Combine(
                versionDirectory,
                $"entity-tracker-daily-{now:yyyyMMdd}.db");
            if (!File.Exists(dailyPath))
            {
                await CreateOnlineBackupAsync(dailyPath, cancellationToken);
                createdPaths.Add(dailyPath);
            }

            if (storedSchemaVersion != SqliteDatabase.CurrentSchemaVersion)
            {
                string migrationPath = Path.Combine(
                    versionDirectory,
                    $"entity-tracker-pre-migration-{now:yyyyMMddTHHmmssfffZ}" +
                    $"-v{storedSchemaVersion}.db");
                await CreateOnlineBackupAsync(migrationPath, cancellationToken);
                createdPaths.Add(migrationPath);
            }

            PruneBackups();
            return new SqliteBackupResult(createdPaths, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add("EntityTracker could not create its startup database backup. " +
                         $"Startup will continue. {exception.Message}");
            return new SqliteBackupResult(createdPaths, warnings);
        }
    }

    public async Task<string> CreatePreApplyBackupAsync(CancellationToken cancellationToken = default)
    {
        int storedSchemaVersion = await _database.GetStoredSchemaVersionAsync(cancellationToken);
        string versionDirectory = GetVersionDirectory(storedSchemaVersion);
        Directory.CreateDirectory(versionDirectory);
        string path = Path.Combine(versionDirectory,
            $"entity-tracker-pre-sync-{_timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.db");
        await CreateOnlineBackupAsync(path, cancellationToken);
        PruneBackups();
        return path;
    }

    private string GetVersionDirectory(int version) =>
        Path.Combine(_backupDirectory, $"v{version}");

    private async Task OrganizeExistingBackupsAsync(
        List<string> warnings, CancellationToken cancellationToken)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(
                     _backupDirectory, "entity-tracker-*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                int version = await ReadBackupSchemaVersionAsync(sourcePath, cancellationToken);
                string versionDirectory = GetVersionDirectory(version);
                Directory.CreateDirectory(versionDirectory);
                string destinationPath = Path.Combine(versionDirectory, Path.GetFileName(sourcePath));
                File.Move(sourcePath, destinationPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                warnings.Add($"Could not organize database backup '{Path.GetFileName(sourcePath)}': " +
                             exception.Message);
            }
        }
    }

    private static async Task<int> ReadBackupSchemaVersionAsync(
        string path, CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using SqliteConnection connection = new(connectionString.ToString());
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CreateOnlineBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SqliteConnectionStringBuilder destinationConnectionString = new()
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        try
        {
            await using SqliteConnection source =
                await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteConnection destination =
                new(destinationConnectionString.ToString());
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }
    }

    private void PruneBackups()
    {
        DirectoryInfo directory = new(_backupDirectory);
        foreach (FileInfo obsoleteBackup in directory
                     .EnumerateDirectories("v*", SearchOption.TopDirectoryOnly)
                     .SelectMany(version => version.EnumerateFiles(
                         "entity-tracker-*.db", SearchOption.TopDirectoryOnly))
                     .Where(static file => !file.Name.StartsWith(
                         "entity-tracker-pre-migration-", StringComparison.Ordinal))
                     .OrderByDescending(static file => file.LastWriteTimeUtc)
                     .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
                     .Skip(RetainedBackupCount))
        {
            obsoleteBackup.Delete();
        }
    }
}
