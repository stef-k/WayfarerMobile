using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock logger that captures log entries for testing.
/// </summary>
/// <typeparam name="T">The type being logged.</typeparam>
public class MockLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logEntries = new();

    /// <summary>
    /// Gets all captured log entries.
    /// </summary>
    public IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();

    /// <summary>
    /// Gets log entries at a specific level.
    /// </summary>
    public IEnumerable<LogEntry> GetEntriesAtLevel(LogLevel level)
        => _logEntries.Where(e => e.Level == level);

    /// <summary>
    /// Gets the count of entries at a specific level.
    /// </summary>
    public int CountAtLevel(LogLevel level)
        => _logEntries.Count(e => e.Level == level);

    /// <summary>
    /// Clears all captured log entries.
    /// </summary>
    public void Clear() => _logEntries.Clear();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry
        {
            Level = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Timestamp = DateTime.UtcNow
        });
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Represents a captured log entry.
/// </summary>
public class LogEntry
{
    public LogLevel Level { get; init; }
    public EventId EventId { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; }
}
