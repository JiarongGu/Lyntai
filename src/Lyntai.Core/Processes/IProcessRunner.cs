namespace Lyntai.Processes;

/// <summary>
/// The process-spawning seam used by CLI-based providers. The default <see cref="ProcessRunner"/>
/// applies the family's spawn hygiene; register your own implementation (before <c>AddLyntai</c>, or
/// via <c>services.AddSingleton&lt;IProcessRunner&gt;(...)</c>) to own how child processes are launched —
/// sandboxing, a custom shell, remote/audited execution, resource limits, etc.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Buffered run: returns exit code + full stdout/stderr. Honors the two clocks (killing the
    /// process tree on expiry, reported via <see cref="ProcessResult.TimeoutKind"/>) and caller
    /// cancellation. <paramref name="inactivityTimeout"/> is an INACTIVITY window — the default
    /// <see cref="ProcessRunner"/> re-arms it on every stdout chunk and stdin-drain slice, so a
    /// slow-but-ALIVE child isn't killed, only one that goes silent for the window;
    /// <paramref name="maxDuration"/> is an absolute ceiling on total runtime (a backstop for a child
    /// that never stalls but never finishes). A custom runner defines its own clock policy but should
    /// still bound the call by both, killing the tree on expiry.</summary>
    Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default);

    /// <summary>Streamed run: yields stdout lines as they arrive. <paramref name="inactivityTimeout"/> is
    /// the same inactivity window as the buffered run (it deliberately does NOT count time the consumer
    /// spends between lines); <paramref name="maxDuration"/> is the absolute ceiling (a chatty child that
    /// never finishes is killed too). Throws <see cref="ProcessTimeoutException"/> when either clock fires
    /// and <see cref="ProcessRunException"/> (with the stderr tail) on nonzero exit — both after the lines
    /// produced so far have been yielded. Abandoning the enumerator early terminates the child.</summary>
    IAsyncEnumerable<string> StreamLinesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default);
}
