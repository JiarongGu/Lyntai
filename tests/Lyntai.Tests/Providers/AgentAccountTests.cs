using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
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
        Assert.Equal("tok", runner.LastEnvironment["LYNTAI_MCP_BEARER_REMOTE"]);
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

    [Fact]
    public void An_account_holds_a_name_and_a_home_and_nothing_a_secret_could_ride_in()
    {
        var members = typeof(AgentAccount).GetProperties().Select(p => p.Name).Order();

        Assert.Equal(["Home", "Name", "Provider"], members);
        Assert.Equal([typeof(string)], typeof(AgentAccount).GetProperties().Select(p => p.PropertyType).Distinct());
    }
}
