using System.Diagnostics;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

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
            inactivityTimeout: TimeSpan.FromSeconds(2));
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
    public async Task Stream_stderr_tail_keeps_the_END_of_a_large_stderr()
    {
        // the streamed path captures stderr as a BOUNDED tail (it only ever reports the last 500 chars) —
        // a child spewing hundreds of KB of stderr must yield the END of it, not the start, and the tail
        // must be correct across chunk boundaries (a ring/trim bug would scramble or truncate it)
        var ex = await Assert.ThrowsAsync<ProcessRunException>(async () =>
        {
            await foreach (var _ in _runner.StreamLinesAsync("node",
                ["-e", "process.stderr.write('BEGIN-' + 'x'.repeat(600000) + '-END-MARKER', () => process.exit(5))"]))
            {
            }
        });

        Assert.Equal(5, ex.ExitCode);
        Assert.True(ex.StdErrTail.Length <= 500, $"tail is {ex.StdErrTail.Length} chars — not bounded");
        Assert.EndsWith("-END-MARKER", ex.StdErrTail);
        Assert.DoesNotContain("BEGIN-", ex.StdErrTail);
    }

    [Fact]
    public async Task Stdin_write_is_covered_by_the_timeout()
    {
        // a child that never reads stdin + a payload beyond the OS pipe buffer used to block the
        // writer forever (the timeout was armed only AFTER the write)
        var bigStdin = new string('x', 1_000_000);
        var sw = Stopwatch.StartNew();

        var result = await _runner.RunAsync("node", ["-e", "setTimeout(() => {}, 60000)"],
            stdin: bigStdin, inactivityTimeout: TimeSpan.FromSeconds(2));
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
            stdin: bigStdin, inactivityTimeout: TimeSpan.FromSeconds(20)))
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
        var result = await _runner.RunAsync("node", ["-e", script], inactivityTimeout: TimeSpan.FromSeconds(4));
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
            inactivityTimeout: TimeSpan.FromSeconds(4));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessTimeoutKind.Inactivity, result.TimeoutKind);
        Assert.Contains("alive", result.StdOut);       // output before the stall survives
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed} — kill didn't work");
    }

    [Fact] // R3: a child actively DRAINING a large stdin is ALIVE — each drained slice re-arms the clock
    public async Task Child_actively_draining_stdin_past_the_window_is_not_killed()
    {
        // stdout closes instantly; the child then SIPS stdin (take one pipe-buffer's worth, nap 150ms,
        // repeat — ~4 KB per sip on Windows) so the TOTAL drain time (~11s) far exceeds the inactivity
        // window, but it never goes SILENT for the window: each sip frees pipe space, the parent's next
        // slice write completes, and that progress re-arms the clock. Pre-fix, the single fixed post-EOF
        // window killed this healthy child mid-drain.
        //
        // THE WINDOW IS 5s, NOT 2s, AND THAT IS A FLAKE FIX (2026-08-16). At 2s the margin over a 150ms sip
        // was 13x, which a loaded machine eats: this failed once inside a full ~3,000-test run and passed
        // 3/3 in isolation, because a stalled Node process going quiet for 2s is indistinguishable from a
        // wedged one. The property under test is unchanged — total drain still far exceeds the window — and
        // the margin is now 33x. Widening costs no coverage: the OPPOSITE direction, that the post-EOF
        // observe is bounded and cannot hang forever, is pinned by its own test below.
        // A timing test whose failure mode is "the product looks broken" has to be robust to load, or it
        // teaches the next reader to re-run the suite instead of reading the failure.
        const string script = """
            process.stdout.end();
            process.stdin.on('end', () => process.exit(0));
            process.stdin.on('data', () => {
              process.stdin.pause();
              setTimeout(() => process.stdin.resume(), 150);
            });
            """;
        var result = await _runner.RunAsync("node", ["-e", script],
            stdin: new string('x', 300_000), inactivityTimeout: TimeSpan.FromSeconds(5))
            .WaitAsync(TimeSpan.FromSeconds(60)); // guard: fail loud instead of hanging the suite

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut); // drain progress counted as activity — the healthy child finished
    }

    [Fact] // I2: the stdin observe after stdout EOF is bounded by the inactivity clock (no unbounded hang)
    public async Task Child_that_closes_stdout_but_never_drains_stdin_is_killed_by_the_inactivity_clock()
    {
        // stdout ends immediately (EOF for the read loop), stdin is never read, and the child lingers —
        // the writer stays blocked on the full stdin pipe, so only a clock ARMED over the stdin observe
        // can end the call (the pre-fix code stopped the clock there and hung without a maxDuration).
        var sw = Stopwatch.StartNew();
        var result = await _runner.RunAsync("node",
            ["-e", "process.stdout.end(); setTimeout(() => {}, 60000)"],
            stdin: new string('x', 1_000_000), inactivityTimeout: TimeSpan.FromSeconds(2))
            .WaitAsync(TimeSpan.FromSeconds(30)); // red-state guard: fail loud instead of hanging the suite
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessTimeoutKind.Inactivity, result.TimeoutKind);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed} — the stdin observe escaped the clock");
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
            inactivityTimeout: TimeSpan.FromSeconds(30), maxDuration: TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessTimeoutKind.MaxDuration, result.TimeoutKind);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed} — the max cap didn't fire early");
    }

    [Fact]
    public async Task Stream_lines_absolute_max_caps_a_chatty_child_that_never_stalls()
    {
        // Mirror of the buffered max-cap test on the STREAMED path: a child that prints every 50ms never
        // trips the 30s inactivity window, so only the 2s absolute maxDuration can end it — surfacing as
        // ProcessTimeoutException (a timeout, not a ProcessRunException child failure), AFTER the lines
        // produced so far were yielded. The child self-exits at ~6s so a broken cap fails instead of hanging.
        const string script = """
            let i = 0;
            const t = setInterval(() => {
              console.log('spam' + i++);
              if (i > 120) { clearInterval(t); process.exit(0); }
            }, 50);
            """;
        var lines = new List<string>();
        var sw = Stopwatch.StartNew();
        var timeout = await Assert.ThrowsAsync<ProcessTimeoutException>(async () =>
        {
            await foreach (var line in _runner.StreamLinesAsync("node", ["-e", script],
                inactivityTimeout: TimeSpan.FromSeconds(30), maxDuration: TimeSpan.FromSeconds(2)))
            {
                lines.Add(line);
            }
        });
        sw.Stop();

        Assert.NotEmpty(lines); // the healthy output before the cap was still yielded
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed} — the max cap didn't fire early");
        // and the message names the window that ACTUALLY fired: the 2s ceiling, not the 30s inactivity
        // clock that never tripped — a message naming the wrong clock sends the reader to the wrong knob
        Assert.Contains("00:00:02", timeout.Message);
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
            inactivityTimeout: TimeSpan.FromSeconds(4)))
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
        var heartbeat = Path.Combine(TestPaths.TestDbsDir, $"heartbeat-{Guid.NewGuid():N}.txt");
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

            // Bounded poll, not a fixed sleep (which flaked under CI load): the child beats every 100ms,
            // so the kill has landed once the heartbeat file holds the same size across two consecutive
            // 300ms windows (≥5 missed beats). Fails hard if it's still beating at the deadline.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var stableWindows = 0;
            long last = -1;
            while (stableWindows < 2)
            {
                Assert.True(DateTime.UtcNow < deadline, "child still beating 15s after abandonment — the kill didn't land");
                await Task.Delay(300);
                var f = new FileInfo(heartbeat);
                var size = f.Exists ? f.Length : 0;
                stableWindows = size == last ? stableWindows + 1 : 0;
                last = size;
            }
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
    public async Task Runs_a_powershell_ps1_launcher_shim()
    {
        // PR1: a .ps1 launcher shim (some Windows CLIs ship one) can't be exec'd directly by CreateProcess —
        // the runner must host it in PowerShell rather than fail with a Win32Exception. ASCII output only:
        // the UTF-8-no-BOM round-trip is locked separately by Stdin_passes_through_including_utf8_cjk (a real
        // shim's child .exe writes its own bytes through the inherited pipe; PS 5.1 doesn't re-encode them).
        if (!OperatingSystem.IsWindows()) return; // .ps1 hosting via powershell.exe is a Windows concern

        var ps1 = Path.Combine(TestPaths.TestScratchDir, $"shim-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(ps1, "param($arg) Write-Output \"ps1-shim-ran:$arg\"\n");
        try
        {
            // passing the .ps1 path directly (as a BYO command would); the runner wraps it in powershell.
            var result = await _runner.RunAsync(ps1, ["ok"]);

            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);
            Assert.Contains("ps1-shim-ran:ok", result.StdOut);
        }
        finally
        {
            try { File.Delete(ps1); } catch { }
        }
    }

    [Fact]
    public async Task Runs_an_extensionless_npm_shim_through_its_cmd_sibling()
    {
        // CLI2: an npm/nvm global install drops THREE launchers side by side — an extensionless `tool`
        // (a POSIX sh script, for Git Bash), `tool.cmd`, and `tool.ps1`. CreateProcess can't exec the
        // extensionless one ("The specified executable is not a valid application for this OS platform"),
        // and it's exactly what a caller-supplied path (or a where.exe hit list without the .cmd) can
        // resolve to — so the runner must launch the spawnable SIBLING instead of failing.
        if (!OperatingSystem.IsWindows()) return; // an extensionless shim is executable as-is elsewhere

        var (dir, shim) = await WriteShimAsync("cmdsib",
            (".cmd", "@echo off\r\necho cmd-sibling-ran:%1\r\n"));
        try
        {
            var result = await _runner.RunAsync(shim, ["ok"]);

            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);
            Assert.Contains("cmd-sibling-ran:ok", result.StdOut);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Runs_an_extensionless_shim_through_its_ps1_sibling_when_there_is_no_cmd()
    {
        // Same shape with only a PowerShell sibling present: the shim resolves to the .ps1, which is
        // itself un-exec'able and gets the powershell.exe host (the existing .ps1 launcher path).
        if (!OperatingSystem.IsWindows()) return;

        var (dir, shim) = await WriteShimAsync("ps1sib",
            (".ps1", "param($arg) Write-Output \"ps1-sibling-ran:$arg\"\n"));
        try
        {
            var result = await _runner.RunAsync(shim, ["ok"]);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("ps1-sibling-ran:ok", result.StdOut);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>Write an npm-style launcher trio into a fresh scratch dir: the extensionless POSIX shim
    /// (what CreateProcess chokes on) plus the given Windows sibling(s). Returns the dir + the
    /// extensionless shim path.</summary>
    private static async Task<(string Dir, string Shim)> WriteShimAsync(
        string name, params (string Extension, string Content)[] siblings)
    {
        var dir = Path.Combine(TestPaths.TestScratchDir, $"shim-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "mytool");
        await File.WriteAllTextAsync(shim, "#!/bin/sh\nexec node \"$0.mjs\" \"$@\"\n"); // a real npm shim: sh, not PE
        foreach (var (extension, content) in siblings)
            await File.WriteAllTextAsync(shim + extension, content);
        return (dir, shim);
    }

    // The kill-versus-clean-exit decision, as a truth table. It was written TWICE — StreamLinesAsync tested
    // `timeoutCts.IsCancellationRequested && process.ExitCode != 0` with the race named in a comment, and
    // RunAsync tested only the cancellation flag — so the buffered path reported a TIMEOUT for a child that
    // had exited 0, discarding the complete stdout it was holding. CliProviderEngine.CompleteAsync branches
    // on TimedOut BEFORE it parses stdout, so that turned an already-billed CLI turn into
    // LlmVerdict.Timeout and made the router pay for a second one.
    //
    // The race itself cannot be driven deterministically from outside the class — which is exactly why the
    // guard that DID exist had no test either. Extracting the decision is what makes it observable: one
    // function, both callers, and the truth table pinned here.
    [Theory]
    [InlineData(true, 1, true)]    // killed mid-flight — a real timeout
    [InlineData(true, 0, false)]   // THE RACE: the kill fired, but the child had already finished cleanly
    [InlineData(false, 1, false)]  // a plain nonzero exit is a child failure, not a timeout
    [InlineData(false, 0, false)]  // the ordinary success path
    public void Timed_out_is_a_kill_that_beat_the_child_not_merely_a_kill_that_fired(
        bool killRequested, int exitCode, bool expected) =>
        Assert.Equal(expected, ProcessRunner.TimedOut(killRequested, exitCode));

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
