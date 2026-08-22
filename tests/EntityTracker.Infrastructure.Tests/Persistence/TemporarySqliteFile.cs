using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

internal sealed class TemporarySqliteFile : IAsyncDisposable
{
    private readonly string _directoryPath;

    public TemporarySqliteFile()
    {
        _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "EntityTracker.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directoryPath);
        DatabasePath = Path.Combine(_directoryPath, "entity-tracker.db");
    }

    public string DatabasePath { get; }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
