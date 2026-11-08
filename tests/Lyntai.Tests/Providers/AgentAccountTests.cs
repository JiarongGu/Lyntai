using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>An <see cref="AgentAccount"/> reaching each CLI door: the home arrives in the child's own
/// configuration-home variable through the runner stub, a bad account is refused before a spawn, and the library
/// never touches the home or writes a credential. Another provider's account is the registry theory's
/// (<c>AgentProviderConformanceTests</c>). Zero tokens.</summary>
public class AgentAccountTests
{
    private const string Home = "/accounts/work-home";

    private static readonly string[] ClaudeSuccess =
    [
        """{"type":"system","session_id":"s-1","model":"m"}""",
        """{"type":"result","result":"Done","session_id":"s-1","is_error":false,"usage":{"input_tokens":2,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"subtype":null}""",
    ];

    private static readonly string[] CodexSuccess =
    [
        """{"type":"thread.started","thread_id":"t-1"}""",
        """{"type":"turn.started"}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}""",
        """{"type":"turn.completed","usage":{"input_tokens":2,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0}}""",
    ];

    /// <summary>The two doors, each as a session over the stub and the variable it must set.</summary>
    public static TheoryData<string> Doors() => new() { "claude", "codex" };

    private static (IAgentSession Session, FakeProcessRunner Runner, string Variable) Door(
        string door, IReadOnlyDictionary<string, string?>? environment = null, CapturingLogger? log = null)
    {
        if (door == "claude")
        {
            var runner = new FakeProcessRunner(ClaudeSuccess);
            return (new ClaudeAgentSession(runner, new LyntaiOptions(), log?.For<ClaudeAgentSession>(), "claude", environment),
                runner, ClaudeAgentDoor.AccountHomeVariable);
        }
        var codexRunner = new FakeProcessRunner(CodexSuccess);
        return (new CodexAgentSession(codexRunner, new LyntaiOptions(), log?.For<CodexAgentSession>(), "codex", environment),
            codexRunner, CodexAgentDoor.AccountHomeVariable);
    }

    private static AgentSessionOptions Ask(string? home = Home, string name = "work") => new()
    {
        Prompt = "go",
        ToolPolicy = AgentToolPolicy.Write,
        Account = new AgentAccount { Provider = "p", Name = name, Home = home! },
    };

    [Fact]
    public void Each_door_names_the_variable_its_agent_reads_its_home_from()
    {
        Assert.Equal("CLAUDE_CONFIG_DIR", ClaudeAgentDoor.Describe("claude-cli").Accounts!.HomeVariable);
        Assert.Equal("CODEX_HOME", CodexAgentDoor.Describe("codex-cli").Accounts!.HomeVariable);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task The_home_reaches_the_child_in_the_doors_variable(string door)
    {
        var (session, runner, variable) = Door(door);

        var events = await session.StreamAsync(Ask()).ToListAsync();

        Assert.Equal(Home, runner.LastEnvironment![variable]);
        Assert.False(Assert.IsType<SessionEnded>(events[^1]).IsError);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task The_account_wins_over_the_sessions_own_home_for_that_turn_and_leaves_the_rest(string door)
    {
        var standing = new Dictionary<string, string?>
        {
            ["CLAUDE_CONFIG_DIR"] = "portable/claude", ["CODEX_HOME"] = "portable/codex", ["OPENAI_API_KEY"] = null,
        };
        var (session, runner, variable) = Door(door, standing);

        await session.StreamAsync(Ask()).ToListAsync();
        await session.StreamAsync(Ask() with { Account = null }).ToListAsync();

        Assert.Equal(Home, runner.Calls[0].Environment![variable]);
        var other = variable == "CODEX_HOME" ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME";
        Assert.Equal(standing[other], runner.Calls[0].Environment![other]);
        Assert.True(runner.Calls[0].Environment!.TryGetValue("OPENAI_API_KEY", out var removed) && removed is null);
        // the next turn has no account: the session's own home is back, and the host's dictionary was never edited
        Assert.Equal(standing[variable], runner.Calls[1].Environment![variable]);
        Assert.Same(standing, runner.Calls[1].Environment);
        Assert.Equal("portable/claude", standing["CLAUDE_CONFIG_DIR"]);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_turn_with_no_account_adds_no_variable(string door)
    {
        var (session, runner, _) = Door(door);

        await session.StreamAsync(Ask() with { Account = null }).ToListAsync();

        Assert.Null(runner.LastEnvironment);
    }

    [Fact]
    public async Task The_codex_account_is_merged_with_the_turns_bearer_variables()
    {
        var (session, runner, variable) = Door("codex");

        await session.StreamAsync(Ask() with
        {
            McpServers = [AgentMcpServer.Http("remote", "https://tools.example.invalid/mcp", "tok")],
        }).ToListAsync();

        Assert.Equal(Home, runner.LastEnvironment![variable]);
        Assert.Equal("tok", runner.LastEnvironment["LYNTAI_MCP_TOKEN_REMOTE"]);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_account_the_child_cannot_be_handed_is_refused_before_a_spawn(string door)
    {
        foreach (var (home, name) in new[] { ("", "work"), ("  ", "work"), ("/a\0b", "work"), ("/a\nb", "work"), (Home, " ") })
        {
            var (session, runner, _) = Door(door);

            var events = await session.StreamAsync(Ask(home, name)).ToListAsync();

            var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
            Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
            Assert.True(ended.IsError);
            Assert.Equal("account-invalid", ended.Subtype);
            Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
            Assert.Empty(runner.Calls);
        }
    }

    // ── nothing is stored: the library holds a name and a home, and the home's contents are the agent's ──

    private const string Secret = "sk-live-0123456789-do-not-write";

    [Theory, MemberData(nameof(Doors))]
    public async Task No_credential_value_is_written_to_a_file_a_log_an_event_or_the_command_line(string door)
    {
        var home = Directory.CreateTempSubdirectory("lyntai-account-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(home, "auth.json"), $$"""{"token":"{{Secret}}"}""");
            File.WriteAllText(Path.Combine(home, "config.toml"), "model = \"m\"\n");
            var before = Snapshot(home);
            var log = new CapturingLogger();
            var (session, runner, variable) = Door(door, new Dictionary<string, string?> { ["OPENAI_API_KEY"] = null }, log: log);
            var failing = Door(door, log: log);
            failing.Runner.StreamLines = [];   // a turn that ends with no terminal logs its own warning

            var events = (await session.StreamAsync(Ask(home)).ToListAsync())
                .Concat(await failing.Session.StreamAsync(Ask(home)).ToListAsync()).ToList();

            Assert.Equal(before, Snapshot(home));
            var everythingSaid = string.Join('\n', events.Select(e => e.ToString()).Concat(log.Messages)
                .Concat(runner.Calls.Concat(failing.Runner.Calls).SelectMany(c => c.Args.Append(c.Stdin ?? ""))));
            Assert.DoesNotContain(Secret, everythingSaid, StringComparison.Ordinal);
            // the only thing the account put in the child's environment is the home path itself
            Assert.Equal(home, runner.LastEnvironment![variable]);
            Assert.Equal(new[] { "OPENAI_API_KEY", variable }.Order(), runner.LastEnvironment.Keys.Order());
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static List<(string Name, string Content)> Snapshot(string dir) =>
        [.. Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => (Path.GetRelativePath(dir, p), File.Exists(p) ? File.ReadAllText(p) : "<dir>"))];

    // ── an account that is a key: environment entries the provider declares it reads ──

    private static string KeyVariable(string door) => door == "claude" ? "ANTHROPIC_API_KEY" : "OPENAI_API_KEY";

    private static AgentSessionOptions AskAsKey(string door, string value, string? home = null) => new()
    {
        Prompt = "go",
        ToolPolicy = AgentToolPolicy.Write,
        Account = new AgentAccount
        {
            Provider = "p", Name = "gateway", Home = home,
            Environment = new Dictionary<string, string> { [KeyVariable(door)] = value },
        },
    };

    [Fact]
    public void Each_door_declares_the_variables_its_agent_reads_and_the_home_variable_is_one_of_them()
    {
        var claude = ClaudeAgentDoor.Describe("claude-cli").Accounts!;
        var codex = CodexAgentDoor.Describe("codex-cli").Accounts!;

        Assert.Equal("CLAUDE_CONFIG_DIR", claude.Variables[0]);
        Assert.Contains("ANTHROPIC_API_KEY", claude.Variables);
        Assert.Contains("ANTHROPIC_BASE_URL", claude.Variables);
        Assert.Equal("CODEX_HOME", codex.Variables[0]);
        Assert.Contains("OPENAI_API_KEY", codex.Variables);
        Assert.Empty(claude.Variables.Intersect(codex.Variables));
    }

    [Fact]
    public void A_mapping_drops_a_repeat_and_refuses_a_name_an_environment_cannot_carry()
    {
        Assert.Equal(["HOME_VAR", "KEY"], new AgentAccountMapping("HOME_VAR", ["KEY", "HOME_VAR", "KEY"]).Variables);
        Assert.Equal(["HOME_VAR"], new AgentAccountMapping("HOME_VAR").Variables);
        foreach (var bad in new[] { "", " ", "A=B", "A\0B" })
            Assert.Throws<ArgumentException>(() => new AgentAccountMapping("HOME_VAR", [bad]));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_key_account_reaches_the_child_and_leaves_the_sessions_own_home(string door)
    {
        var standing = new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = "portable/claude", ["CODEX_HOME"] = "portable/codex" };
        var (session, runner, home) = Door(door, standing);

        var events = await session.StreamAsync(AskAsKey(door, Secret)).ToListAsync();

        Assert.False(Assert.IsType<SessionEnded>(events[^1]).IsError);
        var environment = Assert.Single(runner.Calls).Environment!;
        Assert.Equal(Secret, environment[KeyVariable(door)]);
        Assert.Equal(standing[home], environment[home]);
    }

    [Fact]
    public async Task A_gateway_account_sets_an_address_and_blanks_the_key_another_variable_would_supply()
    {
        var (session, runner, _) = Door("claude");
        var gateway = new AgentAccount
        {
            Provider = "p", Name = "gateway",
            Environment = new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "https://gateway.example.invalid/api",
                ["ANTHROPIC_AUTH_TOKEN"] = Secret,
                ["ANTHROPIC_API_KEY"] = "",
            },
        };

        await session.StreamAsync(Ask() with { Account = gateway }).ToListAsync();

        var environment = Assert.Single(runner.Calls).Environment!;
        Assert.Equal("https://gateway.example.invalid/api", environment["ANTHROPIC_BASE_URL"]);
        Assert.Equal(Secret, environment["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal("", environment["ANTHROPIC_API_KEY"]);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_entry_the_provider_does_not_read_is_refused_before_a_spawn_without_saying_its_value(string door)
    {
        var other = door == "claude" ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
        var home = door == "claude" ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME";
        foreach (var name in new[] { other, "PATH", KeyVariable(door).ToLowerInvariant(), home })
        {
            var (session, runner, _) = Door(door);
            var options = AskAsKey(door, Secret) with
            {
                Account = new AgentAccount
                {
                    Provider = "p", Name = "gateway", Home = Home, Environment = new Dictionary<string, string> { [name] = Secret },
                },
            };

            var events = await session.StreamAsync(options).ToListAsync();

            var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
            Assert.Equal("account-invalid", ended.Subtype);
            Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
            Assert.Contains(name, ended.Diagnostic);
            Assert.DoesNotContain(Secret, ended.ToString() + ended.Diagnostic, StringComparison.Ordinal);
            Assert.Empty(runner.Calls);
        }
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_account_with_neither_a_home_nor_an_entry_and_a_value_with_a_control_character_are_refused(string door)
    {
        foreach (var account in new[]
                 {
                     new AgentAccount { Provider = "p", Name = "empty" },
                     new AgentAccount
                     {
                         Provider = "p", Name = "cut",
                         Environment = new Dictionary<string, string> { [KeyVariable(door)] = "sk-\0cut" },
                     },
                     new AgentAccount
                     {
                         Provider = "p", Name = "wrapped",
                         Environment = new Dictionary<string, string> { [KeyVariable(door)] = "sk-\nwrapped" },
                     },
                 })
        {
            var (session, runner, _) = Door(door);

            var events = await session.StreamAsync(Ask() with { Account = account }).ToListAsync();

            Assert.Equal("account-invalid", Assert.IsType<SessionEnded>(Assert.Single(events)).Subtype);
            Assert.Empty(runner.Calls);
        }
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_key_value_is_written_nowhere_a_host_can_read_back(string door)
    {
        var log = new CapturingLogger();
        var (session, runner, _) = Door(door, log: log);
        var failing = Door(door, log: log);
        failing.Runner.StreamLines = [];   // a turn that ends with no terminal logs its own warning
        var refused = Door(door, log: log);
        var options = AskAsKey(door, Secret);

        var events = (await session.StreamAsync(options).ToListAsync())
            .Concat(await failing.Session.StreamAsync(options).ToListAsync())
            .Concat(await refused.Session.StreamAsync(options with
            {
                Account = options.Account! with { Environment = new Dictionary<string, string> { ["NOT_READ"] = Secret } },
            }).ToListAsync()).ToList();

        var clock = new ManualTimeProvider();
        var store = new InMemoryKeyValueStore();
        var pool = new AgentAccountPool(PoolOf(options.Account!), store, clock);
        var pick = (await pool.PickAsync()).Pick!;
        await pool.ReportAsync(pick, new SessionEnded(ProviderVerdict.Failed, true, null, null, null, "limit")
        {
            Failure = new AgentFailure(AgentFailureKind.Limit, "test")
            {
                Restriction = new AgentRestriction(RestrictionCause.Quota, RestrictionSubject.Account, RestrictionRecovery.OwnerAction),
            },
        });
        var stored = new List<string>();
        foreach (var key in await store.ListKeysAsync()) stored.Add(key + "=" + await store.GetAsync(key));

        var everythingSaid = string.Join('\n', events.Select(e => e.ToString()).Concat(log.Messages).Concat(stored)
            .Concat(new object[] { options, options.Account!, pick.Seat, pick.Seat.Account }.Select(o => o.ToString()!))
            .Concat(runner.Calls.Concat(failing.Runner.Calls).Concat(refused.Runner.Calls)
                .SelectMany(c => c.Args.Append(c.Stdin ?? ""))));
        Assert.DoesNotContain(Secret, everythingSaid, StringComparison.Ordinal);
        Assert.Contains(KeyVariable(door), options.Account!.ToString());   // the names stay: a host reads which entries it set
        Assert.NotEmpty(stored);
        Assert.Equal(Secret, runner.LastEnvironment![KeyVariable(door)]);   // and the one place it goes is the child
    }

    private static AgentPoolOptions PoolOf(AgentAccount account)
    {
        var options = new AgentPoolOptions();
        options.Choices.Add(new AgentChoice { Provider = "p", Accounts = [account] });
        return options;
    }

    [Fact]
    public void Two_listings_of_one_account_agree_on_the_environment_and_a_refusal_names_no_value()
    {
        var one = new AgentAccount
        {
            Provider = "p", Name = "gateway", Environment = new Dictionary<string, string> { ["K"] = Secret },
        };
        var same = one with { Environment = new Dictionary<string, string> { ["K"] = Secret } };
        var differs = one with { Environment = new Dictionary<string, string> { ["K"] = Secret + "-2" } };

        var options = PoolOf(one);
        options.Choices.Add(new AgentChoice { Provider = "p", Accounts = [same] });
        _ = new AgentAccountPool(options, new InMemoryKeyValueStore(), new ManualTimeProvider());
        options.Choices.Add(new AgentChoice { Provider = "p", Accounts = [differs] });

        var thrown = Assert.Throws<ArgumentException>(
            () => new AgentAccountPool(options, new InMemoryKeyValueStore(), new ManualTimeProvider()));
        Assert.DoesNotContain(Secret, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("environments", thrown.Message);
    }

    [Fact]
    public void An_account_holds_strings_and_one_environment_and_its_text_lists_only_the_names()
    {
        var members = typeof(AgentAccount).GetProperties().Select(p => p.Name).Order();

        Assert.Equal(["Environment", "Home", "Name", "Provider"], members);
        Assert.Equal(typeof(IReadOnlyDictionary<string, string>), typeof(AgentAccount).GetProperty(nameof(AgentAccount.Environment))!.PropertyType);
        var text = new AgentAccount
        {
            Provider = "p", Name = "n", Home = "/h", Environment = new Dictionary<string, string> { ["B"] = Secret, ["A"] = Secret },
        }.ToString();
        Assert.Equal("AgentAccount { Provider = p, Name = n, Home = /h, Environment = [A, B] }", text);
    }
}
