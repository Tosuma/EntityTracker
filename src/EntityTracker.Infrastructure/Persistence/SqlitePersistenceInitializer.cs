using EntityTracker.Application.Persistence;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqlitePersistenceInitializer : IPersistenceInitializer
{
    private readonly SqliteDatabase _database;
    private readonly SqliteBackupService _backupService;

    public SqlitePersistenceInitializer(
        SqliteDatabase database,
        SqliteBackupService backupService)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(backupService);

        _database = database;
        _backupService = backupService;
    }

    public async Task<PersistenceInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        SqliteBackupResult backup =
            await _backupService.CreateStartupBackupsAsync(cancellationToken);
        await _database.InitializeAsync(cancellationToken);
        return new PersistenceInitializationResult(backup.Warnings);
    }
}
