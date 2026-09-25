using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Fakes;

/// <summary>A logger that keeps every entry at or above its minimum level — Warning by default — so a test can
/// assert a condition was REPORTED rather than dropped. Thread-safe. It is also its own
/// <see cref="ILoggerProvider"/>, for a subject that takes its logger from DI.</summary>
public class CapturingLogger : ILogger, ILoggerProvider
{
    private readonly List<string> _messages;
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly LogLevel _minLevel;

    /// <param name="sink">Where each kept message is ALSO appended, for a test that owns the list; optional.</param>
    /// <param name="minLevel">The lowest level kept.</param>
    public CapturingLogger(List<string>? sink = null, LogLevel minLevel = LogLevel.Warning)
    {
        _messages = sink ?? [];
        _minLevel = minLevel;
    }

    /// <summary>Every kept message, in order.</summary>
    public IReadOnlyList<string> Messages { get { lock (_entries) return [.. _messages]; } }

    /// <summary>Every kept entry with its level, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (_entries) return [.. _entries]; } }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < _minLevel) return;
        var message = formatter(state, exception);
        lock (_entries)
        {
            _entries.Add((logLevel, message));
            _messages.Add(message);
        }
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) => this;

    void IDisposable.Dispose() { }
}

/// <summary>The typed form, for a subject that asks for an <see cref="ILogger{TCategoryName}"/>.</summary>
public sealed class CapturingLogger<T>(List<string>? sink = null, LogLevel minLevel = LogLevel.Warning)
    : CapturingLogger(sink, minLevel), ILogger<T>;
