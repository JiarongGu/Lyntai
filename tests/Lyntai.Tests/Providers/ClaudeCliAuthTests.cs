using Lyntai;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The turn-free AUTH seam (<see cref="IProviderAuth"/>): "is this backend signed in, and as
/// whom?" answered WITHOUT running a completion, plus drivable login/logout. Driven through the BYO
/// <see cref="IProcessRunner"/> so no real binary is spawned, no tokens are spent, and — critically — no
/// test ever mutates the developer's real credentials. "Not signed in" is a VALUE here, not an exception.</summary>
public class ClaudeCliAuthTests
{
    private static ClaudeCliProvider Provider(FakeProcessRunner runner, string command = "claude") =>
        new(runner, new LyntaiOptions(), command: command);

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    /// <summary>The real <c>claude auth status --json</c> shape (measured against CLI v2.1.220).</summary>
    private const string SignedInJson = """
        {"loggedIn":true,"authMethod":"claude.ai","apiProvider":"anthropic","email":"dev@example.invalid","orgId":"org-0000","orgName":"Example","subscriptionType":"team"}
        """;

    private const string SignedOutJson = """{"loggedIn":false}""";

    [Fact]
    public void The_auth_capability_is_discoverable_through_the_core_seam()
    {
        // like the probe/update capabilities: a consumer pattern-matches over the registered providers
        // rather than referencing this adapter's type
        ILlmProvider provider = Provider(new FakeProcessRunner());

        Assert.IsAssignableFrom<IProviderAuth>(provider);
    }

    // ── status: the turn-free readout ────────────────────────────────────────

    [Fact]
    public async Task Status_asks_for_machine_readable_state_from_a_neutral_cwd_without_stdin()
    {
        // `--json` is passed EXPLICITLY even though it is the CLI's default: an older build that predates
        // `auth` rejects the unknown FLAG (exit non-zero, no turn), whereas a bare `auth status` would be
        // taken as a PROMPT and quietly spend one. Prefix args from the command override are preserved.
        var runner = new FakeProcessRunner { RunResult = Ok(SignedInJson) };

        await Provider(runner, command: "node \"stub.mjs\"").StatusAsync();

        Assert.Equal("node", runner.LastCommand);
        Assert.Equal(["stub.mjs", "auth", "status", "--json"], runner.LastArgs);
        Assert.Null(runner.LastStdin);
        Assert.Equal(ClaudeCliProvider.NeutralWorkingDirectory, runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task Status_reports_the_signed_in_account()
    {
        var runner = new FakeProcessRunner { RunResult = Ok(SignedInJson) };

        var status = await Provider(runner).StatusAsync();

        Assert.True(status.Authenticated);
        Assert.Equal("claude.ai", status.Method);
        Assert.Equal("dev@example.invalid", status.Account);
    }

    [Fact]
    public async Task Status_reports_signed_out_as_a_value_not_a_throw()
    {
        var runner = new FakeProcessRunner { RunResult = Ok(SignedOutJson) };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Null(status.Account);
    }

    [Fact]
    public async Task Status_trusts_the_reported_state_over_the_exit_code()
    {
        // a signed-out CLI may well exit non-zero while still printing the machine-readable answer the
        // caller asked for — the parsed state wins, so "signed out" doesn't masquerade as "backend broken"
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(1, SignedOutJson, "") };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.DoesNotContain("exit 1", status.Detail);
    }

    [Fact]
    public async Task Status_fails_safe_when_the_binary_is_missing()
    {
        var runner = new FakeProcessRunner { RunHandler = (_, _) => throw new InvalidOperationException("no such file") };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Contains("no such file", status.Detail);
    }

    [Fact]
    public async Task Status_fails_safe_on_an_unrecognized_output_shape()
    {
        // prose instead of JSON (a `--text` build, a banner, an old CLI): report "not authenticated" and
        // hand the caller what the backend actually said — never guess a signed-in state
        var runner = new FakeProcessRunner { RunResult = Ok("Not logged in. Run `claude auth login`.") };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.Contains("Not logged in", status.Detail);
    }

    [Fact]
    public async Task Status_fails_safe_when_the_spawn_stalls()
    {
        var runner = new FakeProcessRunner { RunResult = new ProcessResult(0, "", "", ProcessTimeoutKind.Inactivity) };

        var status = await Provider(runner).StatusAsync();

        Assert.False(status.Authenticated);
        Assert.NotNull(status.Detail);
    }

    [Fact]
    public void Auth_status_parser_reads_the_documented_shape()
    {
        var status = ClaudeAuthStatusJson.Parse(SignedInJson);

        Assert.NotNull(status);
        Assert.True(status.Authenticated);
        Assert.Equal("claude.ai", status.Method);
        Assert.Equal("dev@example.invalid", status.Account);
    }

    [Theory]
    [InlineData("""{"loggedIn":false,"authMethod":null,"email":null}""")]
    [InlineData("""{"logged_in":false}""")]          // a snake_case rename stays readable
    [InlineData("""{"authenticated":false}""")]      // …as does a differently-named flag
    public void Auth_status_parser_reads_a_signed_out_state(string json)
    {
        var status = ClaudeAuthStatusJson.Parse(json);

        Assert.NotNull(status);
        Assert.False(status.Authenticated);
        Assert.Null(status.Account);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"orgName":"Example"}""")]        // no state flag → we don't know, so don't answer
    public void Auth_status_parser_returns_null_for_anything_it_cannot_read(string json)
    {
        Assert.Null(ClaudeAuthStatusJson.Parse(json));
    }

    [Fact]
    public void Auth_status_parser_falls_back_to_the_org_identity_when_no_email_is_reported()
    {
        // a Console/API account need not carry an email; the org is still a legible "as whom"
        var status = ClaudeAuthStatusJson.Parse("""{"loggedIn":true,"apiProvider":"anthropic","orgName":"Example"}""");

        Assert.NotNull(status);
        Assert.Equal("anthropic", status.Method);
        Assert.Equal("Example", status.Account);
    }

    // ── login / logout: drivable, interactive by nature ──────────────────────

    [Theory]
    [InlineData(null, null)]                    // no mode → the backend's own default account kind
    [InlineData("claudeai", "--claudeai")]
    [InlineData("claude.ai", "--claudeai")]
    [InlineData("subscription", "--claudeai")]
    [InlineData("console", "--console")]
    [InlineData("api", "--console")]
    public async Task Login_maps_the_neutral_account_kind_onto_the_cli_flag(string? mode, string? flag)
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..] ? Ok(SignedInJson) : Ok("Logged in"),
        };

        await Provider(runner).LoginAsync(new ProviderLoginRequest(mode));

        string[] expected = flag is null ? ["auth", "login"] : ["auth", "login", flag];
        Assert.Equal(expected, runner.Calls[0].Args);
    }

    [Fact]
    public async Task Login_passes_the_prefilled_email_and_the_sso_request_through()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..] ? Ok(SignedInJson) : Ok("Logged in"),
        };

        await Provider(runner).LoginAsync(new ProviderLoginRequest("console", "dev@example.invalid", Sso: true));

        Assert.Equal(["auth", "login", "--console", "--email", "dev@example.invalid", "--sso"], runner.Calls[0].Args);
    }

    [Fact]
    public async Task Login_refuses_an_account_kind_this_backend_does_not_have()
    {
        // Mode is free-form so another backend's account kinds fit without an enum change — but an
        // unrecognized value must NOT be forwarded as an invented flag
        var runner = new FakeProcessRunner();

        var result = await Provider(runner).LoginAsync(new ProviderLoginRequest("enterprise-sso"));

        Assert.False(result.Succeeded);
        Assert.Contains("enterprise-sso", result.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Login_refuses_a_flag_shaped_email_without_spawning()
    {
        var runner = new FakeProcessRunner();

        var result = await Provider(runner).LoginAsync(new ProviderLoginRequest(Email: "--dangerously-anything"));

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Login_reports_the_state_it_actually_left_the_backend_in()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..] ? Ok(SignedInJson) : Ok("Logged in as dev"),
        };

        var result = await Provider(runner).LoginAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Status?.Authenticated);
        Assert.Equal("dev@example.invalid", result.Status?.Account);
        Assert.Equal(["auth", "login"], runner.Calls[0].Args);
        Assert.Equal(["auth", "status", "--json"], runner.Calls[1].Args);  // login → re-read state
    }

    [Fact]
    public async Task Login_gets_a_clock_that_tolerates_a_silent_browser_wait()
    {
        // a login flow prints a URL and then goes SILENT while a human clicks — so the per-chunk
        // inactivity window a probe uses would kill a perfectly live flow. Both clocks are the same
        // bounded budget here, which is also the documented "it will not hang forever" guarantee.
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..] ? Ok(SignedInJson) : Ok("Logged in"),
        };

        await Provider(runner).LoginAsync();

        var call = runner.Calls[0];
        Assert.Equal(call.MaxDuration, call.InactivityTimeout);
        Assert.True(call.MaxDuration >= TimeSpan.FromMinutes(5), $"login budget too tight: {call.MaxDuration}");
    }

    [Fact]
    public async Task Login_fails_safe_when_the_cli_errors()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..]
                ? Ok(SignedOutJson)
                : new ProcessResult(1, "", "browser flow aborted"),
        };

        var result = await Provider(runner).LoginAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("browser flow aborted", result.Detail);
    }

    [Fact]
    public async Task Logout_runs_the_cli_and_reports_the_resulting_state()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "status", ..] ? Ok(SignedOutJson) : Ok("Logged out"),
        };

        var result = await Provider(runner).LogoutAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Status?.Authenticated);
        Assert.Equal(["auth", "logout"], runner.Calls[0].Args);
        Assert.Equal(["auth", "status", "--json"], runner.Calls[1].Args);
    }

    [Fact]
    public async Task Logout_fails_safe_when_the_cli_errors()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args is ["auth", "logout"]
                ? new ProcessResult(1, "", "keychain locked")
                : Ok(SignedInJson),
        };

        var result = await Provider(runner).LogoutAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("keychain locked", result.Detail);
    }

    // ── the real ProcessRunner, against the deterministic stub ───────────────

    [Fact]
    public async Task Status_against_the_provider_stub_reads_state_over_a_real_spawn()
    {
        var provider = new ClaudeCliProvider(new ProcessRunner(), new LyntaiOptions(),
            command: ClaudeCliProviderTests.StubCommand);

        var status = await provider.StatusAsync();

        Assert.True(status.Authenticated, $"stub auth status failed: {status.Detail}");
        Assert.Equal("provider-stub", status.Method);
    }

    [Fact]
    public async Task Status_works_against_a_windows_npm_shim_install()
    {
        // same CLI2 exposure as the probe/update seams: an npm/nvm `claude` resolves to an EXTENSIONLESS
        // POSIX launcher next to its `.cmd` sibling, which CreateProcess refuses. Every maintenance spawn
        // must go through the runner's shim handling, not just completions.
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.Combine(TestPaths.TestScratchDir, $"claude-auth-shim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "claude");
        var stub = Path.Combine(TestPaths.DevtoolsDir("scripts"), "provider-stub.mjs");
        await File.WriteAllTextAsync(shim, "#!/bin/sh\nexec node \"$0.mjs\" \"$@\"\n");
        await File.WriteAllTextAsync(shim + ".cmd", $"@echo off\r\nnode \"{stub}\" %*\r\n");
        try
        {
            var provider = new ClaudeCliProvider(new ProcessRunner(), new LyntaiOptions(), command: $"\"{shim}\"");

            var status = await provider.StatusAsync();

            Assert.True(status.Authenticated, $"auth status failed: {status.Detail}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
