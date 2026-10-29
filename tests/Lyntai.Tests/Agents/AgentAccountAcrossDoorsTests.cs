using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>One account across the doors of one agent (<c>D234</c>): each door is its own provider and names the
/// agent it shares accounts with, the pool files the account under that name, and a door that would hand the home to
/// another variable is refused when it is registered. The two doors are the claude CLI and an ACP adapter over a
/// recording runner, so the variable each child is started with is read off the spawn. Zero tokens.</summary>
public class AgentAccountAcrossDoorsTests
{
    private const string Agent = "claude";
    private const string Home = "/accounts/work-home";
    private const string Variable = "CLAUDE_CONFIG_DIR";

    private static readonly AgentAccount Work = new() { Provider = Agent, Name = "work", Home = Home };

    private static AgentChoice Cli(string? model = null) =>
        new() { Provider = "claude-cli", Model = model, Accounts = [Work] };

    private static AgentChoice Acp(string? model = null) =>
        new() { Provider = "claude-acp", Model = model, Accounts = [Work] };

    private static ServiceProvider Compose(
        FakeProcessRunner runner, ManualTimeProvider clock, Action<AgentPoolOptions> pool, IKeyValueStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddSingleton<TimeProvider>(clock);
        if (store is not null) services.AddSingleton(store);
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddClaudeCliAgentSession(command: "claude", agent: Agent)
            .AddAcpAgentSession("claude-acp", "claude-agent-acp", accountHomeVariable: Variable, agent: Agent)
            .AddAgentAccountPool(pool));
        return services.BuildServiceProvider();
    }

    private static FakeProcessRunner Runner() => new(
    [
        """{"type":"system","session_id":"s-1","model":"m"}""",
        """{"type":"result","result":"Done","session_id":"s-1","is_error":false,"usage":{"input_tokens":2,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"subtype":null}""",
    ]);

    /// <summary>The turn the pick says, through the registered provider of the seat's choice.</summary>
    private static async Task<SessionEnded> Run(ServiceProvider sp, AgentPick pick)
    {
        var provider = sp.GetRequiredService<AgentProviderRegistry>().Get(pick.Seat.Choice.Provider);
        var events = await provider.Open(provider.Describe().Doors.Single().Id)
            .StreamAsync(pick.Apply(new AgentSessionOptions { Prompt = "go", ToolPolicy = AgentToolPolicy.Write })).ToListAsync();
        return Assert.IsType<SessionEnded>(events[^1]);
    }

    private static SessionEnded Limit(RestrictionRecovery recovery, RestrictionSubject subject = RestrictionSubject.Account) =>
        new(ProviderVerdict.Failed, true, null, null, null, "limit")
        {
            Failure = new AgentFailure(AgentFailureKind.Limit, "test")
            {
                Restriction = new AgentRestriction(RestrictionCause.Quota, subject, recovery),
            },
        };

    private static async Task<AgentPick> Take(AgentAccountPool pool) =>
        (await pool.PickAsync()).Pick ?? throw new Xunit.Sdk.XunitException("no seat was permitted");

    // ── one account, picked through each door ────────────────────────────────────────────────────

    [Fact]
    public async Task One_account_is_picked_through_each_door_and_reaches_its_child_in_the_agents_variable()
    {
        var runner = Runner();
        var clock = new ManualTimeProvider();
        await using var sp = Compose(runner, clock, o => { o.Choices.Add(Cli("opus")); o.Choices.Add(Acp("sonnet")); });
        var pool = sp.GetRequiredService<AgentAccountPool>();

        var viaCli = await Take(pool);
        var cliEnded = await Run(sp, viaCli);
        await pool.ReportAsync(viaCli, Limit(RestrictionRecovery.OwnerAction, RestrictionSubject.Model));
        var viaAcp = await Take(pool);
        var acpEnded = await Run(sp, viaAcp);

        Assert.Equal(("claude-cli", "claude-acp"), (viaCli.Seat.Choice.Provider, viaAcp.Seat.Choice.Provider));
        Assert.Equal(Work, viaCli.Seat.Account);
        Assert.Equal(Work, viaAcp.Seat.Account);
        Assert.False(cliEnded.IsError);
        Assert.NotEqual(AgentProvider.AccountRefusedSubtype, acpEnded.Subtype);
        Assert.Equal(Home, Assert.Single(runner.Calls, c => c.Command == "claude").Environment![Variable]);
        Assert.Equal(Home, Assert.Single(runner.Exchanges).Environment![Variable]);
    }

    [Fact]
    public async Task An_account_filed_under_a_door_s_own_id_still_runs_through_that_door_only()
    {
        var runner = Runner();
        await using var sp = Compose(runner, new ManualTimeProvider(), o => o.Choices.Add(Cli()));
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var own = Work with { Provider = "claude-cli" };

        var cli = await registry.Get("claude-cli").Open("cli")
            .StreamAsync(new AgentSessionOptions { Prompt = "go", Account = own }).ToListAsync();
        var acp = await registry.Get("claude-acp").Open("acp")
            .StreamAsync(new AgentSessionOptions { Prompt = "go", Account = own }).ToListAsync();

        Assert.False(Assert.IsType<SessionEnded>(cli[^1]).IsError);
        Assert.Equal(AgentProvider.AccountRefusedSubtype, Assert.IsType<SessionEnded>(acp[^1]).Subtype);
        Assert.Empty(runner.Exchanges);
    }

    [Fact]
    public async Task An_account_of_another_agent_is_refused_by_every_door_before_a_spawn()
    {
        var runner = Runner();
        await using var sp = Compose(runner, new ManualTimeProvider(), o => o.Choices.Add(Cli()));
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var stranger = Work with { Provider = "codex" };

        foreach (var (provider, door) in new[] { ("claude-cli", "cli"), ("claude-acp", "acp") })
        {
            var events = await registry.Get(provider).Open(door)
                .StreamAsync(new AgentSessionOptions { Prompt = "go", Account = stranger }).ToListAsync();

            var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
            Assert.Equal(AgentProvider.AccountRefusedSubtype, ended.Subtype);
            Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        }
        Assert.Empty(runner.Calls);
    }

    // ── a hold or a cool-off on the account is seen by every door ────────────────────────────────

    [Fact]
    public async Task A_limit_met_through_one_door_holds_the_account_in_the_other()
    {
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o => { o.Choices.Add(Cli()); o.Choices.Add(Acp()); });
        var pool = sp.GetRequiredService<AgentAccountPool>();
        var start = clock.GetUtcNow();

        var pick = await Take(pool);
        await pool.ReportAsync(pick, Limit(RestrictionRecovery.ResetAt(start + TimeSpan.FromHours(1))));

        Assert.Equal("claude-cli", pick.Seat.Choice.Provider);
        var held = await pool.PickAsync();
        Assert.Null(held.Pick);
        Assert.Equal(start + TimeSpan.FromHours(1), held.ReadyAt);
        Assert.Equal("account claude/work", Assert.Single(await pool.HoldsAsync()).Scope);

        // the reset passes: ONE attempt, on the first seat, and the other door waits for it
        clock.Advance(TimeSpan.FromHours(1));
        var probe = await Take(pool);
        Assert.True(probe.Seat.IsProbe);
        Assert.Null((await pool.PickAsync()).Pick);
        await pool.ReportAsync(probe, new SessionEnded(ProviderVerdict.Ok, false, "success", "s", "done", null));
        Assert.Equal("claude-cli", (await Take(pool)).Seat.Choice.Provider);
    }

    [Fact]
    public async Task A_hold_the_host_must_clear_is_cleared_for_both_doors_at_once()
    {
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o => { o.Choices.Add(Cli()); o.Choices.Add(Acp()); });
        var pool = sp.GetRequiredService<AgentAccountPool>();

        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction));
        Assert.Null((await pool.PickAsync()).Pick);

        var hold = Assert.Single(await pool.HoldsAsync());
        Assert.True(await pool.ClearAsync(hold.Id, hold.Revision));
        var probe = await Take(pool);
        Assert.True(probe.Seat.IsProbe);
        Assert.Null((await pool.PickAsync()).Pick);
    }

    [Fact]
    public async Task A_cool_off_the_host_chose_for_an_unstated_recovery_is_one_attempt_across_the_doors()
    {
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o =>
        {
            o.Choices.Add(Cli());
            o.Choices.Add(Acp());
            o.UnknownProbeAfter = TimeSpan.FromMinutes(10);
        });
        var pool = sp.GetRequiredService<AgentAccountPool>();

        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.Unknown));
        Assert.Null((await pool.PickAsync()).Pick);

        clock.Advance(TimeSpan.FromMinutes(10));
        var probe = await Take(pool);
        Assert.True(probe.Seat.IsProbe);
        Assert.Null((await pool.PickAsync()).Pick);
        await pool.AbandonAsync(probe);
        Assert.NotNull((await pool.PickAsync()).Pick);
    }

    [Fact]
    public async Task A_model_held_on_the_account_holds_that_model_through_both_doors_and_no_other()
    {
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o =>
        {
            o.Choices.Add(Cli("opus"));
            o.Choices.Add(Acp("opus"));
            o.Choices.Add(Acp("sonnet"));
        });
        var pool = sp.GetRequiredService<AgentAccountPool>();

        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionSubject.Model));

        var next = await Take(pool);
        Assert.Equal(("claude-acp", "sonnet"), (next.Seat.Choice.Provider, next.Seat.Choice.Model));
    }

    [Fact]
    public async Task An_endpoint_is_one_providers_so_a_hold_on_it_leaves_the_other_door_open()
    {
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o => { o.Choices.Add(Cli()); o.Choices.Add(Acp()); });
        var pool = sp.GetRequiredService<AgentAccountPool>();

        await pool.ReportAsync(await Take(pool), Limit(RestrictionRecovery.OwnerAction, RestrictionSubject.Endpoint));

        Assert.Equal("claude-acp", (await Take(pool)).Seat.Choice.Provider);
        Assert.Equal("endpoint of claude-cli", Assert.Single(await pool.HoldsAsync()).Scope);
    }

    [Fact]
    public async Task An_account_of_one_provider_keeps_the_hold_key_it_always_had_and_a_shared_one_names_the_agent()
    {
        var store = new InMemoryKeyValueStore();
        var clock = new ManualTimeProvider();
        await using var sp = Compose(Runner(), clock, o =>
        {
            o.Choices.Add(Cli());
            o.Choices.Add(new AgentChoice { Provider = "claude-acp", Accounts = [Work with { Provider = "claude-acp", Name = "solo" }] });
        }, store);
        var pool = sp.GetRequiredService<AgentAccountPool>();

        var shared = await Take(pool);
        await pool.ReportAsync(shared, Limit(RestrictionRecovery.OwnerAction));
        var solo = await Take(pool);
        await pool.ReportAsync(solo, Limit(RestrictionRecovery.OwnerAction));

        Assert.Equal(
            ["lyntai:agent-pool:default:hold:account|claude-acp|solo|quota", "lyntai:agent-pool:default:hold:account|claude|work|quota"],
            (await store.ListKeysAsync("lyntai:agent-pool:")).Order(StringComparer.Ordinal));
    }

    // ── what the pool refuses to be built over ───────────────────────────────────────────────────

    [Fact]
    public async Task A_pool_refuses_an_account_that_is_neither_its_providers_nor_its_agents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(Runner());
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddClaudeCliAgentSession(command: "claude", agent: Agent)
            .AddAgentAccountPool(o => o.Choices.Add(
                new AgentChoice { Provider = "claude-cli", Accounts = [Work with { Provider = "codex" }] })));
        await using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<ArgumentException>(() => sp.GetRequiredService<AgentAccountPool>());
        Assert.Contains("claude-cli", ex.Message);
        Assert.Contains(Agent, ex.Message);
    }

    // ── a door whose variable differs is refused at registration ─────────────────────────────────

    [Fact]
    public void A_door_that_reads_the_home_from_another_variable_is_refused_when_it_is_registered_in_either_order()
    {
        var cliFirst = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLyntai(b => b
            .AddClaudeCliAgentSession(command: "claude", agent: Agent)
            .AddAcpAgentSession("other-acp", "other", accountHomeVariable: "OTHER_HOME", agent: Agent)));
        var acpFirst = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("other-acp", "other", accountHomeVariable: "OTHER_HOME", agent: Agent)
            .AddClaudeCliAgentSession(command: "claude", agent: Agent)));

        foreach (var ex in new[] { cliFirst, acpFirst })
        {
            Assert.Contains("CLAUDE_CONFIG_DIR", ex.Message);
            Assert.Contains("OTHER_HOME", ex.Message);
            Assert.Contains("other-acp", ex.Message);
        }
    }

    [Fact]
    public void Every_door_of_one_agent_reads_the_same_variable_and_two_agents_may_differ()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(Runner());
        services.AddLyntai(b => b
            .AddClaudeCliAgentSession(command: "claude", agent: Agent)
            .AddAcpAgentSession("claude-acp", "claude-agent-acp", accountHomeVariable: Variable, agent: Agent)
            .AddCodexCliAgentSession(command: "codex")
            .AddAcpAgentSession("codex-acp", "codex-acp", accountHomeVariable: "CODEX_HOME", agent: "codex-cli"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        Assert.Equal(["claude-acp", "claude-cli", "codex-acp", "codex-cli"], registry.Ids);
        Assert.Equal(Agent, registry.Get("claude-acp").Describe().AccountKey);
        Assert.Equal("codex-cli", registry.Get("codex-acp").Describe().AccountKey);
        Assert.Equal("codex-cli", registry.Get("codex-cli").Describe().AccountKey);
    }

    [Fact]
    public void A_provider_a_host_registers_without_its_descriptor_is_held_to_the_same_rule_when_the_registry_is_built()
    {
        static AgentProvider Door(string id, string variable) => new(
            new AgentProviderDescriptor
            {
                Id = id,
                Agent = Agent,
                Accounts = new AgentAccountMapping(variable),
                Doors = [new AgentDoor { Id = "d", Kind = "host-own", Abilities = [] }],
            },
            _ => throw new NotSupportedException());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AgentProviderRegistry([Door("one", "A_HOME"), Door("two", "B_HOME")]));

        Assert.Contains("A_HOME", ex.Message);
        Assert.Contains("B_HOME", ex.Message);
        _ = new AgentProviderRegistry([Door("one", "A_HOME"), Door("two", "A_HOME")]);
    }

    [Fact]
    public void An_agent_needs_the_variable_its_program_reads_and_a_blank_one_is_no_agent()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("claude-acp", "claude-agent-acp", agent: Agent)));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b
            .AddClaudeCliAgentSession(command: "claude", agent: " ")));
        Assert.Throws<ArgumentException>(() => new AgentProvider(
            new AgentProviderDescriptor
            {
                Id = "x",
                Agent = Agent,
                Doors = [new AgentDoor { Id = "d", Kind = "host-own", Abilities = [] }],
            },
            _ => throw new NotSupportedException()));
    }

    [Fact]
    public void A_provider_declared_one_way_and_built_another_is_refused_when_it_is_built()
    {
        var services = new ServiceCollection();
        var descriptor = new AgentProviderDescriptor
        {
            Id = "x",
            Agent = Agent,
            Accounts = new AgentAccountMapping(Variable),
            Doors = [new AgentDoor { Id = "d", Kind = "host-own", Abilities = [] }],
        };
        services.AddLyntai(b => b.AddAgentProvider(descriptor, _ => new AgentProvider(
            descriptor with { Agent = "elsewhere" }, _ => throw new NotSupportedException())));
        using var sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<AgentProviderRegistry>());
    }

    // ── the allowance read ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_allowance_of_a_shared_account_is_read_through_the_door_that_runs_the_agents_cli()
    {
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) => args.Contains("--version") ? FakeProcessRunner.Ok("2.1.296 (Claude Code)") : FakeProcessRunner.Ok(""),
            ExchangeHandler = sent => sent.Contains("get_usage") ? [UsageCaptures.ClaudeSignedIn_2_1_296] : [],
        };
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b.AddClaudeCliAgentSession(command: "claude", agent: Agent).AddAgentUsageReading());
        await using var sp = services.BuildServiceProvider();
        var source = Assert.IsAssignableFrom<IAgentUsageSource>(sp.GetRequiredService<AgentProviderRegistry>().Get("claude-cli"));

        var reading = await source.ReadAllowanceAsync(Work);

        Assert.True(reading.IsKnown);
        Assert.Equal(Home, runner.Exchanges.Single().Environment![Variable]);
    }
}
