using System.IO;

using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class RollingFileLoggerProviderTests
{
    [Fact]
    public void Logger_WritesDailyUtcLogWithCategoryAndException()
    {
        using TemporaryLogDirectory directory = new();
        TestTimeProvider time = new(
            new DateTimeOffset(2026, 8, 24, 23, 59, 0, TimeSpan.Zero));
        using RollingFileLoggerProvider provider = new(directory.Path, time);
        ILogger logger = provider.CreateLogger("EntityTracker.Test");

        logger.LogError(new InvalidOperationException("failure"), "Operation failed.");

        string logPath = System.IO.Path.Combine(
            directory.Path,
            "entity-tracker-20260824.log");
        Assert.True(File.Exists(logPath));
        string log = File.ReadAllText(logPath);
        Assert.Contains("EntityTracker.Test", log, StringComparison.Ordinal);
        Assert.Contains("Operation failed.", log, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_RetainsNewestFourteenDailyLogs()
    {
        using TemporaryLogDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        for (int day = 1; day <= 16; day++)
        {
            File.WriteAllText(
                System.IO.Path.Combine(directory.Path, $"entity-tracker-202608{day:00}.log"),
                "old");
        }

        using RollingFileLoggerProvider provider = new(
            directory.Path,
            new TestTimeProvider(
                new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)));

        provider.CreateLogger("Test").LogInformation("Trigger retention.");

        Assert.Equal(
            RollingFileLoggerProvider.RetainedLogCount,
            Directory.GetFiles(directory.Path, "entity-tracker-*.log").Length);
        Assert.False(File.Exists(
            System.IO.Path.Combine(directory.Path, "entity-tracker-20260801.log")));
    }

    [Fact]
    public void Logger_WhenDirectoryCannotBeCreated_DoesNotFailCaller()
    {
        using TemporaryLogDirectory directory = new();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(directory.Path)!);
        File.WriteAllText(directory.Path, "blocking file");
        using RollingFileLoggerProvider provider = new(directory.Path);

        Exception? exception = Record.Exception(() =>
            provider.CreateLogger("Test").LogWarning("This cannot be written."));

        Assert.Null(exception);
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryLogDirectory : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EntityTracker.LogTests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_root, "logs");

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
