namespace Lyntai.Processes;

/// <summary>
/// The process-spawning seam used by CLI-based providers. The default <see cref="ProcessRunner"/>
/// applies the family's spawn hygiene; register your own implementation (before <c>AddLyntai</c>, or
/// via <c>services.AddSingleton&lt;IProcessRunner&gt;(...)</c>) to own how child processes are launched —
/// sandboxing, a custom shell, remote/audited execution, resource limits, etc.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Buffered run: returns exit code + full stdout/stderr. Honors a per-call timeout
    /// (killing the process tree on expiry, reported as <c>TimedOut</c>) and caller cancellation.</summary>
    Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default);

    /// <summary>Streamed run: yields stdout lines as they arrive. Throws
    /// <see cref="ProcessTimeoutException"/> on timeout and <see cref="ProcessRunException"/> (with the
    /// stderr tail) on nonzero exit, after the lines produced so far have been yielded. Abandoning the
    /// enumerator early terminates the child.</summary>
    IAsyncEnumerable<string> StreamLinesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default);
}
