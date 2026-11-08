using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr,
    ProcessTimeoutKind TimeoutKind = ProcessTimeoutKind.None)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>Whether a clock killed the run — COMPUTED from <see cref="TimeoutKind"/> (the two can't
    /// contradict; which clock fired is the kind).</summary>
    public bool TimedOut => TimeoutKind != ProcessTimeoutKind.None;

    /// <summary>The last <paramref name="max"/> characters of <see cref="StdErr"/>, trimmed — what a failure
    /// message quotes without carrying a whole log. The END, because that is where a child says why it
    /// stopped; never use it on a document that must be parsed whole.</summary>
    /// <param name="max">The most characters kept.</param>
    public string StdErrTail(int max = 500) => TailOf(StdErr, max);

    /// <summary>The same tail of whichever stream the child spoke on: stdout when it wrote anything, else
    /// stderr.</summary>
    internal string OutputTail(int max = 500) => TailOf(StdOut.Length > 0 ? StdOut : StdErr, max);

    private static string TailOf(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }
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
/// directions, resolved-path cache (where.exe/which, prefer .cmd/.exe/.ps1), a <c>.ps1</c> launcher shim
/// hosted in PowerShell (CreateProcess can't exec it directly), Kill(entireProcessTree) on cancel/timeout.
/// This makes the default runner work for the common Windows CLI shims out of the box — a BYO
/// <see cref="IProcessRunner"/> is only needed for a genuinely different launch policy (sandbox, remote, …).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly ConcurrentDictionary<string, string> PathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The clock the absolute <c>maxDuration</c> ceiling is armed on; the inactivity window and the
    /// kill stay on real timers. System time unless a test owns it, so the ceiling can be moved after the
    /// child has demonstrably started instead of racing the child's own start-up.</summary>
    internal TimeProvider CeilingClock { get; init; } = TimeProvider.System;

    /// <summary>Buffered run: returns exit code + full stdout/stderr. <paramref name="inactivityTimeout"/>
    /// is an INACTIVITY window ("dead" detection), NOT a wall clock: it is re-armed on every stdout chunk
    /// and every drained stdin slice, so a slow-but-ALIVE child (a big prompt, a long tool loop) runs to
    /// completion, while a child that goes SILENT for the window is killed. Reads stdout in chunks rather
    /// than awaiting the whole stream, so the clock can measure the gaps between chunks — the same
    /// inactivity discipline <see cref="StreamLinesAsync"/> uses. A killed run reports <c>TimedOut=true</c>
    /// with <see cref="ProcessResult.TimeoutKind"/> naming the clock that fired. Caller cancellation kills
    /// the tree and rethrows.</summary>
    public async Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken ct = default)
    {
        using var process = Start(command, args, workingDirectory, environment);

        // stderr is diagnostic — drain it concurrently to the end (it completes when the child exits)
        var stderr = new PipeCapture();
        _ = stderr.Start(process.StandardError);
        var exited = process.WaitForExitAsync(CancellationToken.None);

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
        using var maxCts = new CancellationTokenSource(Timeout.InfiniteTimeSpan, CeilingClock);
        using var maxReg = maxCts.Token.Register(() =>
        {
            Interlocked.CompareExchange(ref stopReason, (int)ProcessTimeoutKind.MaxDuration, (int)ProcessTimeoutKind.None);
            killCts.Cancel();
        });
        if (maxDuration is not null) maxCts.CancelAfter(maxDuration.Value);

        var stdout = new PipeCapture();

        // Arm the inactivity clock for the first read, then write stdin CONCURRENTLY with the read loop:
        // a large prompt can fill the stdin pipe before the child drains it, and awaiting the full write
        // first would deadlock a child that emits stdout before reading stdin. A child that never drains
        // its pipe is killed by the inactivity clock, which unblocks the writer. (Mirrors StreamLinesAsync.)
        if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value);
        // each drained stdin slice re-arms the clock: a child actively CONSUMING a large prompt is alive
        var stdinTask = WriteStdinAsync(process, stdin,
            onProgress: () => { if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); }, killCts.Token);

        try
        {
            // read with an uncancellable token — the kill (registered on killCts) is what unblocks a stall; a
            // killed read is reason-resolved after the removal, and a broken pipe is EOF (the exit code tells the story)
            var stdoutPump = stdout.Start(process.StandardOutput,
                onRead: () => { if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); }, // re-arm for THIS read
                killed: () => killCts.IsCancellationRequested);
            if (await DrainedAsync(stdoutPump, exited, stdout).ConfigureAwait(false)) await stdoutPump.ConfigureAwait(false);
            else KillTree(process); // the child is gone and a grandchild still holds the pipe: what was read stands
            var stderrText = await ObserveStdinAndReapAsync(process, stdinTask, stderr, exited,
                reArm: () => { if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); },
                killed: () => killCts.IsCancellationRequested).ConfigureAwait(false);

            if (killCts.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested(); // caller cancel propagates; only a timeout is reported as a result

                // …but a kill that FIRED is not a kill that BEAT the child: one that exited 0 as the timer
                // fired holds a complete, already-billed stdout, and reporting a stall would make the router
                // pay for a second turn. Both paths ask through TimedOut.
                if (TimedOut(true, process.ExitCode))
                {
                    // killCts fired without a tagged reason ⇒ the caller's own token (handled above) or a race; default to inactivity
                    var kind = stopReason == (int)ProcessTimeoutKind.None ? ProcessTimeoutKind.Inactivity : (ProcessTimeoutKind)stopReason;
                    return new ProcessResult(-1, stdout.ToString(), stderrText, kind);
                }
            }
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderrText);
        }
        finally
        {
            // any abnormal exit (a non-pipe read fault) must not leave the child running
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
        }
    }

    /// <summary>Did the kill BEAT the child, or did the child finish first? A kill request on its own does
    /// not mean a timeout: <c>KillTree</c> is a no-op against a process that has already gone, so a child
    /// exiting <c>0</c> as the clock fires leaves the flag set and the run entirely successful. ONE function
    /// for <c>RunAsync</c> and <c>StreamLinesAsync</c>, so the two paths cannot answer differently; internal and
    /// unit-tested through the truth table, because the race it settles cannot be driven deterministically from
    /// outside.</summary>
    /// <param name="killRequested">Whether a stop was requested (an inactivity or max-duration clock).</param>
    /// <param name="exitCode">The removed child's exit code.</param>
    internal static bool TimedOut(bool killRequested, int exitCode) => killRequested && exitCode != 0;

    /// <summary>Streamed run: yields stdout lines as they arrive. <paramref name="inactivityTimeout"/> is
    /// an INACTIVITY window on the child (the stdin write and each stdout read) — it deliberately does NOT
    /// count time the consumer spends between chunks, so a slow reader never gets a healthy stream killed
    /// under it. <paramref name="maxDuration"/> is the absolute ceiling (mirrors <see cref="RunAsync"/>'s
    /// backstop — a chatty child that never finishes is killed too). Abandoning the enumerator early
    /// (breaking out of <c>await foreach</c>) kills the child process tree. Throws
    /// <see cref="ProcessTimeoutException"/> when either clock fires, <see cref="ProcessRunException"/>
    /// (with the stderr tail) on nonzero exit — both after the lines produced so far were yielded.</summary>
    public IAsyncEnumerable<string> StreamLinesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken ct = default) =>
        StreamCoreAsync(command, args, stdin, inactivityTimeout, maxDuration, workingDirectory, environment, null,
            static process => process.StandardOutput.ReadLineAsync(CancellationToken.None), ct);

    /// <summary>Streamed run with a live stdin — <see cref="IProcessRunner.ExchangeLinesAsync(string, IReadOnlyList{string}, IAsyncEnumerable{string}, TimeSpan?, TimeSpan?, string?, IReadOnlyDictionary{string, string?}?, CancellationToken)"/>. The same
    /// core as <see cref="StreamLinesAsync"/> with the stdin writer swapped, so the two cannot drift on the
    /// clocks or the kill.</summary>
    public IAsyncEnumerable<string> ExchangeLinesAsync(
        string command,
        IReadOnlyList<string> args,
        IAsyncEnumerable<string> stdinLines,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stdinLines);
        return StreamCoreAsync(command, args, null, inactivityTimeout, maxDuration, workingDirectory, environment, stdinLines,
            static process => process.StandardOutput.ReadLineAsync(CancellationToken.None), ct);
    }

    /// <summary>The shipped runner honours an <see cref="InactivityPause"/>.</summary>
    public bool CanPauseInactivity => true;

    /// <summary>Streamed run with a live stdin whose inactivity window is paused while <paramref name="pause"/> is held —
    /// <see cref="IProcessRunner.ExchangeLinesAsync(string, IReadOnlyList{string}, IAsyncEnumerable{string}, InactivityPause, TimeSpan?, TimeSpan?, string?, IReadOnlyDictionary{string, string?}?, CancellationToken)"/>.
    /// The same core as the other live-stdin run, so the clocks and the kill cannot drift: a hold stops the window and its
    /// release starts it again from that moment, and the maximum duration is untouched.</summary>
    public IAsyncEnumerable<string> ExchangeLinesAsync(
        string command,
        IReadOnlyList<string> args,
        IAsyncEnumerable<string> stdinLines,
        InactivityPause pause,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stdinLines);
        ArgumentNullException.ThrowIfNull(pause);
        return StreamCoreAsync(command, args, null, inactivityTimeout, maxDuration, workingDirectory, environment, stdinLines,
            static process => process.StandardOutput.ReadLineAsync(CancellationToken.None), ct, pause);
    }

    /// <summary>Streamed BINARY run — <see cref="IProcessRunner.StreamBytesAsync"/>. The read goes through
    /// the raw base stream, never the encoding-aware reader, so every byte arrives as written; chunk
    /// boundaries are the pipe's. Same clocks, same kill discipline, same terminal exceptions as the line
    /// stream — the two are ONE core with the read swapped, so they cannot drift.</summary>
    public IAsyncEnumerable<byte[]> StreamBytesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken ct = default)
    {
        // one buffer per enumeration: the core reads strictly sequentially, and each yielded chunk is a
        // fresh copy, so the consumer may hold chunks across reads
        var buffer = new byte[16 * 1024];
        return StreamCoreAsync(command, args, stdin, inactivityTimeout, maxDuration, workingDirectory, environment, null,
            async process =>
            {
                var n = await process.StandardOutput.BaseStream
                    .ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                return n == 0 ? null : buffer[..n];
            }, ct);
    }

    private async IAsyncEnumerable<T> StreamCoreAsync<T>(
        string command,
        IReadOnlyList<string> args,
        string? stdin,
        TimeSpan? inactivityTimeout,
        TimeSpan? maxDuration,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        IAsyncEnumerable<string>? stdinLines,
        Func<Process, ValueTask<T?>> readOne,
        [EnumeratorCancellation] CancellationToken ct = default,
        InactivityPause? pause = null) where T : class
    {
        using var process = Start(command, args, workingDirectory, environment);

        // only the 500-char TAIL is ever reported (ProcessRunException) — drain stderr bounded, so a
        // child spewing MBs of diagnostics while streaming can't grow the parent's memory with it.
        // (RunAsync's full-stderr buffer is contract — its ProcessResult returns the whole stream.)
        var stderr = new PipeCapture(StdErrTailChars);
        _ = stderr.Start(process.StandardError);
        var exited = process.WaitForExitAsync(CancellationToken.None);
        // a stdin producer's own fault is the exchange's: it ends the child and is rethrown once the lines read are yielded
        Exception? producerFault = null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // killing the tree unblocks a pending read/write with EOF/IOException — no cancellable read needed
        using var killOnCancel = timeoutCts.Token.Register(() => KillTree(process));
        // the absolute ceiling (RunAsync's backstop, mirrored): armed ONCE; firing feeds the same kill
        // path as the inactivity clock, so a chatty child that never finishes is bounded too. It tags itself
        // on the way through (RunAsync tags both clocks into stopReason; only this one is taggable here), so
        // ProcessTimeoutException can name the window that actually fired instead of always naming inactivity.
        using var maxCts = new CancellationTokenSource(Timeout.InfiniteTimeSpan, CeilingClock);
        var hitMaxDuration = false;
        using var maxReg = maxCts.Token.Register(() => { Volatile.Write(ref hitMaxDuration, true); timeoutCts.Cancel(); });
        if (maxDuration is not null) maxCts.CancelAfter(maxDuration.Value);
        var timedOut = false;

        try
        {
            // Write stdin CONCURRENTLY with the stdout read loop below. A large prompt fills the stdin pipe while the
            // child is already writing to stdout; awaiting the FULL stdin write BEFORE the first stdout read deadlocks
            // both pipes (parent blocked writing stdin, child blocked writing stdout) — the child never starts its turn.
            // Firing the write lets the read loop drain stdout so the child keeps draining stdin. (RunAsync reads-first
            // for the same reason; StreamLinesAsync must not serialize write-then-read on a big prompt.)
            // Writer-progress re-arms are gated OFF until the stdout loop ends: during the loop the clock is
            // deliberately STOPPED while the consumer works, and a writer re-arm there would leave it running
            // through consumer dwell — killing a healthy stream under a slow reader.
            var observeStdin = false;
            // The window is armed only while a read is pending and no pause is held (a held pause is the CALLER waiting, so a
            // silent child is not a dead one). A pause opening or closing re-arms from the state as it is NOW, converging
            // however the two threads interleave: the arm is repeated until the pause reads the same before and after it.
            var reading = false;
            void Rearm()
            {
                if (inactivityTimeout is null || !Volatile.Read(ref reading)) return;
                while (true)
                {
                    var paused = pause?.IsPaused == true;
                    timeoutCts.CancelAfter(paused ? Timeout.InfiniteTimeSpan : inactivityTimeout.Value);
                    if ((pause?.IsPaused == true) == paused) break;
                }
                // a read that ended while this ran has stopped the clock for the consumer: leave it stopped
                if (!Volatile.Read(ref reading)) timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }

            using var pauseWatch = pause?.Subscribe(Rearm);
            Volatile.Write(ref reading, true);
            Rearm();   // arm for the first read
            // a live stdin has its own token: a child that exits leaves the sequence it was reading unfinished,
            // and the writer must not outlive the child waiting on it
            using var writerCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
            Action onStdinProgress = () => { if (inactivityTimeout is not null && Volatile.Read(ref observeStdin)) timeoutCts.CancelAfter(inactivityTimeout.Value); };
            var stdinTask = stdinLines is null
                ? WriteStdinAsync(process, stdin, onStdinProgress, timeoutCts.Token)
                : WriteStdinLinesAsync(process, stdinLines, onStdinProgress, writerCts.Token,
                    onFault: fault => { producerFault ??= fault; KillTree(process); });

            while (!timedOut && !timeoutCts.IsCancellationRequested)
            {
                T? item;
                try
                {
                    Volatile.Write(ref reading, true);
                    Rearm();                                                                                // arm for this read
                    item = await ReadBoundedAsync(readOne(process), exited, process).ConfigureAwait(false);
                    Volatile.Write(ref reading, false);
                    if (inactivityTimeout is not null) timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan); // stop the clock while the consumer works
                }
                catch (Exception) when (timeoutCts.IsCancellationRequested)
                {
                    timedOut = !ct.IsCancellationRequested;
                    break;
                }
                if (item is null)
                {
                    // EOF right after our own kill still reports as the timeout that caused it
                    if (timeoutCts.IsCancellationRequested) timedOut = !ct.IsCancellationRequested;
                    break;
                }
                yield return item;
            }

            // The stdout loop ended (child closed stdout, or a timeout). Enable writer-progress re-arms,
            // then run the shared observe-stdin/remove tail below.
            Volatile.Write(ref observeStdin, true);
            if (stdinLines is not null) writerCts.Cancel();
            var stderrText = await ObserveStdinAndReapAsync(process, stdinTask, stderr, exited,
                reArm: () => { if (inactivityTimeout is not null) timeoutCts.CancelAfter(inactivityTimeout.Value); },
                killed: () => timeoutCts.IsCancellationRequested).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            if (producerFault is not null) ExceptionDispatchInfo.Capture(producerFault).Throw();
            // a kill that fired during the stdin observe / remove is a TIMEOUT, not a child failure — classify
            // it as one (unless the child actually finished cleanly first: the kill can race a clean exit)
            if (TimedOut(timeoutCts.IsCancellationRequested, process.ExitCode)) timedOut = true;
            if (timedOut)
            {
                // report the window that fired — the ceiling when maxCts tagged the kill, else the inactivity one
                var fired = Volatile.Read(ref hitMaxDuration) ? maxDuration : inactivityTimeout;
                throw new ProcessTimeoutException(command, fired ?? maxDuration ?? TimeSpan.Zero);
            }
            if (process.ExitCode != 0)
                throw new ProcessRunException(command, process.ExitCode, Tail(stderrText));
        }
        finally
        {
            // early enumerator abandonment (consumer breaks out of await foreach) resumes HERE with
            // the child still running: kill it, or it keeps generating with nothing left to remove it.
            // On the normal path the process has already exited and this is a no-op.
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
        }
    }

    /// <summary>Resolve a bare command name via where.exe/which (cached). Multiple hits prefer
    /// .cmd, then .exe, then .ps1 (an extensionless npm shim can't be spawned by CreateProcess). Paths with
    /// separators are returned as-is. A resolved (or directly-supplied) <c>.ps1</c> is hosted in PowerShell
    /// at spawn time — see the private launcher resolution.</summary>
    /// <remarks><b>A FAILED lookup is never cached, and that asymmetry is the whole of this method.</b> The
    /// cache is a process-wide static, so anything it remembers it remembers forever: caching the unresolved
    /// bare name after one transient locator failure would make <see cref="CommandExists"/> report an
    /// installed CLI as absent until the process restarts. Re-looking-up a genuinely missing command costs one
    /// locator spawn per call, which is the right trade.</remarks>
    public static string ResolveCommandPath(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains('/') || command.Contains('\\')) return command;
        if (PathCache.TryGetValue(command, out var cached)) return cached;

        var located = Locate(command);
        if (located is null) return command;

        return PathCache.GetOrAdd(command, located);
    }

    /// <summary>Whether <paramref name="command"/> has a memoized resolution — internal for the test that
    /// pins the negative-caching rule, because "a failure was not remembered" is a statement about the
    /// CACHE and cannot be observed from the returned value alone.</summary>
    internal static bool IsPathCached(string command) => PathCache.ContainsKey(command);

    /// <summary>Whether <paramref name="command"/> looks spawnable RIGHT NOW — the presence check behind a
    /// provider's <c>IsAvailable</c>. A bare name must resolve on PATH; a PATH-shaped command (an app's own
    /// PORTABLE copy of a CLI, not a global install) must exist on disk, or have a spawnable sibling the way
    /// <see cref="Start"/> would resolve it (an extensionless npm-shaped shim next to its <c>.cmd</c>).</summary>
    /// <remarks>Presence only — it never runs the command. A command that exists but is broken still surfaces
    /// as a verdict on the actual call.</remarks>
    public static bool CommandExists(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var resolved = ResolveCommandPath(command);

        // a bare name that PATH lookup couldn't answer comes back unchanged — not found. (Never probed as a
        // relative file: the process's current directory must not answer a PATH lookup.)
        if (Path.GetDirectoryName(resolved) is not { Length: > 0 }) return false;

        return File.Exists(resolved) || (OperatingSystem.IsWindows() && SpawnableSibling(resolved) is not null);
    }

    /// <inheritdoc/>
    bool IProcessRunner.CommandExists(string command) => CommandExists(command);

    /// <summary>The spawnable PATH hit for <paramref name="command"/>. On Windows the name is asked as
    /// <c>$PATH:name</c>, because a bare <c>where.exe</c> search reads the current directory first and a launcher
    /// dropped there would answer. <paramref name="workingDirectory"/> is the locator's own, for the tests.</summary>
    internal static string? Locate(string command, string? workingDirectory = null) =>
        OperatingSystem.IsWindows()
            ? RunLocator("where.exe", "$PATH:" + command, workingDirectory)
            : RunLocator("which", command, workingDirectory);

    /// <summary>Run <paramref name="locator"/> against one name and pick the spawnable hit. Internal for
    /// the tests: the hang bound is only provable against a locator that actually hangs.</summary>
    internal static string? RunLocator(string locator, string command, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(locator)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        psi.ArgumentList.Add(command);
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            // BOTH pipes are drained asynchronously so WaitForExit(5000) bounds the whole call. Reading
            // stdout synchronously put the only bound BEHIND an unbounded read: a locator hung by a dead
            // network drive on PATH (or an AV hook) froze the first CLI-provider call forever, ahead of
            // every inactivity clock RunAsync arms and with no token in this chain to break out. (Still
            // redirected rather than inherited: an inherited handle would print the locator's miss line
            // into the consuming app's own console.)
            var outTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errTask = p.StandardError.ReadToEndAsync(CancellationToken.None);
            var exited = p.WaitForExit(5000);
            if (!exited) KillTree(p);
            // Bounded observe of both drains: after a clean exit they complete at EOF, after the kill the
            // broken pipes complete them — and a grandchild keeping a pipe open must not re-open the hang
            // this bound exists to close. stderr is diagnostic only either way.
            string? output = null;
            try
            {
                if (Task.WaitAll([outTask, errTask], TimeSpan.FromSeconds(2)))
                    output = outTask.GetAwaiter().GetResult();
            }
            catch { /* child gone / pipe broken */ }
            if (!exited || output is null) return null;
            var hits = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hits.Length == 0) return null;
            return hits.FirstOrDefault(h => h.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                ?? hits.FirstOrDefault(h => h.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? hits.FirstOrDefault(h => h.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                ?? hits[0];
        }
        catch
        {
            return null;
        }
    }

    private static System.Diagnostics.Process Start(
        string command, IReadOnlyList<string> args, string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var (exe, prefixArgs) = ResolveLauncher(command, args);
        var psi = new ProcessStartInfo(exe)
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
        foreach (var a in prefixArgs) psi.ArgumentList.Add(a);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (k, v) in environment)
            {
                // a null REMOVES the inherited variable; an empty string would leave it set, and present
                if (v is null) psi.Environment.Remove(k);
                else psi.Environment[k] = v;
            }

        var process = new System.Diagnostics.Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException($"failed to start {command}");
        return process;
    }

    /// <summary>Extensions Windows' own CreateProcess can launch (it hosts <c>.bat</c>/<c>.cmd</c> in the
    /// command interpreter itself). Anything else — an extensionless POSIX launcher script, a <c>.ps1</c>,
    /// a <c>.js</c> — needs an explicit host or a spawnable sibling.</summary>
    private static readonly string[] SpawnableExtensions = [".exe", ".com", ".cmd", ".bat"];

    /// <summary>Sibling launchers to try, best first, when the resolved command isn't spawnable itself.</summary>
    private static readonly string[] SiblingExtensions = [".cmd", ".bat", ".exe", ".com", ".ps1"];

    /// <summary>Resolve a command to the executable + any prefix args needed to launch it. A resolved
    /// <c>.cmd</c>/<c>.exe</c> (the common case) runs directly. Two Windows shim shapes can't be exec'd by
    /// CreateProcess and are mapped onto something that can:
    /// <list type="bullet">
    /// <item>an EXTENSIONLESS (or otherwise un-exec'able) launcher script — the npm/nvm global-install shape,
    /// where a POSIX <c>tool</c> shim sits beside <c>tool.cmd</c>/<c>tool.ps1</c> — is swapped for its
    /// spawnable SIBLING. Without this, a command that resolves to (or is handed in as) the bare shim fails
    /// with "The specified executable is not a valid application for this OS platform" on an install that
    /// otherwise works; only paths with a directory component are probed, so a bare name never picks up a
    /// same-named file from the current directory.</item>
    /// <item>a <c>.ps1</c> (supplied directly, where.exe-resolved, or reached as the sibling above) is hosted
    /// in PowerShell (<c>powershell -NoProfile -ExecutionPolicy Bypass -File &lt;path&gt;</c>).</item>
    /// </list>
    /// CAVEAT — the one scoped exception to the "never a shell" spawn rule: PowerShell RE-PARSES the argv it
    /// forwards to a <c>-File</c> script, so an argument with embedded double quotes or a trailing backslash
    /// can arrive mangled. Prompts already travel via stdin (never argv); keep <c>.ps1</c>-shim argv to simple
    /// flags, or supply a BYO <see cref="IProcessRunner"/> for a CLI that needs exotic args through a .ps1 shim.
    /// A resolved <c>.cmd</c>/<c>.bat</c> is the other exception: <c>cmd.exe</c> re-parses its command line, so an
    /// argument holding <c>&amp; | &lt; &gt; ^ %</c> or a line break is REFUSED (<see cref="ArgumentException"/>)
    /// rather than run as a second command; <c>"</c> is not on the list, since the argument quoting already
    /// carries it. Stream encoding is forced to BOM-less UTF-8 by <see cref="Start"/> regardless.</summary>
    private static (string Exe, IReadOnlyList<string> PrefixArgs) ResolveLauncher(string command, IReadOnlyList<string> args)
    {
        var resolved = ResolveCommandPath(command);
        if (!OperatingSystem.IsWindows()) return (resolved, []);

        if (!IsSpawnable(resolved)) resolved = SpawnableSibling(resolved) ?? resolved;
        if (resolved.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return ("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", resolved]);
        if (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            foreach (var arg in args)
                if (arg.AsSpan().IndexOfAny(CmdMetacharacters) >= 0)
                    throw new ArgumentException(
                        $"an argument for {Path.GetFileName(resolved)} holds a character cmd.exe would re-parse (& | < > ^ % or a line break); " +
                        "a .cmd or .bat launcher is refused it rather than run a second command", nameof(args));
        return (resolved, []);
    }

    private static readonly System.Buffers.SearchValues<char> CmdMetacharacters = System.Buffers.SearchValues.Create("&|<>^%\r\n");

    private static bool IsSpawnable(string path) =>
        SpawnableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The same launcher with a Windows-runnable extension, if one is sitting next to it. Only
    /// a path with a directory component is probed — a bare name that PATH lookup couldn't resolve must
    /// NOT be answered by a same-named file in the process's current directory.</summary>
    private static string? SpawnableSibling(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 }) return null;
        var stem = Path.GetExtension(path).Length == 0 ? path : Path.ChangeExtension(path, null);
        foreach (var extension in SiblingExtensions)
        {
            var candidate = stem + extension;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task WriteStdinAsync(System.Diagnostics.Process process, string? stdin,
        Action? onProgress, CancellationToken ct)
    {
        try
        {
            if (stdin is not null)
            {
                // Write in small slices and report progress after each: on a payload past the OS pipe
                // buffer, a slice write only completes as the child DRAINS ahead of it — so progress ≈ the
                // child actively consuming stdin, and the caller re-arms its inactivity clock on it. A child
                // that stops draining (true silence) stops the progress and is killed by the clock, which
                // breaks the pipe and unblocks this writer. The slice is deliberately near the OS pipe
                // buffer's granularity (Windows child-stdin pipes are ~4 KB): a large slice would complete
                // only once per MANY drained buffers, making a slowly-but-actively draining child look
                // silent for whole windows.
                const int Slice = 8 * 1024;
                for (var offset = 0; offset < stdin.Length; offset += Slice)
                {
                    await process.StandardInput.WriteAsync(
                        stdin.AsMemory(offset, Math.Min(Slice, stdin.Length - offset)), ct).ConfigureAwait(false);
                    onProgress?.Invoke();
                }
            }
            process.StandardInput.Close(); // always signal EOF — CLIs that read stdin would hang otherwise
        }
        catch (IOException)
        {
            // broken pipe: the child exited (or was killed) before draining stdin — the exit-code /
            // stderr path reports the real story; this is not a spawn failure
            try { process.StandardInput.Close(); } catch (IOException) { }
        }
    }

    private static async Task WriteStdinLinesAsync(System.Diagnostics.Process process, IAsyncEnumerable<string> lines,
        Action? onProgress, CancellationToken ct, Action<Exception> onFault)
    {
        try
        {
            await foreach (var line in lines.WithCancellation(ct).ConfigureAwait(false))
            {
                await process.StandardInput.WriteAsync((line + "\n").AsMemory(), ct).ConfigureAwait(false);
                onProgress?.Invoke();
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // a broken pipe is a child that exited, and a cancellation is the clock, the caller or the child's
            // own end: each is reported by the exit code or the timeout, never by the writer
        }
        catch (Exception ex)
        {
            onFault(ex); // the producer's own fault: the caller ends the child and reports THIS
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (IOException) { }
        }
    }

    /// <summary>How long a pipe may stay silent once the child that owned it has exited before a grandchild
    /// still holding it open is given up on.</summary>
    private static readonly TimeSpan DrainBound = TimeSpan.FromSeconds(2);

    /// <summary>Wait for a pipe's reader to finish. True when it reached EOF; false when the child had exited and
    /// the pipe then stayed silent for <see cref="DrainBound"/> — a grandchild is holding it, and only a kill
    /// ends that read. A pipe still producing after the exit is drained for as long as it does.</summary>
    private static async Task<bool> DrainedAsync(Task pump, Task exited, PipeCapture capture)
    {
        await Task.WhenAny(pump, exited).ConfigureAwait(false);
        while (!pump.IsCompleted)
        {
            var seen = capture.Received;
            await Task.WhenAny(pump, Task.Delay(DrainBound)).ConfigureAwait(false);
            if (!pump.IsCompleted && capture.Received == seen) return false;
        }
        return true;
    }

    /// <summary>One read of the streamed core, bounded the same way: a read still pending
    /// <see cref="DrainBound"/> after the child exited ends the tree and reads as EOF.</summary>
    private static async Task<T?> ReadBoundedAsync<T>(ValueTask<T?> started, Task exited,
        System.Diagnostics.Process process) where T : class
    {
        var read = started.AsTask();
        if (!read.IsCompleted)
        {
            await Task.WhenAny(read, exited).ConfigureAwait(false);
            if (!read.IsCompleted && await Task.WhenAny(read, Task.Delay(DrainBound)).ConfigureAwait(false) != read)
            {
                _ = read.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                KillTree(process);
                return null;
            }
        }
        return await read.ConfigureAwait(false);
    }

    /// <summary>A pipe's text, read by a pump that outlives a caller giving up on it. Optionally keeps only the
    /// last <c>tailMax</c> characters — a rolling window, so the capture is bounded however much the child writes.</summary>
    private sealed class PipeCapture(int? tailMax = null)
    {
        private readonly StringBuilder _text = new();
        private long _received;

        public long Received => Interlocked.Read(ref _received);

        public Task Completion { get; private set; } = Task.CompletedTask;

        /// <summary>Read <paramref name="reader"/> to EOF. A broken pipe is EOF, and so is any fault once
        /// <paramref name="killed"/> says the child was killed; <paramref name="onRead"/> runs before each read.</summary>
        public Task Start(StreamReader reader, Action? onRead = null, Func<bool>? killed = null) =>
            Completion = PumpAsync(reader, onRead, killed);

        private async Task PumpAsync(StreamReader reader, Action? onRead, Func<bool>? killed)
        {
            var buffer = new char[8192];
            while (true)
            {
                onRead?.Invoke();
                int n;
                try { n = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false); }
                catch (IOException) { break; }
                catch (Exception) when (killed?.Invoke() == true) { break; }
                if (n == 0) break;
                lock (_text)
                {
                    _text.Append(buffer, 0, n);
                    if (tailMax is { } max && _text.Length > max) _text.Remove(0, _text.Length - max);
                }
                Interlocked.Add(ref _received, n);
            }
        }

        public override string ToString()
        {
            lock (_text) return _text.ToString();
        }
    }

    /// <summary>The shared post-read-loop tail of BOTH run shapes (the I2 fresh-window discipline, kept in
    /// one place so the two paths can't drift on it): give the stdin observe a FRESH inactivity window (a
    /// child that closed stdout but never drains its stdin pipe would otherwise block the writer — and us —
    /// forever, while a child actively DRAINING keeps re-arming via the writer's per-slice progress and
    /// lives; the armed clock's kill breaks the pipes either way), observe the concurrent stdin write so it
    /// is never left unobserved (a broken pipe from an early child exit is already swallowed in
    /// <see cref="WriteStdinAsync"/>), give the final remove a fresh window too (a child that drained
    /// everything but lingers), then remove and return the drained stderr. <paramref name="reArm"/> re-arms
    /// the caller's OWN inactivity clock — the two run shapes deliberately keep their distinct clock
    /// topologies (buffered dual-clock with tagged reasons vs streamed single-clock) — and
    /// <paramref name="killed"/> reports the caller's kill state so a fired clock is never re-armed.</summary>
    private static async Task<string> ObserveStdinAndReapAsync(System.Diagnostics.Process process,
        Task stdinTask, PipeCapture stderr, Task exited, Action reArm, Func<bool> killed)
    {
        if (!killed()) reArm();
        try { await stdinTask.ConfigureAwait(false); } catch { /* reflected in exit code / timeout */ }

        if (!killed()) reArm();
        await exited.ConfigureAwait(false);
        if (!await DrainedAsync(stderr.Completion, exited, stderr).ConfigureAwait(false)) KillTree(process);
        return stderr.ToString();
    }

    private static void KillTree(System.Diagnostics.Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    private const int StdErrTailChars = 500;

    // its one caller passes the capture bounded to StdErrTailChars
    private static string Tail(string text) => text.Trim();
}
