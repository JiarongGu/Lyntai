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

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr,
    ProcessTimeoutKind TimeoutKind = ProcessTimeoutKind.None)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    /// <summary>Whether a clock killed the run — COMPUTED from <see cref="TimeoutKind"/> (the two can't
    /// contradict; which clock fired is the kind).</summary>
    public bool TimedOut => TimeoutKind != ProcessTimeoutKind.None;
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
        if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value);
        // each drained stdin slice re-arms the clock: a child actively CONSUMING a large prompt is alive
        var stdinTask = WriteStdinAsync(process, stdin,
            onProgress: () => { if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); }, killCts.Token);

        try
        {
            var buffer = new char[8192];
            while (true)
            {
                if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); // re-arm for THIS read
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
            var stderr = await ObserveStdinAndReapAsync(process, stdinTask, stderrTask,
                reArm: () => { if (inactivityTimeout is not null) idleCts.CancelAfter(inactivityTimeout.Value); },
                killed: () => killCts.IsCancellationRequested).ConfigureAwait(false);

            if (killCts.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested(); // caller cancel propagates; only a timeout is reported as a result

                // …but a kill that FIRED is not the same as a kill that BEAT the child. Through 2.5.x this
                // branch reported a timeout on the cancellation flag alone, so a child that exited 0 as the
                // timer fired had its complete, successful stdout thrown away and reported as a stall — and
                // CliProviderEngine.CompleteAsync branches on TimedOut before it parses stdout, so an
                // already-billed turn became a Timeout verdict and the router paid for a second one.
                // StreamLinesAsync has always asked the fuller question; the two now ask it through ONE
                // function rather than through two copies kept in step by review.
                if (TimedOut(true, process.ExitCode))
                {
                    // killCts fired without a tagged reason ⇒ the caller's own token (handled above) or a race; default to inactivity
                    var kind = stopReason == (int)ProcessTimeoutKind.None ? ProcessTimeoutKind.Inactivity : (ProcessTimeoutKind)stopReason;
                    return new ProcessResult(-1, stdout.ToString(), stderr, kind);
                }
            }
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr);
        }
        finally
        {
            // any abnormal exit (a non-pipe read fault) must not leave the child running
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
        }
    }

    /// <summary>Did the kill BEAT the child, or did the child finish first? A kill request on its own does
    /// not mean a timeout: <c>KillTree</c> is a no-op against a process that has already gone, so a child
    /// exiting <c>0</c> as the clock fires leaves the flag set and the run entirely successful.
    ///
    /// <para><b>One function because it was two copies, and they had already diverged.</b>
    /// <c>StreamLinesAsync</c> asked both halves of this question; <c>RunAsync</c> asked only whether the
    /// flag was set, and therefore reported a stall for a run whose complete stdout it was holding. The two
    /// call sites are far apart in this file and neither is reachable from the other's tests, which is how a
    /// guard existed on one path and was absent from the other for a whole release. Keeping the decision in
    /// one place is what makes the divergence unrepresentable rather than merely fixed.</para>
    ///
    /// <para><b>Internal, and unit-tested through the truth table</b>, because the race it settles cannot be
    /// driven deterministically from outside this class — which is precisely why the copy that DID carry the
    /// guard never had a test.</para></summary>
    /// <param name="killRequested">Whether a stop was requested (an inactivity or max-duration clock).</param>
    /// <param name="exitCode">The reaped child's exit code.</param>
    internal static bool TimedOut(bool killRequested, int exitCode) => killRequested && exitCode != 0;

    /// <summary>Streamed run: yields stdout lines as they arrive. <paramref name="inactivityTimeout"/> is
    /// an INACTIVITY window on the child (the stdin write and each stdout read) — it deliberately does NOT
    /// count time the consumer spends between chunks, so a slow reader never gets a healthy stream killed
    /// under it. <paramref name="maxDuration"/> is the absolute ceiling (mirrors <see cref="RunAsync"/>'s
    /// backstop — a chatty child that never finishes is killed too). Abandoning the enumerator early
    /// (breaking out of <c>await foreach</c>) kills the child process tree. Throws
    /// <see cref="ProcessTimeoutException"/> when either clock fires, <see cref="ProcessRunException"/>
    /// (with the stderr tail) on nonzero exit — both after the lines produced so far were yielded.</summary>
    public async IAsyncEnumerable<string> StreamLinesAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? inactivityTimeout = null,
        TimeSpan? maxDuration = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var process = Start(command, args, workingDirectory, environment);

        // only the 500-char TAIL is ever reported (ProcessRunException) — drain stderr bounded, so a
        // child spewing MBs of diagnostics while streaming can't grow the parent's memory with it.
        // (RunAsync's full-stderr buffer is contract — its ProcessResult returns the whole stream.)
        var stderrTask = ReadTailAsync(process.StandardError);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // killing the tree unblocks a pending read/write with EOF/IOException — no cancellable read needed
        using var killOnCancel = timeoutCts.Token.Register(() => KillTree(process));
        // the absolute ceiling (RunAsync's backstop, mirrored): armed ONCE; firing feeds the same kill
        // path as the inactivity clock, so a chatty child that never finishes is bounded too. It tags itself
        // on the way through (RunAsync tags both clocks into stopReason; only this one is taggable here), so
        // ProcessTimeoutException can name the window that actually fired instead of always naming inactivity.
        using var maxCts = new CancellationTokenSource();
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
            if (inactivityTimeout is not null) timeoutCts.CancelAfter(inactivityTimeout.Value); // arm for the first read
            var stdinTask = WriteStdinAsync(process, stdin,
                onProgress: () => { if (inactivityTimeout is not null && Volatile.Read(ref observeStdin)) timeoutCts.CancelAfter(inactivityTimeout.Value); },
                timeoutCts.Token);

            while (!timedOut && !timeoutCts.IsCancellationRequested)
            {
                string? line;
                try
                {
                    if (inactivityTimeout is not null) timeoutCts.CancelAfter(inactivityTimeout.Value);            // arm for this read
                    line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
                    if (inactivityTimeout is not null) timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan); // stop the clock while the consumer works
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

            // The stdout loop ended (child closed stdout, or a timeout). Enable writer-progress re-arms,
            // then run the shared observe-stdin/reap tail below.
            Volatile.Write(ref observeStdin, true);
            var stderr = await ObserveStdinAndReapAsync(process, stdinTask, stderrTask,
                reArm: () => { if (inactivityTimeout is not null) timeoutCts.CancelAfter(inactivityTimeout.Value); },
                killed: () => timeoutCts.IsCancellationRequested).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            // a kill that fired during the stdin observe / reap is a TIMEOUT, not a child failure — classify
            // it as one (unless the child actually finished cleanly first: the kill can race a clean exit)
            if (TimedOut(timeoutCts.IsCancellationRequested, process.ExitCode)) timedOut = true;
            if (timedOut)
            {
                // report the window that fired — the ceiling when maxCts tagged the kill, else the inactivity one
                var fired = Volatile.Read(ref hitMaxDuration) ? maxDuration : inactivityTimeout;
                throw new ProcessTimeoutException(command, fired ?? maxDuration ?? TimeSpan.Zero);
            }
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
    /// .cmd, then .exe, then .ps1 (an extensionless npm shim can't be spawned by CreateProcess). Paths with
    /// separators are returned as-is. A resolved (or directly-supplied) <c>.ps1</c> is hosted in PowerShell
    /// at spawn time — see the private launcher resolution.</summary>
    public static string ResolveCommandPath(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains('/') || command.Contains('\\')) return command;
        return PathCache.GetOrAdd(command, static cmd => Locate(cmd) ?? cmd);
    }

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
            // stderr is redirected, so it must be DRAINED — an unread pipe that fills blocks the child, and the
            // only bound here (WaitForExit) sits BEHIND the unbounded stdout read. (Not un-redirected: an
            // inherited handle would print the locator's miss line into the consuming app's own console.)
            var errTask = p.StandardError.ReadToEndAsync(CancellationToken.None);
            var output = p.StandardOutput.ReadToEnd();
            var exited = p.WaitForExit(5000);
            if (!exited) KillTree(p);
            // the tail is diagnostic only; the kill above breaks the pipe, so a fault says nothing worth surfacing
            try { errTask.GetAwaiter().GetResult(); } catch { /* child gone / pipe broken */ }
            if (!exited) return null;
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
        IReadOnlyDictionary<string, string>? environment)
    {
        var (exe, prefixArgs) = ResolveLauncher(command);
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
            foreach (var (k, v) in environment) psi.Environment[k] = v;

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
    /// Stream encoding is forced to BOM-less UTF-8 by <see cref="Start"/> regardless.</summary>
    private static (string Exe, IReadOnlyList<string> PrefixArgs) ResolveLauncher(string command)
    {
        var resolved = ResolveCommandPath(command);
        if (!OperatingSystem.IsWindows()) return (resolved, []);

        if (!IsSpawnable(resolved)) resolved = SpawnableSibling(resolved) ?? resolved;
        if (resolved.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return ("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", resolved]);
        return (resolved, []);
    }

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

    /// <summary>The shared post-read-loop tail of BOTH run shapes (the I2 fresh-window discipline, kept in
    /// one place so the two paths can't drift on it): give the stdin observe a FRESH inactivity window (a
    /// child that closed stdout but never drains its stdin pipe would otherwise block the writer — and us —
    /// forever, while a child actively DRAINING keeps re-arming via the writer's per-slice progress and
    /// lives; the armed clock's kill breaks the pipes either way), observe the concurrent stdin write so it
    /// is never left unobserved (a broken pipe from an early child exit is already swallowed in
    /// <see cref="WriteStdinAsync"/>), give the final reap a fresh window too (a child that drained
    /// everything but lingers), then reap and return the drained stderr. <paramref name="reArm"/> re-arms
    /// the caller's OWN inactivity clock — the two run shapes deliberately keep their distinct clock
    /// topologies (buffered dual-clock with tagged reasons vs streamed single-clock) — and
    /// <paramref name="killed"/> reports the caller's kill state so a fired clock is never re-armed.</summary>
    private static async Task<string> ObserveStdinAndReapAsync(System.Diagnostics.Process process,
        Task stdinTask, Task<string> stderrTask, Action reArm, Func<bool> killed)
    {
        if (!killed()) reArm();
        try { await stdinTask.ConfigureAwait(false); } catch { /* reflected in exit code / timeout */ }

        if (!killed()) reArm();
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return await stderrTask.ConfigureAwait(false);
    }

    private static void KillTree(System.Diagnostics.Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    private const int StdErrTailChars = 500;

    // Its one caller (StreamLinesAsync) passes ReadTailAsync's output, already bounded to StdErrTailChars, so
    // the slice branch never runs today — it stays for any future caller handing in an unbounded string.
    private static string Tail(string text, int max = StdErrTailChars) =>
        text.Length <= max ? text.Trim() : text[^max..].Trim();

    /// <summary>Drain a diagnostic stream to EOF keeping only the last <paramref name="max"/> chars — a
    /// rolling window, so the capture is bounded no matter how much the child writes. Completes when the
    /// child exits (its side of the pipe closes), like <c>ReadToEndAsync</c> did.</summary>
    private static async Task<string> ReadTailAsync(StreamReader reader, int max = StdErrTailChars)
    {
        var buffer = new char[4096];
        var tail = new StringBuilder(max + buffer.Length);
        while (true)
        {
            var n = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            if (n == 0) break; // EOF — child closed stderr
            tail.Append(buffer, 0, n);
            if (tail.Length > max) tail.Remove(0, tail.Length - max); // keep the END of the stream
        }
        return tail.ToString();
    }
}
