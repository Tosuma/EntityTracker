using System.Globalization;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// The level the app logs at, changeable while it runs (Settings › About › Detailed logging).
/// </summary>
public sealed class LogLevelSwitch(LogLevel minimumLevel = LogLevel.Information)
{
    private volatile int _minimumLevel = (int)minimumLevel;

    public LogLevel MinimumLevel
    {
        get => (LogLevel)_minimumLevel;
        set => _minimumLevel = (int)value;
    }
}

/// <summary>
/// Writes the app's log to one file per day in the logs folder and keeps the last
/// <see cref="RetainedLogCount"/> files. Each line has the time (UTC), level, thread, category and the
/// scopes it was written in, for example
/// <c>2026-10-13T09:15:02.123Z [Information] (12) EntityTracker.Application.GitSync.ProjectGitSync [ProjectSync 7f3a2c]: ...</c>,
/// followed by the exception, if any, with all its inner exceptions.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public const int RetainedLogCount = 14;

    private readonly RollingFileLogSink _sink;
    private readonly LogLevelSwitch _levelSwitch;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public RollingFileLoggerProvider(
        string logDirectory,
        TimeProvider? timeProvider = null,
        LogLevelSwitch? levelSwitch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _sink = new RollingFileLogSink(
            Path.GetFullPath(logDirectory),
            timeProvider ?? TimeProvider.System);
        _levelSwitch = levelSwitch ?? new LogLevelSwitch();
    }

    public string LogDirectory => _sink.Directory;

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopes = scopeProvider ?? new LoggerExternalScopeProvider();

    public void Dispose()
    {
        // The sink opens files only for each individual write.
    }

    private sealed class RollingFileLogger(string categoryName, RollingFileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= provider._levelSwitch.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (message.Length == 0 && exception is null)
            {
                return;
            }

            List<string> scopes = [];
            provider._scopes.ForEachScope(static (scope, list) =>
            {
                string? text = scope?.ToString();
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
            }, scopes);
            provider._sink.Write(logLevel, categoryName, eventId, scopes, message, exception);
        }
    }

    private sealed class RollingFileLogSink
    {
        private readonly object _gate = new();
        private readonly TimeProvider _timeProvider;
        private DateOnly? _lastPrunedDate;

        public RollingFileLogSink(string directory, TimeProvider timeProvider)
        {
            Directory = directory;
            _timeProvider = timeProvider;
        }

        public string Directory { get; }

        public void Write(
            LogLevel level,
            string category,
            EventId eventId,
            IReadOnlyList<string> scopes,
            string message,
            Exception? exception)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            StringBuilder entry = new();
            entry.Append(now.ToString("O", CultureInfo.InvariantCulture));
            entry.Append(" [").Append(level).Append("] (").Append(Environment.CurrentManagedThreadId).Append(") ");
            entry.Append(category);
            if (eventId.Id != 0)
            {
                entry.Append(" (").Append(eventId.Id).Append(')');
            }

            if (scopes.Count > 0)
            {
                entry.Append(" [").Append(string.Join(" > ", scopes)).Append(']');
            }

            entry.Append(": ").AppendLine(message);
            if (exception is not null)
            {
                // Exception.ToString includes every inner exception and its stack trace.
                entry.AppendLine(exception.ToString());
            }

            lock (_gate)
            {
                try
                {
                    DateOnly currentDate = DateOnly.FromDateTime(now.UtcDateTime);
                    System.IO.Directory.CreateDirectory(Directory);
                    string path = Path.Combine(Directory, $"entity-tracker-{now:yyyyMMdd}.log");
                    File.AppendAllText(path, entry.ToString(), Encoding.UTF8);

                    if (_lastPrunedDate != currentDate)
                    {
                        PruneLogs();
                        _lastPrunedDate = currentDate;
                    }
                }
                catch (IOException)
                {
                    // Not logged: this is the log itself, and logging must never make an operation fail.
                }
                catch (UnauthorizedAccessException)
                {
                    // Not logged: this is the log itself, and logging must never make an operation fail.
                }
            }
        }

        private void PruneLogs()
        {
            DirectoryInfo directory = new(Directory);
            foreach (FileInfo obsoleteLog in directory
                         .EnumerateFiles("entity-tracker-*.log", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(static file => file.Name, StringComparer.Ordinal)
                         .Skip(RetainedLogCount))
            {
                obsoleteLog.Delete();
            }
        }
    }
}
