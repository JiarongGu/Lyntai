using Lyntai.Agents;
using Lyntai.Providers.Acp;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The inactivity window an agent door arms. A coding agent goes quiet between a tool step's start and
/// end (<c>codex exec --json</c> carries no heartbeat), so the agent doors take
/// <see cref="LyntaiOptions.AgentTimeout"/> where a text call keeps <see cref="LyntaiOptions.ProviderTimeout"/>.</summary>
public class AgentTimeoutTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Fact]
    public void The_agent_window_defaults_to_fifteen_minutes_and_a_text_call_keeps_two()
    {
        var options = new LyntaiOptions();

        Assert.Equal(Window, options.AgentTimeout);
        Assert.Equal(Window, options.ResolveAgentTimeout(null));
        Assert.Equal(TimeSpan.FromMinutes(2), options.ResolveTimeout(seconds: null));
    }

    [Fact]
    public void A_turns_own_seconds_win_clamped_to_the_ceiling_and_a_configured_window_is_trusted()
    {
        var options = new LyntaiOptions { AgentTimeout = TimeSpan.FromMinutes(45), MaxProviderTimeout = TimeSpan.FromMinutes(30) };

        Assert.Equal(TimeSpan.FromSeconds(90), options.ResolveAgentTimeout(90));
        Assert.Equal(TimeSpan.FromMinutes(30), options.ResolveAgentTimeout(7200));
        Assert.Equal(TimeSpan.FromMinutes(45), options.ResolveAgentTimeout(null));
        Assert.Equal(TimeSpan.FromMinutes(45), options.ResolveAgentTimeout(0));
    }

    [Fact]
    public async Task The_codex_door_arms_the_agent_window_unless_the_turn_names_one()
    {
        var runner = new FakeProcessRunner();
        var session = new CodexAgentSession(runner, new LyntaiOptions(), command: "codex");

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();
        Assert.Equal(Window, runner.LastInactivityTimeout);

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi", TimeoutSeconds = 60 }).ToListAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), runner.LastInactivityTimeout);
    }

    [Fact]
    public async Task The_claude_door_arms_the_agent_window_unless_the_turn_names_one()
    {
        var runner = new FakeProcessRunner();
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), logger: null, command: "claude", environment: null);

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();
        Assert.Equal(Window, runner.LastInactivityTimeout);

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi", TimeoutSeconds = 60 }).ToListAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), runner.LastInactivityTimeout);
    }

    [Fact]
    public async Task The_acp_door_arms_the_agent_window_unless_the_turn_names_one()
    {
        var runner = new FakeProcessRunner { ExchangeHandler = _ => null };
        var session = new AcpAgentSession(runner, new LyntaiOptions(), "agent");

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();
        Assert.Equal(Window, runner.Exchanges[^1].InactivityTimeout);

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi", TimeoutSeconds = 60 }).ToListAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), runner.Exchanges[^1].InactivityTimeout);
    }
}
