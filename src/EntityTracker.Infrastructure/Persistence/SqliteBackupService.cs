using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteBackupService
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

        try
        {
            FileInfo databaseFile = new(_database.DatabasePath);
            if (!databaseFile.Exists || databaseFile.Length == 0)
            {
                return new SqliteBackupResult();
            }

            Directory.CreateDirectory(_backupDirectory);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            int storedSchemaVersion = await _database.GetStoredSchemaVersionAsync(cancellationToken);

            string dailyPath = Path.Combine(
                _backupDirectory,
                $"entity-tracker-daily-{now:yyyyMMdd}.db");
            if (!File.Exists(dailyPath))
            {
                await CreateOnlineBackupAsync(dailyPath, cancellationToken);
                createdPaths.Add(dailyPath);
            }

            if (storedSchemaVersion != SqliteDatabase.CurrentSchemaVersion)
            {
                string migrationPath = Path.Combine(
                    _backupDirectory,
                    $"entity-tracker-pre-migration-{now:yyyyMMddTHHmmssfffZ}" +
                    $"-v{storedSchemaVersion}.db");
                await CreateOnlineBackupAsync(migrationPath, cancellationToken);
                createdPaths.Add(migrationPath);
            }

            PruneBackups();
            return new SqliteBackupResult(createdPaths);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SqliteBackupResult(
                createdPaths,
                [
                    "EntityTracker could not create its startup database backup. " +
                    $"Startup will continue. {exception.Message}"
                ]);
        }
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
                     .EnumerateFiles("entity-tracker-*.db", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static file => file.LastWriteTimeUtc)
                     .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
                     .Skip(RetainedBackupCount))
        {
            obsoleteBackup.Delete();
        }
    }
}
