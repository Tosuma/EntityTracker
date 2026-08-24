using System.Globalization;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Services;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public const int RetainedLogCount = 14;

    private readonly RollingFileLogSink _sink;

    public RollingFileLoggerProvider(
        string logDirectory,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _sink = new RollingFileLogSink(
            Path.GetFullPath(logDirectory),
            timeProvider ?? TimeProvider.System);
    }

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(categoryName, _sink);

    public void Dispose()
    {
        // The sink opens files only for each individual write.
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly RollingFileLogSink _sink;

        public RollingFileLogger(string categoryName, RollingFileLogSink sink)
        {
            _categoryName = categoryName;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

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

            _sink.Write(logLevel, _categoryName, eventId, message, exception);
        }
    }

    private sealed class RollingFileLogSink
    {
        private readonly object _gate = new();
        private readonly string _directory;
        private readonly TimeProvider _timeProvider;
        private DateOnly? _lastPrunedDate;

        public RollingFileLogSink(string directory, TimeProvider timeProvider)
        {
            _directory = directory;
            _timeProvider = timeProvider;
        }

        public void Write(
            LogLevel level,
            string category,
            EventId eventId,
            string message,
            Exception? exception)
        {
            lock (_gate)
            {
                try
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    DateOnly currentDate = DateOnly.FromDateTime(now.UtcDateTime);
                    Directory.CreateDirectory(_directory);

                    string path = Path.Combine(
                        _directory,
                        $"entity-tracker-{now:yyyyMMdd}.log");
                    StringBuilder entry = new();
                    entry.Append(now.ToString("O", CultureInfo.InvariantCulture));
                    entry.Append(" [").Append(level).Append("] ");
                    entry.Append(category);
                    if (eventId.Id != 0)
                    {
                        entry.Append(" (").Append(eventId.Id).Append(')');
                    }

                    entry.Append(": ").AppendLine(message);
                    if (exception is not null)
                    {
                        entry.AppendLine(exception.ToString());
                    }

                    File.AppendAllText(path, entry.ToString(), Encoding.UTF8);

                    if (_lastPrunedDate != currentDate)
                    {
                        PruneLogs();
                        _lastPrunedDate = currentDate;
                    }
                }
                catch (IOException)
                {
                    // Logging must never make an application operation fail.
                }
                catch (UnauthorizedAccessException)
                {
                    // Logging must never make an application operation fail.
                }
            }
        }

        private void PruneLogs()
        {
            DirectoryInfo directory = new(_directory);
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
