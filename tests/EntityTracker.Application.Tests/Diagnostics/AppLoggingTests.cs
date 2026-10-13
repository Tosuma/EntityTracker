using EntityTracker.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Application.Tests.Diagnostics;

public sealed class AppLoggingTests
{
    [Fact]
    public void LogHandled_LogsTheWholeFailureOnceAndThenOnlyPointsBackToIt()
    {
        ListLogger logger = new();
        InvalidOperationException failure = new("Git fetch failed.");

        logger.LogHandled(failure, "Sync of Project {ProjectId} failed.", 7);
        logger.LogHandled(failure, "Sync of Project {ProjectId} failed.", 7);

        Assert.Collection(logger.Entries,
            first =>
            {
                Assert.Equal(LogLevel.Error, first.Level);
                Assert.Same(failure, first.Exception);
                Assert.Equal("Sync of Project 7 failed.", first.Message);
            },
            second =>
            {
                Assert.Equal(LogLevel.Information, second.Level);
                Assert.Null(second.Exception);
                Assert.Equal("Sync of Project 7 failed. The failure (InvalidOperationException) is logged above.", second.Message);
            });
    }

    [Fact]
    public void CreateLogger_WritesToTheFactorySetLater()
    {
        ILogger logger = AppLogging.CreateLogger<AppLoggingTests>();
        logger.LogInformation("Before start-up.");
        ListLogger target = new();
        ILoggerFactory previous = AppLogging.Factory;
        try
        {
            AppLogging.Factory = new SingleLoggerFactory(target);
            logger.LogInformation("After start-up.");
        }
        finally { AppLogging.Factory = previous; }

        Assert.Equal("After start-up.", Assert.Single(target.Entries).Message);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class SingleLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}
