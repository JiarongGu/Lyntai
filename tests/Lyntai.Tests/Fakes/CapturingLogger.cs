using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Lyntai.Tests.Fakes;

/// <summary>Records every log entry at or above its minimum level — the way a test tells a SWALLOWED fault from an
/// empty result, or asserts a condition was REPORTED rather than dropped. Thread-safe.
/// <para>One instance serves any number of categories through <see cref="For{T}"/>, so a test listening to an
/// engine AND the seed source beneath it reads a single list; it is also its own <see cref="ILoggerProvider"/>, for
/// a subject that takes its logger from DI. Given an <see cref="ITestOutputHelper"/> it echoes each warning
/// there.</para></summary>
public class CapturingLogger : ILogger, ILoggerProvider
{
    private readonly List<string> _messages;
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly LogLevel _minLevel;
    private readonly ITestOutputHelper? _output;

    /// <param name="sink">Where each kept message is ALSO appended, for a test that owns the list; optional.</param>
    /// <param name="minLevel">The lowest level kept — every level by default.</param>
    /// <param name="output">Where each warning or worse is echoed; optional.</param>
    public CapturingLogger(List<string>? sink = null, LogLevel minLevel = LogLevel.Trace,
        ITestOutputHelper? output = null)
    {
        _messages = sink ?? [];
        _minLevel = minLevel;
        _output = output;
    }

    /// <summary>Keeps every level and echoes each warning to <paramref name="output"/>.</summary>
    public CapturingLogger(ITestOutputHelper? output) : this(null, LogLevel.Trace, output) { }

    /// <summary>Every kept message, in order.</summary>
    public IReadOnlyList<string> Messages { get { lock (_entries) return [.. _messages]; } }

    /// <summary>Every kept entry with its level, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (_entries) return [.. _entries]; } }

    /// <summary>Every kept level, in order.</summary>
    public IReadOnlyList<LogLevel> Levels { get { lock (_entries) return [.. _entries.Select(e => e.Level)]; } }

    /// <summary>The message of every kept warning or worse, in order.</summary>
    public IReadOnlyList<string> Warnings
    {
        get { lock (_entries) return [.. _entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message)]; }
    }

    /// <summary>A typed view writing into this same record.</summary>
    public ILogger<T> For<T>() => new Typed<T>(this);

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
        if (logLevel >= LogLevel.Warning)
            _output?.WriteLine($"{logLevel}: {message}" + (exception is null ? "" : $"\n  {exception}"));
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) => this;

    void IDisposable.Dispose() { }

    private sealed class Typed<T>(CapturingLogger sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => sink.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Log(logLevel, eventId, state, exception, formatter);
    }
}

/// <summary>The typed form, for a subject that asks for an <see cref="ILogger{TCategoryName}"/>. Keeps warnings
/// and worse by default.</summary>
public sealed class CapturingLogger<T>(List<string>? sink = null, LogLevel minLevel = LogLevel.Warning)
    : CapturingLogger(sink, minLevel), ILogger<T>;
