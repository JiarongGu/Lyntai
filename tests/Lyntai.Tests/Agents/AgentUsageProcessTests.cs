using System.Diagnostics;
using Lyntai.Agents;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Agents;

/// <summary>The usage read against the shipped <see cref="ProcessRunner"/> and a real <c>node</c> stand-in for the
/// vendor's server: one that ends the moment its stdin closes (as codex's does), one that never answers, one that
/// exits by itself, and one that holds a grandchild. Every path is checked for the same thing — no process the test
/// started is left running — and the test ends ONLY processes it started, by the pids the stand-in wrote.</summary>
public class AgentUsageProcessTests
{
    // Writes "<pid> <grandchild pid>" to argv[3] first, so a test can find what to look for even when the read throws;
    // ends itself after 60 s (the grandchild after 120 s), so a regression orphans nothing for long.
    private const string Server = """
        const { spawn } = require('child_process');
        const fs = require('fs');
        const mode = process.argv[2];
        const grand = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 120000)'], { stdio: 'ignore' });
        fs.writeFileSync(process.argv[3], process.pid + ' ' + grand.pid);
        setTimeout(() => process.exit(0), 60000);
        process.stdin.on('end', () => process.exit(0));   // the server ends the moment its stdin closes
        let buffer = '';
        process.stdin.on('data', chunk => {
          buffer += chunk;
          let i;
          while ((i = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, i); buffer = buffer.slice(i + 1);
            if (mode === 'answer' && line.includes('ask'))
              setTimeout(() => console.log(JSON.stringify({ answer: true, cwd: process.cwd(), home: process.env.STAND_IN_HOME ?? null })), 300);
            if (mode === 'die') { console.log('last words'); process.exit(3); }
          }
        });
        """;

    private sealed class StandInDialect(string pidFile, string mode) : IUsageDialect
    {
        public List<string> Seen { get; } = [];
        public string ProviderId => "stand-in";
        public string HomeVariable => "STAND_IN_HOME";
        public ICliBackend Backend { get; } = new ClaudeCliBackend();
        public IReadOnlyList<string> VersionArgs { get; } = ["--version"];
        public string? ParseVersion(string output) => output;
        public IReadOnlySet<string> ValidatedVersions { get; } = new HashSet<string>();
        public IReadOnlyList<string> ServerArgs { get; } = [mode, pidFile];
        public IReadOnlyList<string> Requests { get; } = ["hello", "ask"];
        public bool IsAnswer(string line) => line.Contains("\"answer\"");

        public AgentAllowanceReading Read(IReadOnlyList<string> lines, DateTimeOffset measuredAt)
        {
            Seen.AddRange(lines);
            return new AgentAllowanceReading
            {
                MeasuredAt = measuredAt,
                Windows = [new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, 1, null, null)],
            };
        }
    }

    private static (CliUsageSource Source, StandInDialect Dialect) Standing(
        ScratchDir scratch, string mode, TimeSpan timeout, out string pidFile)
    {
        pidFile = scratch.Combine("pids.txt");
        var script = scratch.File("server.cjs", Server);
        var dialect = new StandInDialect(pidFile, mode);
        var source = new CliUsageSource(dialect, new ProcessRunner(),
            new AgentUsageReadingOptions { Enabled = true, Timeout = timeout }, logger: null,
            command: $"node \"{script}\"", environment: null, requireValidatedVersion: false);
        return (source, dialect);
    }

    private static AgentAccount Account(string home) => new() { Provider = "stand-in", Name = "work", Home = home };

    private static async Task<List<int>> Pids(string pidFile)
    {
        for (var i = 0; i < 200; i++)
        {
            try
            {
                if (File.Exists(pidFile) && await File.ReadAllTextAsync(pidFile) is { } text && text.Contains(' '))
                    return [.. text.Split(' ').Select(int.Parse)];
            }
            catch (IOException) { /* being written */ }
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("the stand-in never wrote its pids");
    }

    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task AssertNoneAlive(List<int> pids)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (pids.Any(Alive) && DateTime.UtcNow < deadline) await Task.Delay(100);
            Assert.All(pids, pid => Assert.False(Alive(pid), $"process {pid}, started by this test, is still running"));
        }
        finally
        {
            // only the pids this test's stand-in wrote: a regression must not leave them for the next run
            foreach (var pid in pids.Where(Alive))
                try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); } catch { /* gone */ }
        }
    }

    [Fact]
    public async Task A_server_that_ends_on_stdin_close_still_answers_because_stdin_stays_open()
    {
        using var scratch = new ScratchDir("usage-process");
        using var home = new ScratchDir("usage-process-home");
        var (source, dialect) = Standing(scratch, "answer", TimeSpan.FromSeconds(30), out var pidFile);

        var reading = await source.ReadAllowanceAsync(Account(home.Path));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        await AssertNoneAlive(await Pids(pidFile));

        using var answer = System.Text.Json.JsonDocument.Parse(dialect.Seen[^1]);
        Assert.Equal(home.Path, answer.RootElement.GetProperty("home").GetString());
        var cwd = answer.RootElement.GetProperty("cwd").GetString()!;
        Assert.NotEqual(Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetFullPath(cwd));
        Assert.False(Directory.Exists(cwd), "the fresh working directory is removed when the read ends");
    }

    [Fact]
    public async Task A_server_that_never_answers_is_ended_with_its_children_when_the_time_runs_out()
    {
        using var scratch = new ScratchDir("usage-process");
        var (source, _) = Standing(scratch, "silent", TimeSpan.FromSeconds(3), out var pidFile);
        var clock = Stopwatch.StartNew();

        var reading = await source.ReadAllowanceAsync(Account("/homes/work"));

        Assert.Equal(AgentAllowanceUnknown.TimedOut, reading.UnknownReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"took {clock.Elapsed}");
        await AssertNoneAlive(await Pids(pidFile));
    }

    [Fact]
    public async Task Cancellation_rethrows_and_leaves_no_process_behind()
    {
        using var scratch = new ScratchDir("usage-process");
        var (source, _) = Standing(scratch, "silent", TimeSpan.FromSeconds(60), out var pidFile);
        using var cts = new CancellationTokenSource();

        var running = source.ReadAllowanceAsync(Account("/homes/work"), cts.Token);
        var pids = await Pids(pidFile);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await AssertNoneAlive(pids);
    }

    [Fact]
    public async Task A_server_that_exits_by_itself_without_answering_reads_unknown()
    {
        using var scratch = new ScratchDir("usage-process");
        var (source, _) = Standing(scratch, "die", TimeSpan.FromSeconds(30), out var pidFile);

        var reading = await source.ReadAllowanceAsync(Account("/homes/work"));

        Assert.Equal(AgentAllowanceUnknown.NoAnswer, reading.UnknownReason);
        await AssertNoneAlive(await Pids(pidFile));
    }

    [Fact]
    public async Task A_binary_that_does_not_exist_reads_unknown_and_never_throws()
    {
        using var scratch = new ScratchDir("usage-process");
        var dialect = new StandInDialect(scratch.Combine("pids.txt"), "answer");
        var source = new CliUsageSource(dialect, new ProcessRunner(),
            new AgentUsageReadingOptions { Enabled = true }, logger: null,
            command: "lyntai-no-such-binary-" + Guid.NewGuid().ToString("N"), environment: null, requireValidatedVersion: false);

        var reading = await source.ReadAllowanceAsync(Account("/homes/work"));

        Assert.Equal(AgentAllowanceUnknown.SpawnFailed, reading.UnknownReason);
    }
}
