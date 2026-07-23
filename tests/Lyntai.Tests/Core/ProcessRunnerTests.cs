using System.Diagnostics;
using Lyntai.Processes;

namespace Lyntai.Tests.Core;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task Captures_stdout()
    {
        var result = await _runner.RunAsync("node", ["-e", "console.log('hello from node')"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("hello from node", result.StdOut);
    }

    [Fact]
    public async Task Timeout_kills_the_process_and_reports_timed_out()
    {
        var sw = Stopwatch.StartNew();
        var result = await _runner.RunAsync("node", ["-e", "setTimeout(() => {}, 60000)"],
            timeout: TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed} — kill didn't work");
    }

    [Fact]
    public async Task Stdin_passes_through_including_utf8_cjk()
    {
        // BOM-less UTF-8 both directions: CJK must round-trip byte-exact through stdin → stdout.
        var result = await _runner.RunAsync("node", ["-e", "process.stdin.pipe(process.stdout)"],
            stdin: "ping 灵台 メモ\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ping 灵台 メモ", result.StdOut);
        Assert.DoesNotContain('\uFEFF', result.StdOut); // no BOM leaked into the stream
    }

    [Fact]
    public async Task Arguments_with_metacharacters_survive_argumentlist()
    {
        // Never a shell: a prompt-like arg with quotes/newlines/metachars must arrive intact.
        var tricky = "a \"quoted\" arg & | > < % $ with\nnewline";
        var result = await _runner.RunAsync("node", ["-e", "console.log(process.argv[1])", tricky]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("a \"quoted\" arg & | > < % $ with", result.StdOut);
    }

    [Fact]
    public async Task Stream_lines_yields_lines_then_throws_on_nonzero_exit()
    {
        var lines = new List<string>();
        var ex = await Assert.ThrowsAsync<ProcessRunException>(async () =>
        {
            await foreach (var line in _runner.StreamLinesAsync("node",
                ["-e", "console.log('one'); console.log('two'); console.error('sad'); process.exit(3)"]))
            {
                lines.Add(line);
            }
        });

        Assert.Equal(["one", "two"], lines);
        Assert.Equal(3, ex.ExitCode);
        Assert.Contains("sad", ex.StdErrTail);
    }

    [Fact]
    public async Task Stdin_write_is_covered_by_the_timeout()
    {
        // a child that never reads stdin + a payload beyond the OS pipe buffer used to block the
        // writer forever (the timeout was armed only AFTER the write)
        var bigStdin = new string('x', 1_000_000);
        var sw = Stopwatch.StartNew();

        var result = await _runner.RunAsync("node", ["-e", "setTimeout(() => {}, 60000)"],
            stdin: bigStdin, timeout: TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed} — stdin write escaped the timeout");
    }

    [Fact]
    public async Task Stream_lines_does_not_deadlock_on_large_stdin_with_interleaved_stdout()
    {
        // Regression: StreamLinesAsync used to await the FULL stdin write (and close stdin) BEFORE
        // the stdout read loop began. On a prompt larger than the OS pipe buffer this deadlocks a
        // child that emits stdout before draining stdin (like `claude --output-format stream-json`,
        // which prints its startup/MCP handshake first): the parent blocks filling the stdin pipe
        // while the child blocks filling the stdout pipe the parent hasn't begun draining. On Windows
        // node's pipe writes are synchronous, so the child's up-front stdout burst blocks its event
        // loop before it reads stdin — the deadlock reproduces deterministically here.
        //
        // Both payloads exceed any pipe buffer (~64 KB max): ~256 KB of stdout up front, then a
        // 512 KB stdin to consume. Fired concurrently, the read loop drains stdout so the child keeps
        // draining stdin and both complete; serialized write-then-read hangs to the timeout.
        const string script = """
            const l = 'y'.repeat(256) + '\n';
            for (let i = 0; i < 1024; i++) process.stdout.write(l);   // ~256 KB of stdout BEFORE reading stdin
            let n = 0;
            process.stdin.on('data', d => { n += d.length; });
            process.stdin.on('end', () => { process.stdout.write('DONE:' + n + '\n'); });
            """;
        var bigStdin = new string('x', 512 * 1024); // 524288 bytes, well past the pipe buffer

        var lines = new List<string>();
        var sw = Stopwatch.StartNew();
        await foreach (var line in _runner.StreamLinesAsync("node", ["-e", script],
            stdin: bigStdin, timeout: TimeSpan.FromSeconds(20)))
        {
            lines.Add(line);
        }
        sw.Stop();

        // the child fully consumed stdin AND the parent fully drained stdout: no deadlock
        Assert.Equal("DONE:524288", lines[^1]);
        Assert.True(lines.Count > 1000, $"only {lines.Count} lines — stdout wasn't fully drained");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed} — write-then-read deadlocked");
    }

    [Fact]
    public async Task Stream_lines_passes_small_stdin_through()
    {
        // Regression guard for the concurrent-stdin change: a small prompt (under the pipe buffer)
        // must still round-trip and the stream must complete cleanly.
        var lines = new List<string>();
        await foreach (var line in _runner.StreamLinesAsync("node",
            ["-e", "let n = 0; process.stdin.on('data', d => n += d.length); process.stdin.on('end', () => console.log('READ:' + n))"],
            stdin: "hello 灵台\n"))
        {
            lines.Add(line);
        }

        Assert.Equal(["READ:13"], lines); // "hello " (6) + 灵台 (2×3 UTF-8 bytes) + "\n" (1) = 13 bytes
    }

    [Fact]
    public async Task Run_async_a_streaming_child_past_the_inactivity_window_completes()
    {
        // The buffered path's timeout is child INACTIVITY, not wall clock: a slow-but-ALIVE turn (a big
        // prompt, a long tool loop) that keeps emitting output must finish, even when its TOTAL runtime
        // exceeds the window — only TRUE SILENCE for the window kills it. Today RunAsync applies a
        // wall-clock timeout and kills this healthy child at ~4s; an inactivity clock lets it run to exit.
        // The 4s window is generous headroom for node's cold start under parallel test load (the window is
        // armed before the child prints); the 1s ticks are well under it, but 6 of them outlast the window.
        const string script = """
            let i = 0;
            console.log('tick' + i++);                                 // first line covers cold start
            const t = setInterval(() => {
              console.log('tick' + i++);
              if (i > 5) { clearInterval(t); process.exit(0); }
            }, 1000);
            """;
        var sw = Stopwatch.StartNew();
        var result = await _runner.RunAsync("node", ["-e", script], timeout: TimeSpan.FromSeconds(4));
        sw.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);                 // never silent for the window → not a timeout
        Assert.Contains("tick5", result.StdOut);       // ran to its clean exit, past the window
    }

    [Fact]
    public async Task Run_async_kills_a_child_gone_silent_and_reports_inactivity()
    {
        // Dead detection: a child that emits once then stalls forever is killed after the inactivity
        // window with no further output — reported as an INACTIVITY timeout, with the pre-stall output
        // preserved. (Distinct from an absolute-max timeout; see the max-cap test.)
        var sw = Stopwatch.StartNew();
        var result = await _runner.RunAsync("node",
            ["-e", "console.log('alive'); setTimeout(() => {}, 60000);"],
            timeout: TimeSpan.FromSeconds(4));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessTimeoutKind.Inactivity, result.TimeoutKind);
        Assert.Contains("alive", result.StdOut);       // output before the stall survives
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed} — kill didn't work");
    }

    [Fact]
    public async Task Run_async_absolute_max_caps_a_chatty_child_that_never_stalls()
    {
        // Backstop: a child that NEVER goes silent (output every 50ms) would run forever under a pure
        // inactivity clock, so the absolute max duration caps it — reported as a MaxDuration timeout,
        // distinct from inactivity. The inactivity window (30s) can't trip here; only the 2s cap can.
        // The child self-exits at ~6s so a broken cap fails the test instead of hanging the run.
        const string script = """
            let i = 0;
            const t = setInterval(() => {
              console.log('spam' + i++);
              if (i > 120) { clearInterval(t); process.exit(0); }
            }, 50);
            """;
        var sw = Stopwatch.StartNew();
        var result = await _runner.RunAsync("node", ["-e", script],
            timeout: TimeSpan.FromSeconds(30), maxDuration: TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessTimeoutKind.MaxDuration, result.TimeoutKind);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed} — the max cap didn't fire early");
    }

    [Fact]
    public async Task Slow_consumer_dwell_does_not_trip_the_stream_timeout()
    {
        // the timeout is child INACTIVITY, not wall clock: a consumer slower than the timeout between
        // chunks must not get a healthy stream killed under it. The 4s budget is generous headroom for
        // node's cold start under parallel test load (the timeout is armed before the child prints);
        // the 4.5s dwell still exceeds it, so a (buggy) wall-clock timeout WOULD fire during the dwell.
        var lines = new List<string>();
        await foreach (var line in _runner.StreamLinesAsync("node",
            ["-e", "console.log('one'); console.log('two'); console.log('three')"],
            timeout: TimeSpan.FromSeconds(4)))
        {
            lines.Add(line);
            if (lines.Count == 2) break;                  // (also exercises early abandonment cleanup)
            await Task.Delay(TimeSpan.FromSeconds(4.5));   // dwell > timeout: only an inactivity clock survives
        }

        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public async Task Abandoning_the_stream_kills_the_child_process()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
        Directory.CreateDirectory(dir);
        var heartbeat = Path.Combine(dir, $"heartbeat-{Guid.NewGuid():N}.txt");
        try
        {
            // child appends a heartbeat every 100ms forever; the enumerator is abandoned after
            // the first line — the child must die with it, not keep generating in the background
            const string script = """
                const fs = require('fs');
                setInterval(() => { try { fs.appendFileSync(process.argv[1], 'x'); } catch {} console.log('beat'); }, 100);
                """;
            await foreach (var _ in _runner.StreamLinesAsync("node", ["-e", script, heartbeat]))
                break; // abandon immediately

            await Task.Delay(700); // room for the kill to land
            var size1 = new FileInfo(heartbeat).Length;
            await Task.Delay(800);
            var size2 = new FileInfo(heartbeat).Length;

            Assert.Equal(size1, size2); // no beats after abandonment → the child is dead
        }
        finally
        {
            try { File.Delete(heartbeat); } catch { }
        }
    }

    [Fact]
    public async Task Working_directory_is_honored()
    {
        var expected = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var result = await _runner.RunAsync("node", ["-e", "console.log(process.cwd())"],
            workingDirectory: Path.GetTempPath());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StdOut.Trim().TrimEnd(Path.DirectorySeparatorChar), ignoreCase: true);
    }

    [Fact]
    public void Resolve_command_path_finds_node_and_caches()
    {
        var resolved = ProcessRunner.ResolveCommandPath("node");
        Assert.NotEqual("node", resolved); // found an absolute path
        Assert.Equal(resolved, ProcessRunner.ResolveCommandPath("node")); // cached, stable
        // paths with separators pass through untouched
        Assert.Equal(@"C:\tools\x.exe", ProcessRunner.ResolveCommandPath(@"C:\tools\x.exe"));
    }
}
