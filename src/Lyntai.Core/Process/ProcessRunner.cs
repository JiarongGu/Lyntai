using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lyntai.Processes;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
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
public sealed class ProcessRunner
{
    private static readonly ConcurrentDictionary<string, string> PathCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Buffered run: returns exit code + full stdout/stderr. Timeout kills the process tree
    /// and reports <c>TimedOut=true</c>; caller cancellation kills the tree and rethrows.</summary>
    public async Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string? stdin = null,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default)
    {
        using var process = Start(command, args, workingDirectory, environment);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await WriteStdinAsync(process, stdin).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is not null) timeoutCts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); // caller cancel propagates; only a timeout is reported as a result
            return new ProcessResult(-1, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), TimedOut: false);
    }

    /// <summary>Streamed run: yields stdout lines as they arrive. Throws
    /// <see cref="ProcessTimeoutException"/> on timeout, <see cref="ProcessRunException"/> (with the
    /// stderr tail) on nonzero exit — both after the lines produced so far have been yielded.</summary>
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
        await WriteStdinAsync(process, stdin).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is not null) timeoutCts.CancelAfter(timeout.Value);
        // killing the tree unblocks the pending ReadLineAsync with EOF — no cancellable read needed
        using var killOnCancel = timeoutCts.Token.Register(() => KillTree(process));

        while (true)
        {
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (timeoutCts.IsCancellationRequested)
            {
                break; // killed under us — the epilogue below reports why
            }
            if (line is null) break;
            yield return line;
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        if (timeoutCts.IsCancellationRequested)
            throw new ProcessTimeoutException(command, timeout ?? TimeSpan.Zero);
        if (process.ExitCode != 0)
            throw new ProcessRunException(command, process.ExitCode, Tail(stderr));
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

    private static async Task WriteStdinAsync(System.Diagnostics.Process process, string? stdin)
    {
        if (stdin is not null)
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
        process.StandardInput.Close(); // always signal EOF — CLIs that read stdin would hang otherwise
    }

    private static void KillTree(System.Diagnostics.Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    private static string Tail(string text, int max = 500) =>
        text.Length <= max ? text.Trim() : text[^max..].Trim();
}
