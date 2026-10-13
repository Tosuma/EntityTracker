using Microsoft.Extensions.Logging;

namespace EntityTracker.Infrastructure.Tests;

/// <summary>A logger for tests: keeps every entry with its level, message, exception and scopes.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly LoggerExternalScopeProvider _scopes = new();
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get { lock (_entries) return [.. _entries]; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes.Push(state);

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        List<string> scopes = [];
        _scopes.ForEachScope(static (scope, list) => list.Add(scope?.ToString() ?? ""), scopes);
        lock (_entries) _entries.Add(new Entry(logLevel, formatter(state, exception), exception, scopes));
    }

    internal sealed record Entry(LogLevel Level, string Message, Exception? Exception, IReadOnlyList<string> Scopes);
}
