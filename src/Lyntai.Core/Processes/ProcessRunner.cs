using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lyntai.Processes;

/// <summary>Why a buffered run's timeout fired, so a "working-but-slow" kill is distinguishable from a
/// "ran-too-long" one: <see cref="Inactivity"/> = the child went silent for the inactivity window ("dead"
/// detection); <see cref="MaxDuration"/> = it kept producing output but exceeded the absolute ceiling.</summary>
public enum ProcessTimeoutKind
{
    /// <summary>The run did not time out.</summary>
    None = 0,
    /// <summary>Killed after the inactivity window elapsed with no output — a stalled/dead child.</summary>
    Inactivity,
    /// <summary>Killed after the absolute max duration — a chatty child that simply ran too long.</summary>
    MaxDuration,
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>When <see cref="TimedOut"/>, which clock fired (inactivity vs absolute max); otherwise
    /// <see cref="ProcessTimeoutKind.None"/>.</summary>
    public ProcessTimeoutKind TimeoutKind { get; init; } = ProcessTimeoutKind.None;
}

/// <summary>A streamed process exited nonzero; carries the stderr tail for diagnostics.</summary>
public sealed class ProcessRunException(string command, int exitCode, string stdErrTail)
    : Exception($"{command} exited {exitCode}: {stdErrTail}")
{
    public int ExitCode { get; } = exitCode;
    public string StdErrTail { get; } = stdErrTail;
}

/// <summary>A streamed process exceeded its per-call timeout and was killed (entire tree).</summary>
public sealed class ProcessTimeoutException(string command, TimeSpan timeout)
    : Exception($"{command} timed out after {timeout}");

/// <summary>
/// CLI spawn hygiene in one place (design §6): <c>UseShellExecute=false</c>, <c>ArgumentList</c> only
/// (prompts carry newlines/metacharacters — never a shell), prompt over stdin, BOM-less UTF-8 both
/// directions, resolved-path cache (where.exe/which, prefer .cmd/.exe), Kill(entireProcessTree) on
/// cancel/timeout.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly ConcurrentDictionary<string, string> PathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Buffered run: returns exit code + full stdout/stderr. The <paramref name="timeout"/> is an
    /// INACTIVITY window ("dead" detection), NOT a wall clock: it is re-armed on every stdout chunk (and the
    /// stdin write), so a slow-but-ALIVE child (a big prompt, a long tool loop) that keeps producing output
    /// runs to completion, while a child that goes SILENT for the window is killed. Reads stdout in chunks
    /// rather than awaiting the whole stream, so the clock can measure the gaps between chunks — the same
    /// inactivity discipline <see cref="StreamLinesAsync"/> uses. A killed run reports <c>TimedOut=true</c>
    /// with <see cref="ProcessResult.TimeoutKind"/> = <see cref="ProcessTimeoutKind.Inactivity"/>. Caller
    /// cancellation kills the tree and rethrows.</summary>
    public async Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? timeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default)
    {
        using var process = Start(command, args, workingDirectory, environment);

        // stderr is diagnostic — drain it concurrently to the end (it completes when the child exits)
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var killCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // killing the child breaks the pipes, so a stalled read/write unblocks with EOF/IOException
        using var killReg = killCts.Token.Register(() => KillTree(process));

        // Two independent backstops feed the single kill signal, tagged (first-writer-wins) so the result
        // can name WHY it fired. Stored as int for a lock-free Interlocked set from the timer callbacks:
        //  • inactivity ("dead") — re-armed on each stdout chunk; fires on the window elapsing in TRUE SILENCE.
        //  • maxDuration ("exceeded max") — armed once; fires no matter how chatty the child stays.
        var stopReason = (int)ProcessTimeoutKind.None;
        using var idleCts = new CancellationTokenSource();
        using var idleReg = idleCts.Token.Register(() =>
        {
            Interlocked.CompareExchange(ref stopReason, (int)ProcessTimeoutKind.Inactivity, (int)ProcessTimeoutKind.None);
            killCts.Cancel();
        });
        using var maxCts = new CancellationTokenSource();
        using var maxReg = maxCts.Token.Register(() =>
        {
            Interlocked.CompareExchange(ref stopReason, (int)ProcessTimeoutKind.MaxDuration, (int)ProcessTimeoutKind.None);
            killCts.Cancel();
        });
        if (maxDuration is not null) maxCts.CancelAfter(maxDuration.Value);

        var stdout = new StringBuilder();

        // Arm the inactivity clock for the first read, then write stdin CONCURRENTLY with the read loop:
        // a large prompt can fill the stdin pipe before the child drains it, and awaiting the full write
        // first would deadlock a child that emits stdout before reading stdin. A child that never drains
        // its pipe is killed by the inactivity clock, which unblocks the writer. (Mirrors StreamLinesAsync.)
        if (timeout is not null) idleCts.CancelAfter(timeout.Value);
        var stdinTask = WriteStdinAsync(process, stdin, killCts.Token);

        try
        {
            var buffer = new char[8192];
            while (true)
            {
                if (timeout is not null) idleCts.CancelAfter(timeout.Value); // re-arm for THIS read
                int n;
                try
                {
                    // read with an uncancellable token — the kill (registered on killCts) is what unblocks a stall
                    n = await process.StandardOutput.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception) when (killCts.IsCancellationRequested)
                {
                    break; // killed (inactivity / caller) — reason resolved after the reap
                }
                catch (IOException)
                {
                    break; // broken pipe after the child exited/crashed — treat as EOF; exit code tells the story
                }
                if (n == 0) break; // EOF — child closed stdout
                stdout.Append(buffer, 0, n);
            }
            if (timeout is not null) idleCts.CancelAfter(Timeout.InfiniteTimeSpan); // stop the clock while we observe stdin

            // observe the concurrent stdin write (a broken pipe from an early child exit is already swallowed)
            try { await stdinTask.ConfigureAwait(false); } catch { /* reflected in exit code / TimedOut */ }

            // bound the final reap too (a child that closed stdout but lingers can't hang us)
            if (timeout is not null && !killCts.IsCancellationRequested) idleCts.CancelAfter(timeout.Value);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (killCts.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested(); // caller cancel propagates; only a timeout is reported as a result
                // killCts fired without a tagged reason ⇒ the caller's own token (handled above) or a race; default to inactivity
                var kind = stopReason == (int)ProcessTimeoutKind.None ? ProcessTimeoutKind.Inactivity : (ProcessTimeoutKind)stopReason;
                return new ProcessResult(-1, stdout.ToString(), stderr, TimedOut: true) { TimeoutKind = kind };
            }
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr, TimedOut: false);
        }
        finally
        {
            // any abnormal exit (a non-pipe read fault) must not leave the child running
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
        }
    }

    /// <summary>Streamed run: yields stdout lines as they arrive. The timeout is an INACTIVITY window
    /// on the child (the stdin write and each stdout read) — it deliberately does NOT count time the
    /// consumer spends between chunks, so a slow reader never gets a healthy stream killed under it.
    /// Abandoning the enumerator early (breaking out of <c>await foreach</c>) kills the child process
    /// tree. Throws <see cref="ProcessTimeoutException"/> on timeout, <see cref="ProcessRunException"/>
    /// (with the stderr tail) on nonzero exit — both after the lines produced so far were yielded.</summary>
    public async IAsyncEnumerable<string> StreamLinesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var process = Start(command, args, workingDirectory, environment);

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // killing the tree unblocks a pending read/write with EOF/IOException — no cancellable read needed
        using var killOnCancel = timeoutCts.Token.Register(() => KillTree(process));
        var timedOut = false;

        try
        {
            // Write stdin CONCURRENTLY with the stdout read loop below. A large prompt fills the stdin pipe while the
            // child is already writing to stdout; awaiting the FULL stdin write BEFORE the first stdout read deadlocks
            // both pipes (parent blocked writing stdin, child blocked writing stdout) — the child never starts its turn.
            // Firing the write lets the read loop drain stdout so the child keeps draining stdin. (RunAsync reads-first
            // for the same reason; StreamLinesAsync must not serialize write-then-read on a big prompt.)
            if (timeout is not null) timeoutCts.CancelAfter(timeout.Value); // arm for the first read
            var stdinTask = WriteStdinAsync(process, stdin, timeoutCts.Token);

            while (!timedOut && !timeoutCts.IsCancellationRequested)
            {
                string? line;
                try
                {
                    if (timeout is not null) timeoutCts.CancelAfter(timeout.Value);            // arm for this read
                    line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
                    if (timeout is not null) timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan); // stop the clock while the consumer works
                }
                catch (Exception) when (timeoutCts.IsCancellationRequested)
                {
                    timedOut = !ct.IsCancellationRequested;
                    break;
                }
                if (line is null)
                {
                    // EOF right after our own kill still reports as the timeout that caused it
                    if (timeoutCts.IsCancellationRequested) timedOut = !ct.IsCancellationRequested;
                    break;
                }
                yield return line;
            }

            // The stdout loop ended (child closed stdout, or a timeout) — observe the concurrent stdin write so it's
            // never left unobserved. A broken pipe (child exited before draining) is already swallowed in
            // WriteStdinAsync; a cancel/timeout is the same one the loop reported. The real signal is exit code / stderr.
            try { await stdinTask.ConfigureAwait(false); } catch { /* reflected in exit code / timedOut */ }

            // bound the final reap too (a child that closed stdout but lingers)
            if (timeout is not null && !timeoutCts.IsCancellationRequested) timeoutCts.CancelAfter(timeout.Value);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            if (timedOut)
                throw new ProcessTimeoutException(command, timeout ?? TimeSpan.Zero);
            if (process.ExitCode != 0)
                throw new ProcessRunException(command, process.ExitCode, Tail(stderr));
        }
        finally
        {
            // early enumerator abandonment (consumer breaks out of await foreach) resumes HERE with
            // the child still running: kill it, or it keeps generating with nothing left to reap it.
            // On the normal path the process has already exited and this is a no-op.
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
        }
    }

    /// <summary>Resolve a bare command name via where.exe/which (cached). Multiple hits prefer
    /// .cmd, then .exe (an extensionless npm shim can't be spawned by CreateProcess). Paths with
    /// separators are returned as-is.</summary>
    public static string ResolveCommandPath(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains('/') || command.Contains('\\')) return command;
        return PathCache.GetOrAdd(command, static cmd => Locate(cmd) ?? cmd);
    }

    private static string? Locate(string command)
    {
        var locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var psi = new ProcessStartInfo(locator)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(command);
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5000)) { KillTree(p); return null; }
            var hits = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hits.Length == 0) return null;
            return hits.FirstOrDefault(h => h.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                ?? hits.FirstOrDefault(h => h.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? hits[0];
        }
        catch
        {
            return null;
        }
    }

    private static System.Diagnostics.Process Start(
        string command, IReadOnlyList<string> args, string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var psi = new ProcessStartInfo(ResolveCommandPath(command))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (k, v) in environment) psi.Environment[k] = v;

        var process = new System.Diagnostics.Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException($"failed to start {command}");
        return process;
    }

    private static async Task WriteStdinAsync(System.Diagnostics.Process process, string? stdin, CancellationToken ct)
    {
        try
        {
            if (stdin is not null)
                await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
            process.StandardInput.Close(); // always signal EOF — CLIs that read stdin would hang otherwise
        }
        catch (IOException)
        {
            // broken pipe: the child exited (or was killed) before draining stdin — the exit-code /
            // stderr path reports the real story; this is not a spawn failure
            try { process.StandardInput.Close(); } catch (IOException) { }
        }
    }

    private static void KillTree(System.Diagnostics.Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    private static string Tail(string text, int max = 500) =>
        text.Length <= max ? text.Trim() : text[^max..].Trim();
}
