namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteBackupResult
{
    public SqliteBackupResult(
        IEnumerable<string>? createdBackupPaths = null,
        IEnumerable<string>? warnings = null)
    {
        CreatedBackupPaths = (createdBackupPaths ?? []).ToArray();
        Warnings = (warnings ?? []).ToArray();
    }

    public IReadOnlyList<string> CreatedBackupPaths { get; }

    public IReadOnlyList<string> Warnings { get; }
}
