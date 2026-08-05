using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Processes;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The `codex` CLI provider — the SECOND implementer of the shared
/// <see cref="CliProviderEngine"/> seam, and therefore the test that the seam is genuinely generic rather
/// than claude-shaped. Everything here is asserted against surface MEASURED on codex-cli 0.146.0
/// (2026-08-04): <c>--help</c> for the argv, one real successful turn (via the <c>--oss</c> local-model path,
/// so no tokens were spent) and one real failed turn for the JSONL shapes.
///
/// Driven through <see cref="FakeProcessRunner"/> and the codex-shaped stub — never a real binary, and never
/// <c>login</c>/<c>logout</c> against one (they mutate a developer's credentials).</summary>
public class CodexCliProviderTests
{
    public static string StubCommand
    {
        get
        {
            var stub = Path.Combine(TestPaths.DevtoolsDir("scripts"), "codex-stub.mjs");
            return $"node \"{stub}\"";
        }
    }

    private static CodexCliProvider Provider(FakeProcessRunner runner, string command = "codex") =>
        new(runner, new LyntaiOptions(), command: command);

    private static CodexCliProvider StubProvider() =>
        new(new ProcessRunner(), new LyntaiOptions(), command: StubCommand);

    private static LlmRequest Ask(string prompt = "hello", string? model = null) =>
        new() { Messages = [LlmMessage.User(prompt)], Model = model };

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    // ── capabilities: what this backend has, and what it deliberately hasn't ──

    [Fact]
    public void It_claims_exactly_the_capabilities_the_codex_cli_actually_has()
    {
        ILlmProvider provider = Provider(new FakeProcessRunner());

        Assert.IsAssignableFrom<IProviderInstallation>(provider);   // codex --version
        Assert.IsAssignableFrom<IProviderUpdater>(provider);        // codex update
        Assert.IsAssignableFrom<IProviderAuth>(provider);           // codex login status / login / logout

        // NOT pinning: `codex update` takes no version or channel argument, so this backend genuinely cannot
        // install a named version — and must not advertise that it can
        Assert.IsNotAssignableFrom<IProviderVersionInstaller>(provider);
    }

    // ── the completion invocation (measured flags) ────────────────────────────

    [Fact]
    public async Task A_completion_runs_exec_with_json_events_and_the_prompt_on_stdin()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("") };

        await Provider(runner).CompleteAsync(Ask("say ok"));

        Assert.Equal(["exec", "--json", "--skip-git-repo-check", "--color", "never", "--sandbox", "read-only", "-"],
            runner.LastArgs);
        Assert.Equal("say ok", runner.LastStdin);
    }

    [Fact]
    public async Task The_git_repo_check_is_skipped_because_the_engine_spawns_from_a_neutral_directory()
    {
        // measured: codex refuses to run outside a git repository unless told not to, and the engine's cwd is
        // deliberately a neutral temp dir (never the host app's, whose project config would leak into calls).
        // Without this flag every completion would fail on a correctly-configured install.
        var runner = new FakeProcessRunner { RunResult = Ok("") };

        await Provider(runner).CompleteAsync(Ask());

        Assert.Contains("--skip-git-repo-check", runner.LastArgs!);
        Assert.Equal(CliProviderEngine.NeutralWorkingDirectory, runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task A_completion_sandboxes_read_only_by_default()
    {
        // this seam is a TEXT completion: it must not let the agent edit the caller's disk to produce one
        var runner = new FakeProcessRunner { RunResult = Ok("") };

        await Provider(runner).CompleteAsync(Ask());

        var args = runner.LastArgs!.ToList();
        Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
    }

    [Fact]
    public async Task The_sandbox_can_be_raised_deliberately_through_the_dialect()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("") };
        var provider = new CodexCliProvider(runner, new LyntaiOptions(), command: "codex",
            dialect: new CodexCliDialect { SandboxMode = "workspace-write" });

        await provider.CompleteAsync(Ask());

        var args = runner.LastArgs!.ToList();
        Assert.Equal("workspace-write", args[args.IndexOf("--sandbox") + 1]);
    }

    [Fact]
    public async Task A_model_request_is_passed_through()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("") };

        await Provider(runner).CompleteAsync(Ask(model: "gpt-5-codex"));

        var args = runner.LastArgs!.ToList();
        Assert.Equal("gpt-5-codex", args[args.IndexOf("--model") + 1]);
    }

    // ── the measured JSONL shapes ────────────────────────────────────────────

    [Fact]
    public void The_parser_reads_a_measured_successful_turn()
    {
        // verbatim from a real run
        const string thread = """{"type":"thread.started","thread_id":"019fc935-704c-75b2-a660-a85c89a67514"}""";
        const string started = """{"type":"turn.started"}""";
        const string message = """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}""";
        const string completed = """{"type":"turn.completed","usage":{"input_tokens":6489,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":2,"reasoning_output_tokens":0}}""";

        Assert.Equal(CliOutputEventKind.Ignored, CodexJsonlParser.Parse(thread).Kind);
        Assert.Equal(CliOutputEventKind.Ignored, CodexJsonlParser.Parse(started).Kind);

        var content = CodexJsonlParser.Parse(message);
        Assert.Equal(CliOutputEventKind.Content, content.Kind);
        Assert.Equal("ok", content.Text);

        var result = CodexJsonlParser.Parse(completed);
        Assert.Equal(CliOutputEventKind.Result, result.Kind);
        Assert.Equal("", result.Text);                      // turn.completed carries usage, never text
        Assert.Equal(6489, result.Usage?.InputTokens);
        Assert.Equal(2, result.Usage?.OutputTokens);
        Assert.Null(result.Usage?.CostUsd);                 // codex reports no cost — none is invented
    }

    [Fact]
    public void A_turn_failure_is_the_only_terminal_failure_shape()
    {
        const string failed = """{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer"}}""";

        var evt = CodexJsonlParser.Parse(failed);

        Assert.Equal(CliOutputEventKind.Failure, evt.Kind);
        Assert.Contains("401 Unauthorized", evt.Text);
    }

    [Theory]
    // BOTH of these appeared in a run that went on to SUCCEED — treating either as terminal would fail
    // healthy calls that merely retried a websocket or ran a model without metadata
    [InlineData("""{"type":"error","message":"Reconnecting... 2/5 (unexpected status 401 Unauthorized …)"}""")]
    [InlineData("""{"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata not found"}}""")]
    [InlineData("2026-08-03T19:57:43.038887Z ERROR codex_api::endpoint: failed to connect")]  // non-JSON tracing
    [InlineData("")]
    [InlineData("{ not json")]
    public void Non_terminal_noise_is_ignored_not_treated_as_failure(string line)
    {
        Assert.Equal(CliOutputEventKind.Ignored, CodexJsonlParser.Parse(line).Kind);
    }

    [Fact]
    public async Task An_in_band_failure_at_exit_zero_is_still_a_classified_failure()
    {
        // the codex-shaped trap: the process exits 0 and reports the failure as an event
        var runner = new FakeProcessRunner
        {
            RunResult = Ok("""{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer"}}"""),
        };

        var reply = await Provider(runner).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.AuthFailed, reply.Verdict);   // not a bare Failed — the router cools the host
        Assert.Contains("401", reply.Detail);
    }

    [Fact]
    public async Task An_in_band_failure_is_classified_even_when_the_process_ALSO_exits_nonzero()
    {
        // MEASURED 2026-08-05 against an account whose login had EXPIRED (CLI15, filed by a consuming app):
        // one turn prints BOTH error-ish events — which do not share a shape — and then exits non-zero with
        // codex's ordinary startup chatter on stderr. The 401 lives only in the in-band message, so reading
        // the exit code first reports a bare Failed whose detail is "Reading prompt from stdin...": the
        // router advances instead of cooling the host, and the owner is sent to check their PATH rather
        // than their login.
        var runner = new FakeProcessRunner
        {
            RunResult = new ProcessResult(1,
                """
                {"type":"error","message":"Reconnecting... 2/5 (unexpected status 401 Unauthorized)"}
                {"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: expired login"}}
                """,
                "Reading prompt from stdin..."),
        };

        var reply = await Provider(runner).CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.AuthFailed, reply.Verdict);
        Assert.Contains("401 Unauthorized", reply.Detail);
        // ONE failure, not two: the stderr chatter must not be reported underneath the real reason
        Assert.DoesNotContain("Reading prompt from stdin", reply.Detail);
    }

    // ── auth: prose, because this CLI has no machine-readable readout ─────────

    [Fact]
    public async Task Status_asks_the_measured_command_without_stdin()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("Not logged in") };

        await Provider(runner).StatusAsync();

        Assert.Equal(["login", "status"], runner.LastArgs);
        Assert.Null(runner.LastStdin);
    }

    [Fact]
    public async Task Status_reports_the_measured_signed_out_line_as_a_value()
    {
        // measured exactly: `codex login status` prints "Not logged in" and exits 0
        var runner = new FakeProcessRunner { RunResult = Ok("Not logged in") };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Contains("Not logged in", status.Detail);
    }

    [Theory]
    [InlineData("Logged in using ChatGPT account dev@example.invalid", "dev@example.invalid")]
    [InlineData("Signed in as dev@example.invalid", "dev@example.invalid")]
    public void The_auth_text_parser_reads_a_signed_in_line(string line, string expectedAccount)
    {
        var status = CodexAuthStatusText.Parse(line);

        Assert.NotNull(status);
        Assert.True(status.Authenticated);
        Assert.Equal(expectedAccount, status.Account);
    }

    [Theory]
    [InlineData("Not logged in")]
    [InlineData("You are logged out.")]
    public void The_auth_text_parser_reads_a_signed_out_line(string line)
    {
        var status = CodexAuthStatusText.Parse(line);

        Assert.NotNull(status);
        Assert.False(status.Authenticated);   // "Not logged in" must not match on the substring "logged in"
    }

    [Theory]
    [InlineData("")]
    [InlineData("codex 0.146.0")]
    [InlineData("some future wording nobody has seen")]
    public void An_unrecognized_auth_wording_is_UNKNOWN_never_a_guessed_yes(string line)
    {
        // returning null makes the engine report "not authenticated" with the raw text — the safe direction
        Assert.Null(CodexAuthStatusText.Parse(line));
    }

    [Fact]
    public async Task Login_refuses_the_options_this_backend_does_not_have_without_spawning()
    {
        // codex login takes no account-kind/email/SSO flag; forwarding one as `--whatever` would be inventing
        // surface, and its --with-api-key mode reads a SECRET from stdin, which Lyntai never carries
        var runner = new FakeProcessRunner();
        var provider = Provider(runner);

        var withMode = await provider.LoginAsync(new ProviderLoginRequest("console"));
        var withSso = await provider.LoginAsync(new ProviderLoginRequest(Sso: true));
        var withEmail = await provider.LoginAsync(new ProviderLoginRequest(Email: "dev@example.invalid"));

        Assert.False(withMode.Succeeded);
        Assert.False(withSso.Succeeded);
        Assert.False(withEmail.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_plain_login_drives_the_measured_command_and_re_reads_state()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["login", "status"]
                ? Ok("Logged in using ChatGPT account dev@example.invalid")
                : Ok("Successfully logged in"),
        };

        var result = await Provider(runner).LoginAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Status?.Authenticated);
        Assert.Equal("dev@example.invalid", result.Status?.Account);
        Assert.Equal(["login"], runner.Calls[0].Args);
        Assert.Equal(["login", "status"], runner.Calls[1].Args);
    }

    [Fact]
    public async Task Logout_is_a_TOP_LEVEL_command_on_this_cli()
    {
        // measured: `codex logout`, not `auth logout` — the kind of per-CLI difference the dialect exists for
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["login", "status"] ? Ok("Not logged in") : Ok("Logged out"),
        };

        var result = await Provider(runner).LogoutAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Status?.Authenticated);
        Assert.Equal(["logout"], runner.Calls[0].Args);
    }

    // ── probe / update ───────────────────────────────────────────────────────

    [Fact]
    public async Task Probe_reads_the_measured_version_line()
    {
        var runner = new FakeProcessRunner { RunResult = Ok("codex-cli 0.146.0\n") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("0.146.0", probe.Version);
        Assert.Null(probe.Model);                 // no turn-free model readout on this CLI either
        Assert.Equal(["--version"], runner.LastArgs);
    }

    [Fact]
    public async Task Update_drives_the_cli_updater_and_compares_versions()
    {
        var versions = new Queue<string>(["codex-cli 0.145.0", "codex-cli 0.146.0"]);
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["--version"] ? Ok(versions.Dequeue()) : Ok("Updated to 0.146.0"),
        };

        var result = await Provider(runner).UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Updated);
        Assert.Equal("0.145.0", result.FromVersion);
        Assert.Equal("0.146.0", result.ToVersion);
        Assert.Equal(["update"], runner.Calls[1].Args);
    }

    [Fact]
    public async Task Probe_fails_safe_when_the_binary_is_missing()
    {
        var runner = new FakeProcessRunner { RunHandler = (_, _) => throw new InvalidOperationException("no such file") };

        var probe = await Provider(runner).ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("no such file", probe.Detail);
    }

    // ── real spawns, against the codex-shaped stub ───────────────────────────

    [Fact]
    public async Task A_completion_against_the_stub_round_trips_over_a_real_spawn()
    {
        var reply = await StubProvider().CompleteAsync(Ask("hello codex"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("codex stub reply: hello codex", reply.Text);
        Assert.Equal(6489, reply.Usage?.InputTokens);
        Assert.Equal(12, reply.Usage?.CacheReadTokens);
    }

    [Fact]
    public async Task Streaming_against_the_stub_delivers_content_then_final()
    {
        var chunks = new List<LlmChunk>();
        await foreach (var c in StubProvider().StreamAsync(Ask("stream please")))
            chunks.Add(c);

        Assert.Contains(chunks, c => c.Kind == LlmChunkKind.Content);
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Transient_noise_from_the_stub_does_not_fail_a_successful_call()
    {
        var reply = await StubProvider().CompleteAsync(Ask("NOISY please answer"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Contains("codex stub reply", reply.Text);
    }

    [Fact]
    public async Task An_in_band_stub_failure_at_exit_zero_is_classified_over_a_real_spawn()
    {
        var reply = await StubProvider().CompleteAsync(Ask("AUTH_ERROR"));

        Assert.Equal(LlmVerdict.AuthFailed, reply.Verdict);
    }

    [Fact]
    public async Task An_in_band_stub_failure_at_a_NONZERO_exit_is_classified_over_a_real_spawn()
    {
        // the same measured pair as the FakeProcessRunner test above, but through a real spawn — so the
        // ordering is pinned against an actual exit code and an actual stderr, not a hand-built ProcessResult
        var reply = await StubProvider().CompleteAsync(Ask("AUTH_ERROR_EXIT"));

        Assert.Equal(LlmVerdict.AuthFailed, reply.Verdict);
        Assert.Contains("401", reply.Detail);
    }

    [Fact]
    public async Task Probe_and_update_against_the_stub_work_over_a_real_spawn()
    {
        var provider = StubProvider();

        var probe = await provider.ProbeAsync();
        var update = await provider.UpdateAsync();

        Assert.True(probe.Available, $"probe failed: {probe.Detail}");
        Assert.Equal("0.0.0-stub", probe.Version);
        Assert.True(update.Succeeded, $"update failed: {update.Detail}");
        Assert.False(update.Updated);
    }

    [Fact]
    public async Task Status_against_the_stub_reads_the_signed_out_line_over_a_real_spawn()
    {
        var status = await StubProvider().StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Contains("Not logged in", status.Detail);
    }

    // ── DI wiring ────────────────────────────────────────────────────────────

    [Fact]
    public void The_builder_extension_registers_it_as_a_routable_provider()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddCodexCliProvider(command: StubCommand).UseDefaultCandidates("codex-cli"));
        using var sp = services.BuildServiceProvider();

        var providers = sp.GetServices<ILlmProvider>().ToList();

        Assert.Contains(providers, p => p.Id == CodexCliProvider.ProviderId);
    }

    [Fact]
    public void A_portable_install_is_wired_without_touching_the_process_environment()
    {
        // the portable story end-to-end: a path + that install's own home dir, straight from app config
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddCodexCliProvider(
            command: StubCommand,
            environment: new Dictionary<string, string> { ["CODEX_HOME"] = "portable/home" }));
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetServices<ILlmProvider>().Single(p => p.Id == CodexCliProvider.ProviderId);

        Assert.True(provider.IsAvailable);   // resolves `node` (the stub's launcher), not a global codex
    }
}
