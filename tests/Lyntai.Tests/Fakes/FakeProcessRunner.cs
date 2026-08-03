using System.Runtime.CompilerServices;
using Lyntai.Processes;

namespace Lyntai.Tests.Fakes;

/// <summary>The one shared <see cref="IProcessRunner"/> fake: scripted <see cref="StreamLines"/>
/// (yielded first, then <see cref="ThrowsAfterLines"/> is thrown AFTER them — matching the real
/// <see cref="ProcessRunner"/>'s yield-then-throw order), a canned <see cref="RunResult"/> for the
/// buffered path, and a <see cref="Calls"/> capture of every invocation's arguments.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>One captured invocation (RunAsync or StreamLinesAsync).</summary>
    public readonly record struct Call(string Command, IReadOnlyList<string> Args, string? Stdin,
        string? WorkingDirectory, TimeSpan? InactivityTimeout, TimeSpan? MaxDuration,
        IReadOnlyDictionary<string, string>? Environment = null);

    public FakeProcessRunner(IReadOnlyList<string>? streamLines = null, Exception? throwsAfterLines = null)
    {
        StreamLines = streamLines ?? [];
        ThrowsAfterLines = throwsAfterLines;
    }

    /// <summary>Lines yielded by <see cref="StreamLinesAsync"/>.</summary>
    public IReadOnlyList<string> StreamLines { get; set; }

    /// <summary>Thrown after all <see cref="StreamLines"/> have been yielded (never instead of them).</summary>
    public Exception? ThrowsAfterLines { get; set; }

    /// <summary>Canned result returned by <see cref="RunAsync"/>.</summary>
    public ProcessResult RunResult { get; set; } = new(0, string.Empty, string.Empty);

    /// <summary>Per-call result selector for <see cref="RunAsync"/>, so a test can script a SEQUENCE of
    /// buffered runs by inspecting (command, args) — e.g. a version probe, then an update, then a
    /// re-probe. Falls back to <see cref="RunResult"/> when null; may throw to simulate a missing binary
    /// (the call is still recorded in <see cref="Calls"/>).</summary>
    public Func<string, IReadOnlyList<string>, ProcessResult>? RunHandler { get; set; }

    /// <summary>Every invocation, in order. Streamed calls record at enumeration time (when the
    /// iterator body first runs), like the real runner spawns lazily.</summary>
    public List<Call> Calls { get; } = [];

    public string? LastCommand => Calls.Count > 0 ? Calls[^1].Command : null;
    public IReadOnlyList<string>? LastArgs => Calls.Count > 0 ? Calls[^1].Args : null;
    public string? LastStdin => Calls.Count > 0 ? Calls[^1].Stdin : null;
    public string? LastWorkingDirectory => Calls.Count > 0 ? Calls[^1].WorkingDirectory : null;
    public TimeSpan? LastInactivityTimeout => Calls.Count > 0 ? Calls[^1].InactivityTimeout : null;
    public TimeSpan? LastMaxDuration => Calls.Count > 0 ? Calls[^1].MaxDuration : null;
    public IReadOnlyDictionary<string, string>? LastEnvironment => Calls.Count > 0 ? Calls[^1].Environment : null;

    public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
        TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null, CancellationToken ct = default)
    {
        Calls.Add(new Call(command, args, stdin, workingDirectory, inactivityTimeout, maxDuration, environment));
        return Task.FromResult(RunHandler is null ? RunResult : RunHandler(command, args));
    }

    public async IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,
        string? stdin = null, TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(new Call(command, args, stdin, workingDirectory, inactivityTimeout, maxDuration, environment));

        foreach (var line in StreamLines)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
            await Task.Yield();
        }

        if (ThrowsAfterLines is not null)
        {
            // yield lines first, then throw (as ProcessRunner does: yields lines so far, then throws)
            await Task.Yield();
            throw ThrowsAfterLines;
        }
    }
}
