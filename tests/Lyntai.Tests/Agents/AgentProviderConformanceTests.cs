using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The conformance theory over EVERY registered <see cref="IAgentProvider"/>: the two CLI doors, the
/// protocol door, the HTTP door and a host's own stand-in, all through one registry from one source, at zero tokens
/// (a recording runner stands in for the spawn, and a counting handler for the HTTP door's call). The set is read from the registry, so a provider added to it is held to the same rules
/// without this file changing. What a door's STREAM keeps is the sibling theory, <see cref="AgentStreamConformanceTests"/>,
/// over this same registry.</summary>
public class AgentProviderConformanceTests
{
    private const string StandIn = DialectStandIn.Id;

    /// <summary>An endpoint that answers every request with one plain completion and counts them: the HTTP door's
    /// spawn.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}"""),
            });
        }
    }

    /// <summary>The one registry both theories read: every door the layer ships and a host's own, over a recording
    /// runner and an endpoint handler. <paramref name="tool"/> joins the registered tools, which only the HTTP door
    /// reads.</summary>
    internal static ServiceProvider Compose(FakeProcessRunner runner, HttpMessageHandler? endpoint = null, ITool? tool = null)
    {
        endpoint ??= new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b =>
        {
            b.AddClaudeCliAgentSession(command: "claude")
                .AddCodexCliAgentSession(command: "codex")
                .AddAcpAgentSession("acp-agent", "acp-agent-cmd", accountHomeVariable: "ACP_AGENT_HOME")
                .AddHttpAgentSession("http-agent", o => { o.BaseUrl = "https://endpoint.test"; o.ApiKey = "k"; },
                    _ => new HttpClient(endpoint, disposeHandler: false))
                .AddAgentProvider(_ => DialectStandIn.Provider(runner));
            if (tool is not null) b.AddTool(_ => tool);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>Every (provider, door) the registry holds — the theory's one source.</summary>
    public static TheoryData<string, string> Doors()
    {
        using var sp = Compose(new FakeProcessRunner());
        var data = new TheoryData<string, string>();
        foreach (var provider in sp.GetRequiredService<AgentProviderRegistry>().All)
        {
            var descriptor = provider.Describe();
            foreach (var door in descriptor.Doors) data.Add(descriptor.Id, door.Id);
        }
        return data;
    }

    /// <summary>The options that need exactly <paramref name="ability"/> and nothing else a door could refuse.</summary>
    private static AgentSessionOptions Needing(string ability, string providerId = StandIn)
    {
        var options = new AgentSessionOptions { Prompt = "go", ToolPolicy = AgentToolPolicy.Write };
        return ability switch
        {
            AgentAbilities.Accounts => options with { Account = AccountOf(providerId) },
            AgentAbilities.Resume => options with { ResumeToken = "conv-1" },
            AgentAbilities.ReadOnlyGate => options with { ToolPolicy = AgentToolPolicy.ReadOnly },
            AgentAbilities.McpServers => options with { McpServers = [new AgentMcpServer { Name = "app", Command = "app-mcp" }] },
            AgentAbilities.DisallowedTools => options with { DisallowedTools = ["Bash"] },
            AgentAbilities.SystemPrompt => options with { SystemPrompt = "be brief" },
            AgentAbilities.Model => options with { Model = "model-1" },
            // a mode is the agent's own word: the codex exec door's are its sandbox words, so the same word cannot serve every door
            AgentAbilities.PermissionMode => options with { PermissionMode = providerId == "codex-cli" ? "workspace-write" : "plan" },
            AgentAbilities.Effort => options with { Effort = "high" },
            AgentAbilities.Plugins => options with { PluginDirectories = [Path.GetFullPath("plugin-a")] },
            AgentAbilities.Environment => options with { Environment = new Dictionary<string, string?> { ["TURN_VAR"] = "v" } },
            _ => throw new ArgumentOutOfRangeException(nameof(ability), ability, "not an option-shaped ability"),
        };
    }

    private const string AccountHome = "/accounts/work-home";

    private static AgentAccount AccountOf(string providerId) =>
        new() { Provider = providerId, Name = "work", Home = AccountHome };

    private static AgentDoor DoorOf(AgentProviderRegistry registry, string providerId, string doorId) =>
        registry.Get(providerId).Describe().Doors.Single(d => d.Id == doorId);

    private static async Task<List<AgentStreamEvent>> Run(IAgentProvider provider, string doorId, AgentSessionOptions options) =>
        await provider.Open(doorId).StreamAsync(options).ToListAsync();

    // ── every provider ───────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public void Each_door_states_every_ability_once(string providerId, string doorId)
    {
        using var sp = Compose(new FakeProcessRunner());
        var door = DoorOf(sp.GetRequiredService<AgentProviderRegistry>(), providerId, doorId);

        Assert.Equal(AgentAbilities.All.Order(StringComparer.Ordinal), door.Abilities.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.All(door.Abilities, claim => Assert.False(string.IsNullOrWhiteSpace(claim.Evidence), $"{claim.Name} names no evidence"));
    }

    [Theory, MemberData(nameof(Doors))]
    public void An_unmeasured_ability_is_unknown_and_a_measured_one_never_is(string providerId, string doorId)
    {
        using var sp = Compose(new FakeProcessRunner());
        var door = DoorOf(sp.GetRequiredService<AgentProviderRegistry>(), providerId, doorId);

        Assert.All(door.Abilities.Where(a => a.Support == AgentSupport.Unknown),
            claim => Assert.NotEqual(AgentAbilitySource.Measured, claim.Source));
        Assert.All(door.Abilities.Where(a => a.Source == AgentAbilitySource.Measured),
            claim => Assert.NotEqual(AgentSupport.Unknown, claim.Support));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_option_the_door_cannot_honour_is_refused_before_a_spawn(string providerId, string doorId)
    {
        var runner = new FakeProcessRunner();
        var endpoint = new CountingHandler();
        using var sp = Compose(runner, endpoint);
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var door = DoorOf(registry, providerId, doorId);

        foreach (var ability in door.Abilities.Where(a => a.Support == AgentSupport.Unsupported)
                     .Select(a => a.Name).Where(OptionAbilities.Contains))
        {
            var events = await Run(registry.Get(providerId), doorId, Needing(ability, providerId));

            var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
            Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
            Assert.True(ended.IsError);
            Assert.Equal(AgentProvider.OptionRefusedSubtype, ended.Subtype);
            Assert.Contains(ability, ended.Diagnostic);
            Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        }

        Assert.Empty(runner.Calls);
        Assert.Equal(0, endpoint.Calls);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_option_the_door_supports_or_has_not_measured_reaches_the_spawn(string providerId, string doorId)
    {
        var runner = new FakeProcessRunner();
        var endpoint = new CountingHandler();
        using var sp = Compose(runner, endpoint);
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var door = DoorOf(registry, providerId, doorId);

        var reachable = door.Abilities.Where(a => a.Support != AgentSupport.Unsupported)
            .Select(a => a.Name).Where(OptionAbilities.Contains).ToList();
        foreach (var ability in reachable)
        {
            var events = await Run(registry.Get(providerId), doorId, Needing(ability, providerId));
            Assert.DoesNotContain(events, e => e is SessionEnded { Subtype: AgentProvider.OptionRefusedSubtype });
        }

        Assert.Equal(reachable.Count, runner.Calls.Count + endpoint.Calls);
    }

    private static readonly string[] OptionAbilities =
    [
        AgentAbilities.Resume, AgentAbilities.ReadOnlyGate, AgentAbilities.McpServers,
        AgentAbilities.DisallowedTools, AgentAbilities.SystemPrompt, AgentAbilities.Model, AgentAbilities.Accounts,
        AgentAbilities.PermissionMode, AgentAbilities.Effort, AgentAbilities.Plugins, AgentAbilities.Environment,
    ];

    [Theory, MemberData(nameof(Doors))]
    public void A_door_reads_accounts_exactly_when_it_says_how(string providerId, string doorId)
    {
        using var sp = Compose(new FakeProcessRunner());
        var provider = sp.GetRequiredService<AgentProviderRegistry>().Get(providerId);
        var supported = DoorOf(sp.GetRequiredService<AgentProviderRegistry>(), providerId, doorId)
            .SupportFor(AgentAbilities.Accounts) == AgentSupport.Supported;

        Assert.Equal(supported, !string.IsNullOrWhiteSpace(provider.Describe().Accounts?.HomeVariable));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_account_of_another_provider_is_refused_before_a_spawn(string providerId, string doorId)
    {
        var runner = new FakeProcessRunner();
        var endpoint = new CountingHandler();
        using var sp = Compose(runner, endpoint);
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var foreign = Needing(AgentAbilities.Accounts, registry.Ids.First(id => id != providerId));

        var events = await Run(registry.Get(providerId), doorId, foreign);

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Equal(AgentProvider.AccountRefusedSubtype, ended.Subtype);
        Assert.Contains($"'{providerId}'", ended.Diagnostic);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
        Assert.Empty(runner.Calls);
        Assert.Equal(0, endpoint.Calls);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_account_of_its_own_provider_reaches_the_child_in_the_doors_home_variable(string providerId, string doorId)
    {
        var runner = new FakeProcessRunner();
        using var sp = Compose(runner);
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        if (registry.Get(providerId).Describe().Accounts is not { } mapping)
        {
            // a door that reads none must say so, and refuses an account (held above): not a pass by omission
            Assert.Equal(AgentSupport.Unsupported,
                DoorOf(registry, providerId, doorId).Abilities.Single(a => a.Name == AgentAbilities.Accounts).Support);
            return;
        }

        await Run(registry.Get(providerId), doorId, Needing(AgentAbilities.Accounts, providerId));

        var call = Assert.Single(runner.Calls);
        Assert.Equal(AccountHome, call.Environment![mapping.HomeVariable]);
    }

    [Theory, MemberData(nameof(Doors))]
    public void A_door_reads_usage_exactly_when_it_says_so_and_declares_what_its_start_may_do(string providerId, string doorId)
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();
        var door = DoorOf(registry, providerId, doorId);
        var usage = door.Abilities.Single(a => a.Name == AgentAbilities.UsageReading);
        var supported = usage.Support == AgentSupport.Supported;

        Assert.Equal(supported, registry.Get(providerId) is IAgentUsageSource);
        if (supported)
        {
            Assert.NotEqual(AgentSideEffects.None, usage.SideEffects);
            Assert.False(string.IsNullOrWhiteSpace(usage.AppliesTo), "a usage reading is validated per version: say which");
        }

        // only an ability that starts a vendor's own process has effects to declare
        Assert.All(door.Abilities.Where(a => a.Name != AgentAbilities.UsageReading),
            claim => Assert.Equal(AgentSideEffects.None, claim.SideEffects));
    }

    [Fact]
    public void The_usage_claim_names_exactly_the_versions_its_reader_is_validated_on()
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        Assert.Equal("2.1.296", AppliesTo(registry, "claude-cli"));
        Assert.Equal("0.160.0", AppliesTo(registry, "codex-cli"));
        Assert.Equal(AgentSideEffects.RefreshesCredentials | AgentSideEffects.ChangesConfiguration | AgentSideEffects.LoadsExtensions
            | AgentSideEffects.OpensListener | AgentSideEffects.LeavesProcess,
            registry.Get("claude-cli").Describe().Doors[0].Abilities.Single(a => a.Name == AgentAbilities.UsageReading).SideEffects);

        static string? AppliesTo(AgentProviderRegistry registry, string id) =>
            registry.Get(id).Describe().Doors[0].Abilities.Single(a => a.Name == AgentAbilities.UsageReading).AppliesTo;
    }

    [Fact]
    public void Both_cli_doors_declare_the_model_ability_and_the_protocol_door_negotiates_it_per_agent()
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        foreach (var id in new[] { "claude-cli", "codex-cli" })
        {
            var claim = DoorOf(registry, id, "cli").Abilities.Single(a => a.Name == AgentAbilities.Model);
            Assert.Equal((AgentSupport.Supported, AgentAbilitySource.Declared), (claim.Support, claim.Source));
        }

        var acp = DoorOf(registry, "acp-agent", "acp").Abilities.Single(a => a.Name == AgentAbilities.Model);
        Assert.Equal((AgentSupport.Unknown, AgentAbilitySource.Negotiated), (acp.Support, acp.Source));
    }

    [Fact]
    public void A_door_that_states_the_model_ability_unsupported_refuses_a_turn_that_names_one()
    {
        var door = new AgentDoor
        {
            Id = "d", Kind = "k",
            Abilities = [new AgentAbility(AgentAbilities.Model, AgentSupport.Unsupported, AgentAbilitySource.Declared)],
        };

        Assert.Equal([AgentAbilities.Model], door.Unhonourable(Needing(AgentAbilities.Model)));
        Assert.Empty(door.Unhonourable(new AgentSessionOptions { Prompt = "go", ToolPolicy = AgentToolPolicy.Write }));
    }

    // ── the registry, the doors and the descriptor ───────────────────────────

    [Fact]
    public void Both_cli_doors_the_acp_and_http_doors_and_a_hosts_own_are_registered_by_id()
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        Assert.Equal(["acp-agent", "claude-cli", "codex-cli", "http-agent", StandIn], registry.Ids);
        Assert.Equal(registry.Ids, registry.All.Select(p => p.Describe().Id));
        Assert.Equal(AgentDoorKinds.Cli, Assert.Single(registry.Get("claude-cli").Describe().Doors).Kind);
        Assert.Equal(AgentDoorKinds.Acp, Assert.Single(registry.Get("acp-agent").Describe().Doors).Kind);
        Assert.Equal(AgentDoorKinds.Http, Assert.Single(registry.Get("http-agent").Describe().Doors).Kind);
    }

    [Fact]
    public void An_unknown_id_throws_and_names_what_exists()
    {
        using var sp = Compose(new FakeProcessRunner());
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        var ex = Assert.Throws<KeyNotFoundException>(() => registry.Get("gemini-cli"));

        Assert.Contains("gemini-cli", ex.Message);
        Assert.Contains("acp-agent, claude-cli, codex-cli, http-agent, stand-in", ex.Message);
        Assert.False(registry.TryGet("gemini-cli", out _));
        Assert.True(registry.TryGet("codex-cli", out var found));
        Assert.Equal("codex-cli", found.Describe().Id);
    }

    [Fact]
    public void With_no_agent_adapter_the_registry_is_empty_and_says_so()
    {
        var services = new ServiceCollection();
        services.AddLyntai(_ => { });
        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<KeyNotFoundException>(() => sp.GetRequiredService<AgentProviderRegistry>().Get("claude-cli"));

        Assert.Contains("None is registered", ex.Message);
    }

    [Fact]
    public void A_second_registration_under_one_id_replaces_the_first()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        services.AddLyntai(b => b
            .AddClaudeCliAgentSession(command: "first", id: "claude-cli")
            .AddClaudeCliAgentSession(command: "second", id: "claude-cli"));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["claude-cli"], sp.GetRequiredService<AgentProviderRegistry>().Ids);
    }

    [Fact]
    public void A_door_the_provider_lacks_throws_and_names_the_doors_it_has()
    {
        using var sp = Compose(new FakeProcessRunner());

        var ex = Assert.Throws<KeyNotFoundException>(() => sp.GetRequiredService<AgentProviderRegistry>().Get("claude-cli").Open("acp"));

        Assert.Contains("'acp'", ex.Message);
        Assert.Contains("Doors: cli", ex.Message);
    }

    [Fact]
    public async Task The_keyed_session_keeps_its_own_handling_of_an_option_the_door_cannot_honour()
    {
        var runner = new FakeProcessRunner(["{\"type\":\"turn.completed\"}"]);
        using var sp = Compose(runner);

        // the provider refuses disallowed tools on codex; the keyed session beside it logs them and spawns
        var events = await sp.GetRequiredKeyedService<IAgentSession>("codex-cli")
            .StreamAsync(Needing(AgentAbilities.DisallowedTools)).ToListAsync();

        Assert.Single(runner.Calls);
        Assert.DoesNotContain(events, e => e is SessionEnded { Subtype: AgentProvider.OptionRefusedSubtype });
    }

    [Fact]
    public void Options_ask_for_the_abilities_they_use_in_a_fixed_order()
    {
        var everything = new AgentSessionOptions
        {
            Prompt = "go", ResumeToken = "t", SystemPrompt = "s", Model = "m", DisallowedTools = ["Bash"],
            McpServers = [new AgentMcpServer { Name = "app", Command = "x" }],
            Account = AccountOf("p"), PermissionMode = "plan", Effort = "high", PluginDirectories = [Path.GetFullPath("plugin-a")],
            Environment = new Dictionary<string, string?> { ["TURN_VAR"] = "v" },
        };

        Assert.Equal(
            [AgentAbilities.Resume, AgentAbilities.ReadOnlyGate, AgentAbilities.McpServers,
                AgentAbilities.DisallowedTools, AgentAbilities.SystemPrompt, AgentAbilities.Model, AgentAbilities.Accounts,
                AgentAbilities.PermissionMode, AgentAbilities.Effort, AgentAbilities.Plugins, AgentAbilities.Environment],
            AgentAbilities.Needed(everything));
        Assert.Equal([AgentAbilities.ReadOnlyGate], AgentAbilities.Needed(new AgentSessionOptions { Prompt = "go" }));
        Assert.Empty(AgentAbilities.Needed(new AgentSessionOptions { Prompt = "go", ToolPolicy = AgentToolPolicy.Write }));
    }

    [Fact]
    public void A_door_that_states_nothing_about_an_ability_reads_unknown_and_refuses_nothing()
    {
        var door = new AgentDoor { Id = "d", Kind = "k", Abilities = [] };

        Assert.Equal(AgentSupport.Unknown, door.SupportFor(AgentAbilities.Resume));
        Assert.Empty(door.Unhonourable(Needing(AgentAbilities.Resume)));
    }

    [Fact]
    public void A_malformed_descriptor_is_refused_at_construction()
    {
        static AgentDoor Door(string id, params AgentAbility[] abilities) => new() { Id = id, Kind = "k", Abilities = abilities };

        Assert.Throws<ArgumentException>(() => new AgentProvider(new AgentProviderDescriptor { Id = " ", Doors = [Door("a")] }, _ => null!));
        Assert.Throws<ArgumentException>(() => new AgentProvider(new AgentProviderDescriptor { Id = "p", Doors = [] }, _ => null!));
        Assert.Throws<ArgumentException>(() => new AgentProvider(new AgentProviderDescriptor { Id = "p", Doors = [Door("a"), Door("a")] }, _ => null!));
        Assert.Throws<ArgumentException>(() => new AgentProvider(new AgentProviderDescriptor { Id = "p", Doors = [Door(" ")] }, _ => null!));
        var twice = new AgentAbility(AgentAbilities.Resume, AgentSupport.Supported, AgentAbilitySource.Declared);
        Assert.Throws<ArgumentException>(() => new AgentProvider(new AgentProviderDescriptor { Id = "p", Doors = [Door("a", twice, twice)] }, _ => null!));
    }
}
