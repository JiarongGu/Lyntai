using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Lyntai.Tests.Fakes;

/// <summary>Records every log line with its level — the way a test tells a SWALLOWED fault from an empty
/// result, which a best-effort catch otherwise makes indistinguishable.
/// <para>One instance serves any number of categories through <see cref="For{T}"/>, so a test listening to
/// an engine AND the seed source beneath it reads a single list. Given an <see cref="ITestOutputHelper"/> it
/// also echoes each warning there.</para></summary>
internal sealed class CapturingLogger(ITestOutputHelper? output = null) : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    /// <summary>Every level logged, in order.</summary>
    public IReadOnlyList<LogLevel> Levels
    {
        get { lock (_entries) return [.. _entries.Select(e => e.Level)]; }
    }

    /// <summary>The formatted message of every warning or worse, in order.</summary>
    public IReadOnlyList<string> Warnings
    {
        get { lock (_entries) return [.. _entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message)]; }
    }

    /// <summary>A typed view writing into this same record.</summary>
    public ILogger<T> For<T>() => new Typed<T>(this);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_entries) _entries.Add((logLevel, message));
        if (logLevel >= LogLevel.Warning)
            output?.WriteLine($"{logLevel}: {message}" + (exception is null ? "" : $"\n  {exception}"));
    }

    private sealed class Typed<T>(CapturingLogger sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Log(logLevel, eventId, state, exception, formatter);
    }
}
