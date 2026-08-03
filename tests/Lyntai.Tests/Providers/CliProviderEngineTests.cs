using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The GENERIC CLI provider engine — everything a spawned-CLI backend does that isn't specific to
/// one CLI's vocabulary: command resolution, prompt delivery, verdict classification, streaming order, and
/// the four self-maintenance seams. A dialect supplies only the vocabulary, so "add a CLI provider" is one
/// dialect class rather than a re-implementation of these invariants (which is how they drifted before).
///
/// Driven with <see cref="FakeCliDialect"/> so these assertions can't accidentally pass because of
/// something the claude dialect does; the claude-specific behaviour stays covered by
/// <see cref="ClaudeCliProviderTests"/> / <see cref="ClaudeCliProbeTests"/> / <see cref="ClaudeCliAuthTests"/>.</summary>
public class CliProviderEngineTests
{
    private static CliProviderEngine Engine(FakeProcessRunner runner, FakeCliDialect dialect, string? command = "fakecli") =>
        new(dialect, runner, new LyntaiOptions(), command: command);

    private static LlmRequest Ask(string prompt = "hello", string? model = null) =>
        new() { Messages = [LlmMessage.User(prompt)], Model = model };

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    // ── command resolution (pure, so no process env is mutated) ──────────────

    [Fact]
    public void Command_resolution_prefers_the_shared_test_seam_then_the_dialects_own_variable()
    {
        // LYNTAI_PROVIDER_CMD is the shared seam every CLI provider honours (it is what points tests and
        // e2e at the deterministic stub); a dialect's own variable is the per-backend override after it.
        var env = new Dictionary<string, string?> { ["FAKE_CLI_CMD"] = "from-dialect-var" };

        var viaDialectVar = CliCommand.Resolve(null, "fakecli", ["LYNTAI_PROVIDER_CMD", "FAKE_CLI_CMD"], env.GetValueOrDefault);
        env["LYNTAI_PROVIDER_CMD"] = "from-shared-var";
        var viaSharedVar = CliCommand.Resolve(null, "fakecli", ["LYNTAI_PROVIDER_CMD", "FAKE_CLI_CMD"], env.GetValueOrDefault);

        Assert.Equal("from-dialect-var", viaDialectVar.Exe);
        Assert.Equal("from-shared-var", viaSharedVar.Exe);
    }

    [Fact]
    public void Command_resolution_falls_back_to_the_dialects_default_executable()
    {
        var resolved = CliCommand.Resolve(null, "fakecli", ["LYNTAI_PROVIDER_CMD"], _ => null);

        Assert.Equal("fakecli", resolved.Exe);
        Assert.Empty(resolved.PrefixArgs);
    }

    [Fact]
    public void An_explicit_command_wins_over_every_variable_and_keeps_its_prefix_args()
    {
        var resolved = CliCommand.Resolve("node \"C:\\some dir\\stub.mjs\"", "fakecli", ["LYNTAI_PROVIDER_CMD"],
            _ => "ignored-because-explicit");

        Assert.Equal("node", resolved.Exe);
        Assert.Equal(["C:\\some dir\\stub.mjs"], resolved.PrefixArgs);
    }

    // ── prompt delivery: the first thing that differs between CLIs ───────────

    [Fact]
    public async Task A_prompt_travels_over_stdin_by_default()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("result:hi") };

        await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask("say hi"));

        Assert.Equal("say hi", runner.LastStdin);
        Assert.Equal(["run"], runner.LastArgs);
    }

    [Fact]
    public async Task A_prompt_travels_as_a_trailing_ARGUMENT_when_the_dialect_asks_for_that()
    {
        // some CLIs take the prompt positionally rather than on stdin — the engine must not assume either
        var runner = new FakeProcessRunner { RunResult = Ok("result:hi") };
        var dialect = new FakeCliDialect { Delivery = CliPromptDelivery.Argument };

        await Engine(runner, dialect).CompleteAsync(Ask("say hi"));

        Assert.Null(runner.LastStdin);
        Assert.Equal(["run", "say hi"], runner.LastArgs);
    }

    [Fact]
    public async Task Every_spawn_runs_from_the_neutral_working_directory()
    {
        // never the host app's cwd, whose project config a CLI would otherwise load into library calls
        var runner = new FakeProcessRunner { RunResult = Ok("result:hi") };

        await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(CliProviderEngine.NeutralWorkingDirectory, runner.LastWorkingDirectory);
    }

    // ── shared verdict / emptiness invariants ────────────────────────────────

    [Fact]
    public async Task The_dialects_result_text_becomes_the_reply()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("text:partial\nresult:the answer") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("the answer", reply.Text);
        Assert.NotNull(reply.Usage);
    }

    [Fact]
    public async Task Content_without_a_terminal_result_still_answers()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("text:only assistant text") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("only assistant text", reply.Text);
    }

    [Fact]
    public async Task No_output_is_a_FAILURE_not_an_empty_Ok()
    {
        // an empty Ok would let the router treat a dead backend as a successful answer
        var runner = new FakeProcessRunner { RunResult = Ok("") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
    }

    [Fact]
    public async Task A_nonzero_exit_is_classified_by_the_shared_verdict_classifier()
    {
        // no per-provider heuristics: 429 → RateLimited, so the router circuit-breaks instead of retrying
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(1, "", "HTTP 429 rate limit exceeded") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
    }

    [Fact]
    public async Task A_stall_is_a_timeout_verdict()
    {
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(0, "", "", ProcessTimeoutKind.Inactivity) };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Timeout, reply.Verdict);
    }

    [Fact]
    public async Task Streaming_yields_content_then_exactly_one_final()
    {
        var runner = new FakeProcessRunner(["text:one ", "text:two", "result:one two"]);

        var chunks = new List<LlmChunk>();
        await foreach (var c in Engine(runner, new FakeCliDialect()).StreamAsync(Ask()))
            chunks.Add(c);

        Assert.Equal(["one ", "two"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.Single(chunks, c => c.Kind == LlmChunkKind.Final);
    }

    [Fact]
    public async Task A_stream_that_produced_nothing_ends_in_an_error_chunk()
    {
        var runner = new FakeProcessRunner([]);

        var chunks = new List<LlmChunk>();
        await foreach (var c in Engine(runner, new FakeCliDialect()).StreamAsync(Ask()))
            chunks.Add(c);

        Assert.Equal(LlmChunkKind.Error, Assert.Single(chunks).Kind);
    }

    // ── in-band failure: a CLI that reports its own turn failure ─────────────

    [Fact]
    public async Task A_turn_failure_reported_IN_BAND_is_classified_not_swallowed()
    {
        // some CLIs exit 0 and report the failure as an EVENT (measured: codex prints
        // {"type":"turn.failed","error":{"message":"… 401 Unauthorized …"}} and still exits 0). Without a
        // vocabulary for that, the engine would see "no content" and report a bare Failed — losing both the
        // reason and the verdict the router needs (AuthFailed cools the host; Failed just advances).
        var runner = new FakeProcessRunner { RunResult = Ok("fail:unexpected status 401 Unauthorized: missing bearer token") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.AuthFailed, reply.Verdict);
        Assert.Contains("401 Unauthorized", reply.Detail);
    }

    [Fact]
    public async Task A_reported_failure_wins_over_partial_content()
    {
        // half an answer plus "the turn failed" is not an answer — reporting Ok here would be the
        // empty-Ok mistake in a different costume, and the router would never retry
        var runner = new FakeProcessRunner { RunResult = Ok("text:I was about to say\nfail:rate limit exceeded") };

        var reply = await Engine(runner, new FakeCliDialect()).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.RateLimited, reply.Verdict);
    }

    [Fact]
    public async Task A_stream_that_fails_after_content_ends_in_an_error_chunk()
    {
        var runner = new FakeProcessRunner(["text:partial answer", "fail:rate limit exceeded"]);

        var chunks = new List<LlmChunk>();
        await foreach (var c in Engine(runner, new FakeCliDialect()).StreamAsync(Ask()))
            chunks.Add(c);

        Assert.Equal(LlmChunkKind.Content, chunks[0].Kind);   // already delivered — can't be unsent
        Assert.Equal(LlmChunkKind.Error, chunks[^1].Kind);
        Assert.Equal(LlmVerdict.RateLimited, chunks[^1].Verdict);
        Assert.DoesNotContain(chunks, c => c.Kind == LlmChunkKind.Final);
    }

    // ── portable installs: a bundled binary, not a global one ────────────────

    [Fact]
    public void A_portable_binary_is_available_when_the_file_is_actually_there()
    {
        // an app shipping its own CLI copy points the provider at a PATH, not a PATH-installed name
        var dir = Path.Combine(TestPaths.TestScratchDir, $"portable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "mycli.cmd" : "mycli");
        File.WriteAllText(exe, "");
        try
        {
            var engine = new CliProviderEngine(new FakeCliDialect(), new ProcessRunner(), new LyntaiOptions(),
                command: $"\"{exe}\"");

            Assert.True(engine.IsAvailable);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void A_portable_binary_that_is_MISSING_is_not_available()
    {
        // the whole point of IsAvailable: the router skips this candidate instead of discovering the
        // missing portable copy as a failed turn
        var missing = Path.Combine(TestPaths.TestScratchDir, $"absent-{Guid.NewGuid():N}", "mycli.exe");

        var engine = new CliProviderEngine(new FakeCliDialect(), new ProcessRunner(), new LyntaiOptions(),
            command: $"\"{missing}\"");

        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void A_portable_extensionless_shim_is_available_through_its_spawnable_sibling()
    {
        // an npm/nvm-shaped portable layout: `mycli` (POSIX script) next to `mycli.cmd`. CreateProcess
        // can't exec the former, so presence must be judged the way the spawn resolves it (CLI2).
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.Combine(TestPaths.TestScratchDir, $"portable-shim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "mycli");
        File.WriteAllText(shim, "#!/bin/sh\n");
        File.WriteAllText(shim + ".cmd", "@echo off\r\n");
        try
        {
            var engine = new CliProviderEngine(new FakeCliDialect(), new ProcessRunner(), new LyntaiOptions(),
                command: $"\"{shim}\"");

            Assert.True(engine.IsAvailable);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Extra_environment_variables_reach_completion_AND_maintenance_spawns()
    {
        // a self-contained portable install needs its own home/config dir (codex reads CODEX_HOME) — and it
        // must apply to the maintenance spawns too, or a probe/auth check would read the GLOBAL install's state
        var env = new Dictionary<string, string> { ["MYCLI_HOME"] = "/portable/home" };
        var runner = new FakeProcessRunner { RunResult = Ok("result:hi") };
        var engine = new CliProviderEngine(new FakeCliDialect(), runner, new LyntaiOptions(),
            command: "mycli", environment: env);

        await engine.CompleteAsync(Ask());
        Assert.Equal("/portable/home", runner.LastEnvironment?["MYCLI_HOME"]);

        await engine.ProbeAsync();
        Assert.Equal("/portable/home", runner.LastEnvironment?["MYCLI_HOME"]);
    }

    // ── self-maintenance: driven by the dialect, absent when it has none ─────

    [Fact]
    public async Task Probe_asks_with_the_dialects_own_version_args()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("9.9.9 (fake)") };

        var probe = await Engine(runner, new FakeCliDialect()).ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("9.9.9", probe.Version);          // the shared dotted-number parser, for free
        Assert.Equal(["--version"], runner.LastArgs);
        Assert.Null(runner.LastStdin);
    }

    [Fact]
    public async Task A_backend_with_no_version_readout_reports_that_as_a_value_without_spawning()
    {
        var runner = new FakeProcessRunner();
        var dialect = new FakeCliDialect { VersionArgsValue = null };

        var probe = await Engine(runner, dialect).ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("fake-cli", probe.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_backend_with_no_self_updater_reports_that_as_a_value_without_spawning()
    {
        var runner = new FakeProcessRunner();

        var result = await Engine(runner, new FakeCliDialect()).UpdateAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.Updated);
        Assert.Contains("fake-cli", result.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_backend_with_no_auth_readout_reports_that_as_a_value_without_spawning()
    {
        var runner = new FakeProcessRunner();

        var status = await Engine(runner, new FakeCliDialect()).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Contains("fake-cli", status.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_dialect_that_cannot_pin_a_version_refuses_without_spawning()
    {
        var runner = new FakeProcessRunner();

        var result = await Engine(runner, new FakeCliDialect()).InstallAsync(new ProviderInstallRequest("1.2.3"));

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Auth_status_is_parsed_by_the_dialect_and_wins_over_the_exit_code()
    {
        var dialect = new FakeCliDialect { AuthStatusArgsValue = ["whoami"] };
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(3, "signed-in", "") };

        var status = await Engine(runner, dialect).StatusAsync();

        Assert.True(status.Authenticated);
        Assert.Equal("someone@example.invalid", status.Account);
        Assert.Equal(["whoami"], runner.LastArgs);
    }

    [Fact]
    public async Task Login_and_logout_re_read_the_state_they_left_behind()
    {
        var dialect = new FakeCliDialect { AuthStatusArgsValue = ["whoami"], LoginArgsValue = ["signin"], LogoutArgsValue = ["signout"] };
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["whoami"] ? Ok("signed-in") : Ok("done"),
        };
        var engine = Engine(runner, dialect);

        var login = await engine.LoginAsync();
        var logout = await engine.LogoutAsync();

        Assert.True(login.Succeeded);
        Assert.True(login.Status?.Authenticated);
        Assert.Equal(["signin"], runner.Calls[0].Args);
        Assert.Equal(["whoami"], runner.Calls[1].Args);
        Assert.True(logout.Succeeded);
        Assert.Equal(["signout"], runner.Calls[2].Args);
    }

    [Fact]
    public async Task The_maintenance_and_login_clocks_come_from_the_dialect()
    {
        // a probe is a stall detector, a login waits on a human — a dialect can tune both, and the engine
        // must apply the login budget to BOTH clocks (a browser wait is legitimately silent)
        var dialect = new FakeCliDialect
        {
            MaintenanceClock = TimeSpan.FromSeconds(7),
            LoginClock = TimeSpan.FromMinutes(3),
            AuthStatusArgsValue = ["whoami"],
            LoginArgsValue = ["signin"],
        };
        var runner = new FakeProcessRunner { RunResult = Ok("signed-in") };
        var engine = Engine(runner, dialect);

        await engine.ProbeAsync();
        Assert.Equal(TimeSpan.FromSeconds(7), runner.LastInactivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), runner.LastMaxDuration);

        await engine.LoginAsync();
        var login = runner.Calls[1];
        Assert.Equal(TimeSpan.FromMinutes(3), login.InactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(3), login.MaxDuration);
    }
}
