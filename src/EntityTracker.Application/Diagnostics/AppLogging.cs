using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Application.Diagnostics;

/// <summary>
/// The app's logger factory for classes the service container doesn't create (views, small view
/// models, helpers). Classes the container creates take an <see cref="ILogger{T}"/> instead.
/// <see cref="CreateLogger{T}"/> can be called from a static field: the logger looks up
/// <see cref="Factory"/> each time, so it writes to the app's log once start-up has set it, and
/// nowhere before (or in tests that don't set it).
/// </summary>
public static class AppLogging
{
    private static volatile ILoggerFactory _factory = NullLoggerFactory.Instance;

    public static ILoggerFactory Factory
    {
        get => _factory;
        set => _factory = value ?? NullLoggerFactory.Instance;
    }

    public static ILogger CreateLogger<T>() => new DeferredLogger(typeof(T).FullName ?? typeof(T).Name);

    private const string LoggedKey = "EntityTracker.Logged";

    /// <summary>Records that <paramref name="exception"/> was logged in full where it happened.</summary>
    public static void MarkLogged(Exception exception)
    {
        try { exception.Data[LoggedKey] = true; }
        catch (Exception markFailure) when (markFailure is NotSupportedException or ArgumentException)
        {
            // Not logged: a read-only Data dictionary only means the failure may be logged twice.
        }
    }

    public static bool IsLogged(Exception exception) => exception.Data[LoggedKey] is true;

    /// <summary>
    /// Logs a failure the caller handled (for example by telling the user). If the operation that
    /// failed already logged it in full, this writes one line pointing back to it; otherwise it
    /// logs the whole exception as an Error. <paramref name="message"/> is a message template.
    /// </summary>
    public static void LogHandled(this ILogger logger, Exception exception, string message, params object?[] args)
    {
        if (IsLogged(exception))
        {
            logger.LogInformation(message + " The failure ({ErrorType}) is logged above.",
                [.. args, exception.GetType().Name]);
            return;
        }
        logger.LogError(exception, message, args);
        MarkLogged(exception);
    }

    private sealed class DeferredLogger(string category) : ILogger
    {
        private ILoggerFactory? _boundFactory;
        private ILogger _logger = NullLogger.Instance;

        private ILogger Current
        {
            get
            {
                ILoggerFactory factory = _factory;
                if (!ReferenceEquals(factory, _boundFactory))
                {
                    _logger = factory.CreateLogger(category);
                    _boundFactory = factory;
                }
                return _logger;
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Current.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Current.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Current.Log(logLevel, eventId, state, exception, formatter);
    }
}
