using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>
/// OPT-IN proof against an INSTALLED ACP agent. Runs only when <c>LYNTAI_LIVE_ACP</c> is set AND the agent's binary
/// is on PATH; otherwise it reports as SKIPPED (<c>Xunit.SkippableFact</c>), one named skip per agent.
///
/// <para><b>These tests cannot sign in and cannot spend a token</b>, and that is by construction rather than by
/// care: each runs the agent under an EMPTY scratch configuration home with every credential variable it reads
/// removed from the child, so the turn can only fail at the agent's own login check. A real sign-in, or a turn
/// against a signed-in account, is never started from a test.</para>
///
/// Enable:  set LYNTAI_LIVE_ACP=1   (and install <c>claude-agent-acp</c> and/or <c>codex-acp</c>)
/// </summary>
public class AcpAgentLiveTests
{
    private static bool Live => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_ACP"));

    private static string EmptyHome(string name)
    {
        var home = Path.Combine(TestPaths.TestScratchDir, $"live-acp-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);   // codex-acp exits at startup when its home does not exist
        return home;
    }

    private static async Task<SessionEnded> LoggedOutTurn(string provider, string command, string homeVariable, string[] credentials)
    {
        var home = EmptyHome(provider);
        try
        {
            // the home is set BOTH as a standing variable and as the account, so a door that failed to hand the account
            // over would still not reach the machine's own signed-in home
            var environment = credentials.ToDictionary(name => name, _ => (string?)null);
            environment[homeVariable] = home;
            var session = new AcpAgentSession(new ProcessRunner(), new LyntaiOptions(), command,
                environment: environment, accountHomeVariable: homeVariable);

            var events = await session.StreamAsync(new AgentSessionOptions
            {
                Prompt = "say ok",
                WorkingDirectory = home,
                TimeoutSeconds = 120,
                Account = new AgentAccount { Provider = provider, Name = "scratch", Home = home },
            }).ToListAsync();

            return Assert.IsType<SessionEnded>(events.Last());
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch (IOException) { /* a child still holds a file: scratch is gitignored */ }
        }
    }

    // REAL: measured against codex-acp 2.1.1 with an empty home — session/new answers -32000
    // "Authentication required" with no data, before any session opens.
    [SkippableFact]
    public async Task Real_codex_acp_under_an_empty_home_ends_the_turn_as_an_access_failure_never_sent()
    {
        Skip.IfNot(Live, "LYNTAI_LIVE_ACP not set");
        Skip.IfNot(ProcessRunner.CommandExists("codex-acp"), "no codex-acp on PATH");

        var ended = await LoggedOutTurn("codex-acp", "codex-acp", "CODEX_HOME", ["OPENAI_API_KEY", "CODEX_API_KEY"]);

        Assert.True(ended.IsError);
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.Equal(AgentFailureKind.Access, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32000", ended.Failure.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
    }

    // NOT yet measured: where a logged-out claude-agent-acp refuses (session/new, like codex-acp, or the prompt) and
    // with which fields. This records only what must hold wherever it does: the turn ends, as a failure the door read.
    [SkippableFact]
    public async Task Real_claude_agent_acp_under_an_empty_home_ends_the_turn_as_a_failure_the_door_read()
    {
        Skip.IfNot(Live, "LYNTAI_LIVE_ACP not set");
        Skip.IfNot(ProcessRunner.CommandExists("claude-agent-acp"), "no claude-agent-acp on PATH");

        var ended = await LoggedOutTurn("claude-acp", "claude-agent-acp", "CLAUDE_CONFIG_DIR",
            ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN"]);

        Assert.True(ended.IsError);
        Assert.NotNull(ended.Failure);
        Assert.NotEqual(ProviderVerdict.Ok, ended.Verdict);
    }
}
