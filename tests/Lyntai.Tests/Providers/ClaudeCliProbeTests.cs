using Lyntai;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The turn-free installation probe (`claude --version`) and the CLI's own self-update seam
/// (`claude update`) — driven through the BYO <see cref="IProcessRunner"/> so no real binary is spawned and
/// no tokens are spent. Both must FAIL SAFE: an absent/stalled/erroring CLI reports "not available" rather
/// than throwing, exactly like <see cref="ClaudeCliProvider.IsAvailable"/>.</summary>
public class ClaudeCliProbeTests
{
    private static ClaudeCliProvider Provider(FakeProcessRunner runner, string command = "claude") =>
        new(runner, new LyntaiOptions(), command: command);

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    [Fact]
    public void The_capabilities_are_discoverable_through_the_core_seams()
    {
        // the version/self-update capability is a provider FAMILY trait (a CLI prints --version, a local
        // runtime knows its weights, a server has a version endpoint) — so a consumer finds it by
        // pattern-matching over the registered providers, not by referencing this adapter's type
        ILlmProvider provider = Provider(new FakeProcessRunner());

        Assert.IsAssignableFrom<IProviderInstallation>(provider);
        Assert.IsAssignableFrom<IProviderUpdater>(provider);
    }

    // ── CLI2: the probe ──────────────────────────────────────────────────────

    [Fact]
    public async Task Probe_reports_the_version_the_cli_prints()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("2.1.220 (Claude Code)\n") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("2.1.220", probe.Version);
        Assert.Equal("2.1.220 (Claude Code)", probe.Detail);
    }

    [Fact]
    public async Task Probe_asks_only_for_the_version_from_a_neutral_cwd_without_stdin()
    {
        // --version is the ONLY safe turn-free question: the CLI treats an unrecognized token as a
        // PROMPT and spends a turn on it, so the probe must never guess a subcommand. Prefix args from
        // the command override (`node "stub.mjs"`) are preserved ahead of the flag.
        var runner = new FakeProcessRunner { RunResult = Ok("2.1.220 (Claude Code)") };

        await Provider(runner, command: "node \"stub.mjs\"").ProbeAsync();

        Assert.Equal("node", runner.LastCommand);
        Assert.Equal(["stub.mjs", "--version"], runner.LastArgs);
        Assert.Null(runner.LastStdin);
        Assert.Equal(ClaudeCliProvider.NeutralWorkingDirectory, runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task Probe_fails_safe_when_the_binary_is_missing()
    {
        var runner = new FakeProcessRunner { RunHandler = (_, _) => throw new InvalidOperationException("no such file") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.False(probe.Available);
        Assert.Null(probe.Version);
        Assert.Contains("no such file", probe.Detail);
    }

    [Fact]
    public async Task Probe_fails_safe_on_a_nonzero_exit()
    {
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(9, "", "claude: command not found") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.False(probe.Available);
        Assert.Null(probe.Version);
        Assert.Contains("exit 9", probe.Detail);
    }

    [Fact]
    public async Task Probe_fails_safe_when_the_spawn_stalls()
    {
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(0, "", "", ProcessTimeoutKind.Inactivity) };

        var probe = await Provider(runner).ProbeAsync();

        Assert.False(probe.Available);
        Assert.Null(probe.Version);
    }

    [Fact]
    public async Task Probe_reports_no_model_when_the_cli_does_not_name_one()
    {
        // today's CLI can't report its resolved model turn-free — the caller reads the model the CLI
        // ACTUALLY used from UsageFinal.Model after a turn; a half-invented answer here would be worse
        var runner = new FakeProcessRunner { RunResult = Ok("2.1.220 (Claude Code)") };

        Assert.Null((await Provider(runner).ProbeAsync()).Model);
    }

    [Fact]
    public async Task Probe_reads_a_labelled_model_when_a_build_reports_one()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("2.4.0 (Claude Code, model: claude-opus-5)") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.Equal("2.4.0", probe.Version);
        Assert.Equal("claude-opus-5", probe.Model);
    }

    [Theory]
    [InlineData("2.1.220 (Claude Code)", "2.1.220")]
    [InlineData("v2.1.220", "2.1.220")]
    [InlineData("2.2.0-beta.1 (Claude Code)", "2.2.0-beta.1")]
    [InlineData("Claude Code", null)]
    [InlineData("", null)]
    public void Version_line_parser_takes_the_dotted_number(string line, string? expected)
    {
        Assert.Equal(expected, ClaudeVersionLine.Parse(line).Version);
    }

    // ── CLI3: the self-update seam ───────────────────────────────────────────

    [Fact]
    public async Task Update_runs_the_cli_updater_and_reports_the_version_change()
    {
        var versions = new Queue<string>(["2.1.0 (Claude Code)", "2.2.0 (Claude Code)"]);
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args[^1] == "--version"
                ? Ok(versions.Dequeue())
                : Ok("Successfully updated to version 2.2.0"),
        };

        var result = await Provider(runner).UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Updated);
        Assert.Equal("2.1.0", result.FromVersion);
        Assert.Equal("2.2.0", result.ToVersion);
        Assert.Contains("Successfully updated", result.Detail);
        Assert.Equal(["update"], runner.Calls[1].Args); // probe → update → re-probe
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task Update_reports_no_change_when_the_cli_is_already_current()
    {
        // the CLI has no check-only mode — "up to date" is the OUTCOME of running its updater
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args[^1] == "--version"
                ? Ok("2.2.0 (Claude Code)")
                : Ok("Claude Code is up to date!"),
        };

        var result = await Provider(runner).UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Updated);
        Assert.Equal("2.2.0", result.FromVersion);
        Assert.Equal("2.2.0", result.ToVersion);
    }

    [Fact]
    public async Task Update_fails_safe_when_the_updater_errors()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args[^1] == "--version"
                ? Ok("2.1.0 (Claude Code)")
                : new ProcessResult(1, "", "network unreachable"),
        };

        var result = await Provider(runner).UpdateAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.Updated);
        Assert.Equal("2.1.0", result.FromVersion);
        Assert.Equal("2.1.0", result.ToVersion); // nothing changed
        Assert.Contains("network unreachable", result.Detail);
        Assert.Equal(2, runner.Calls.Count); // no re-probe after a failed update
    }

    [Fact]
    public async Task Update_fails_safe_when_the_binary_is_missing()
    {
        var runner = new FakeProcessRunner { RunHandler = (_, _) => throw new InvalidOperationException("no such file") };

        var result = await Provider(runner).UpdateAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.Updated);
        Assert.Null(result.FromVersion);
    }

    // ── the real ProcessRunner, against the deterministic stub ───────────────

    [Fact]
    public async Task Probe_against_the_provider_stub_reads_a_version_over_a_real_spawn()
    {
        var provider = new ClaudeCliProvider(new ProcessRunner(), new LyntaiOptions(),
            command: ClaudeCliProviderTests.StubCommand);

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.StartsWith("0.0.0", probe.Version);
    }

    [Fact]
    public async Task Update_against_the_provider_stub_reports_up_to_date_over_a_real_spawn()
    {
        var provider = new ClaudeCliProvider(new ProcessRunner(), new LyntaiOptions(),
            command: ClaudeCliProviderTests.StubCommand);

        var result = await provider.UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Updated);
        Assert.Equal(result.FromVersion, result.ToVersion);
    }
}
