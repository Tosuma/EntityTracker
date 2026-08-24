using Microsoft.Data.Sqlite;

namespace EntityTracker.DemoData.Tests;

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "EntityTracker.DemoData.Tests",
        Guid.NewGuid().ToString("N"));

    public TemporaryDatabase()
    {
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
