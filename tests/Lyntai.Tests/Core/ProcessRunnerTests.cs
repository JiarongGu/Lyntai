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
    public void Resolve_command_path_finds_node_and_caches()
    {
        var resolved = ProcessRunner.ResolveCommandPath("node");
        Assert.NotEqual("node", resolved); // found an absolute path
        Assert.Equal(resolved, ProcessRunner.ResolveCommandPath("node")); // cached, stable
        // paths with separators pass through untouched
        Assert.Equal(@"C:\tools\x.exe", ProcessRunner.ResolveCommandPath(@"C:\tools\x.exe"));
    }
}
