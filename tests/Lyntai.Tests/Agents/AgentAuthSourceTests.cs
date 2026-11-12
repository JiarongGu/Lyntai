using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The sign-in state and the sign-in per account home, behind <see cref="IAgentAuthSource"/> on the provider a CLI door
/// registers. Zero tokens and no browser: a recording runner stands for the program, so what is held here is which command is
/// spawned under which home, what a refusal says before a spawn, and that a flow's lines reach the host while it still waits.
/// The two programs' own output shapes are held by the inference providers' tests.</summary>
public class AgentAuthSourceTests
{
    private const string Home = "/accounts/work-home";

    private static ServiceProvider Compose(IProcessRunner runner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runner);
        services.AddLyntai(b => b.AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex"));
        return services.BuildServiceProvider();
    }

    private static IAgentAuthSource Source(ServiceProvider sp, string providerId) =>
        Assert.IsAssignableFrom<IAgentAuthSource>(sp.GetRequiredService<AgentProviderRegistry>().Get(providerId));

    private static AgentAccount Account(string providerId) => new() { Provider = providerId, Name = "work", Home = Home };

    private const string ClaudeSignedIn = """{"loggedIn":true,"authMethod":"claude.ai","email":"a@example.test"}""";

    [Theory]
    [InlineData("claude-cli", "CLAUDE_CONFIG_DIR", ClaudeSignedIn)]
    [InlineData("codex-cli", "CODEX_HOME", "Logged in using ChatGPT")]
    public async Task A_status_is_the_programs_own_answer_read_under_the_accounts_home(string providerId, string homeVariable, string output)
    {
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok(output) };
        using var sp = Compose(runner);

        var status = await Source(sp, providerId).StatusAsync(Account(providerId));

        Assert.True(status.Authenticated);
        Assert.Equal(Home, runner.LastEnvironment![homeVariable]);
        Assert.Contains("status", runner.LastArgs!);
    }

    [Fact]
    public async Task Another_providers_account_is_refused_as_an_answer_before_anything_is_spawned()
    {
        var runner = new FakeProcessRunner();
        using var sp = Compose(runner);

        var status = await Source(sp, "claude-cli").StatusAsync(Account("codex-cli"));
        var login = await Source(sp, "claude-cli").LoginAsync(Account("codex-cli"));

        Assert.False(status.Authenticated);
        Assert.Contains("codex-cli", status.Detail);
        Assert.False(login.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_login_hands_each_line_to_the_host_while_it_waits_and_then_reads_the_state_it_left()
    {
        var runner = new FakeProcessRunner(["Open https://example.test/device and enter ABCD-EFGH"])
        {
            RunResult = FakeProcessRunner.Ok("Logged in using ChatGPT"),
        };
        using var sp = Compose(runner);
        var seen = new List<string>();

        var result = await Source(sp, "codex-cli").LoginAsync(Account("codex-cli"), null, seen.Add);

        Assert.Equal(["Open https://example.test/device and enter ABCD-EFGH"], seen);
        Assert.True(result.Succeeded);
        Assert.True(result.Status!.Authenticated);
        Assert.Equal(["login"], runner.Calls[0].Args.Skip(runner.Calls[0].Args.Count - 1).ToList());
        Assert.All(runner.Calls, c => Assert.Equal(Home, c.Environment!["CODEX_HOME"]));
        Assert.Contains("Open https://example.test/device", result.Detail);
    }

    [Fact]
    public async Task A_program_that_exits_non_zero_is_a_failed_result_and_not_a_throw()
    {
        var runner = new FakeProcessRunner([], new ProcessRunException("claude", 1, "no browser"));
        using var sp = Compose(runner);

        var result = await Source(sp, "claude-cli").LoginAsync(Account("claude-cli"));

        Assert.False(result.Succeeded);
        Assert.Contains("no browser", result.Detail);
    }

    [Fact]
    public async Task A_login_mode_the_program_has_no_flag_for_is_refused_before_a_spawn()
    {
        var runner = new FakeProcessRunner();
        using var sp = Compose(runner);

        var result = await Source(sp, "codex-cli").LoginAsync(Account("codex-cli"), new ProviderLoginRequest(Mode: "console"));

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Cancelling_the_wait_propagates()
    {
        var runner = new FakeProcessRunner(["waiting", "still waiting"]);
        using var sp = Compose(runner);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Source(sp, "claude-cli").LoginAsync(Account("claude-cli"), null, _ => cts.Cancel(), cts.Token));
    }

    [Fact]
    public void The_doors_state_the_ability_where_the_program_has_the_commands()
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        Assert.Equal(AgentSupport.Supported, registry.Get("claude-cli").Describe().Doors[0].SupportFor(AgentAbilities.SignIn));
        Assert.Equal(AgentSupport.Supported, registry.Get("codex-cli").Describe().Doors[0].SupportFor(AgentAbilities.SignIn));
    }
}
