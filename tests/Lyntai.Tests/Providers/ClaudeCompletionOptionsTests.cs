using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>How a one-shot claude completion is spawned per CONSUMER (<see cref="ClaudeCliBackend.CompletionByConsumer"/>):
/// the consumer's entry, else <c>"default"</c>, else today's argv — resolved as every <c>*ByConsumer</c> map is.</summary>
public class ClaudeCompletionOptionsTests
{
    private static readonly string[] TodaysArgv =
        ["-p", "--output-format", "stream-json", "--verbose", "--disallowed-tools", "AskUserQuestion"];

    private static TextRequest Ask(string consumer = ProviderConsumers.Default) =>
        new() { Messages = [TextMessage.User("hi")], Consumer = consumer };

    private static ClaudeCliBackend Backend(params (string Consumer, ClaudeCompletionOptions Options)[] entries) =>
        new() { CompletionByConsumer = entries.ToDictionary(e => e.Consumer, e => e.Options) };

    private static string[] Denied(IReadOnlyList<string> argv)
    {
        var at = argv.ToList().IndexOf("--disallowed-tools");
        Assert.True(at >= 0, "--disallowed-tools expected");
        // whole NAMES: a substring check on the joined value is satisfied by a longer tool's name
        return argv[at + 1].Split(',');
    }

    [Fact]
    public void A_consumers_disallowed_tools_are_denied_ALONGSIDE_AskUserQuestion()
    {
        var backend = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["PowerShell", "Monitor"] }));

        var argv = backend.BuildCompletionArgs(Ask("scorer"), []);

        Assert.Equal(["AskUserQuestion", "PowerShell", "Monitor"], Denied(argv));
        Assert.Single(argv, a => a == "--disallowed-tools");
    }

    [Fact]
    public void A_consumer_with_no_entry_takes_the_default_entry_and_the_key_ignores_case()
    {
        var backend = Backend(
            ("SCORER", new ClaudeCompletionOptions { DisallowedTools = ["Bash"] }),
            (ProviderConsumers.Default, new ClaudeCompletionOptions { DisallowedTools = ["PowerShell"] }));

        Assert.Equal(["AskUserQuestion", "Bash"], Denied(backend.BuildCompletionArgs(Ask("scorer"), [])));
        Assert.Equal(["AskUserQuestion", "PowerShell"], Denied(backend.BuildCompletionArgs(Ask("memory"), [])));
    }

    [Fact]
    public void With_no_entry_and_no_default_the_argv_is_byte_identical_to_todays()
    {
        var configured = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["Bash"] }));

        Assert.Equal(TodaysArgv, configured.BuildCompletionArgs(Ask("memory"), []));
        Assert.Equal(TodaysArgv, new ClaudeCliBackend().BuildCompletionArgs(Ask("scorer"), []));
        Assert.Equal(TodaysArgv, Backend(("scorer", new ClaudeCompletionOptions())).BuildCompletionArgs(Ask("scorer"), []));
    }

    [Fact]
    public void A_caller_adds_denials_and_never_removes_AskUserQuestion()
    {
        var backend = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["AskUserQuestion", "Bash", "Bash"] }));

        Assert.Equal(["AskUserQuestion", "Bash"], Denied(backend.BuildCompletionArgs(Ask("scorer"), [])));
    }

    [Fact]
    public void A_null_entry_or_list_is_refused_when_the_backend_is_configured_naming_its_consumer()
    {
        var entry = Assert.Throws<ArgumentException>(() => new ClaudeCliBackend
        {
            CompletionByConsumer = new Dictionary<string, ClaudeCompletionOptions> { ["scorer"] = null! },
        });
        Assert.Contains("scorer", entry.Message);

        Assert.Throws<ArgumentNullException>(() => new ClaudeCompletionOptions { DisallowedTools = null! });
    }

    [Fact]
    public void The_map_is_COPIED_so_a_later_edit_to_the_callers_dictionary_is_never_read()
    {
        var map = new Dictionary<string, ClaudeCompletionOptions>();
        var backend = new ClaudeCliBackend { CompletionByConsumer = map };

        map["scorer"] = new ClaudeCompletionOptions { DisallowedTools = ["Bash"] };

        Assert.Equal(TodaysArgv, backend.BuildCompletionArgs(Ask("scorer"), []));
    }

    [Fact]
    public void Setting_sources_and_strict_mcp_config_are_emitted_only_when_set()
    {
        var backend = Backend(
            ("scorer", new ClaudeCompletionOptions { SettingSources = ["project"], StrictMcpConfig = true }),
            ("judge", new ClaudeCompletionOptions { SettingSources = ["user", "local"] }),
            ("bare", new ClaudeCompletionOptions { SettingSources = [] }));

        var scorer = backend.BuildCompletionArgs(Ask("scorer"), []).ToList();
        Assert.Equal("project", scorer[scorer.IndexOf("--setting-sources") + 1]);
        Assert.Contains("--strict-mcp-config", scorer);

        var judge = backend.BuildCompletionArgs(Ask("judge"), []).ToList();
        Assert.Equal("user,local", judge[judge.IndexOf("--setting-sources") + 1]);
        Assert.DoesNotContain("--strict-mcp-config", judge);

        // measured: an EMPTY value is accepted and loads no source at all, a command-line settings file excepted
        var bare = backend.BuildCompletionArgs(Ask("bare"), []).ToList();
        Assert.Equal("", bare[bare.IndexOf("--setting-sources") + 1]);

        Assert.Equal(TodaysArgv, Backend(("x", new ClaudeCompletionOptions())).BuildCompletionArgs(Ask("x"), []));
    }

    [Fact]
    public void A_consumers_settings_file_is_handed_over_as_a_command_line_settings_file()
    {
        // a command-line settings file outranks the project and local scopes, and applies under every source
        var backend = Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = "/app/data/one-shot.json" }));

        var argv = backend.BuildCompletionArgs(Ask("scorer"), []).ToList();

        Assert.Equal("/app/data/one-shot.json", argv[argv.IndexOf("--settings") + 1]);
        Assert.Single(argv, a => a == "--settings");
        Assert.DoesNotContain("--settings", backend.BuildCompletionArgs(Ask("chat"), []));
        Assert.Equal(TodaysArgv, Backend(("x", new ClaudeCompletionOptions { SettingsPath = "" }))
            .BuildCompletionArgs(Ask("x"), []));
    }

    // ── a HOSTED call: measured on 2.1.285, the CLI applies only the LAST --settings, the earlier file dropped whole ──

    /// <summary>The args the claude tool host hands a call, over files written into <paramref name="dir"/>.</summary>
    private static async Task<IReadOnlyList<string>> HostArgs(ScratchDir dir) =>
        await new ClaudeCliMcpConnector().BuildArgsAsync(new McpCliContext(
            new McpEndpoint("http://127.0.0.1:1234/mcp", "sekret", "lyntai"),
            (kind, content) => dir.File($"{kind}.json", content)));

    private static string SettingsOf(IReadOnlyList<string> args) => args[args.ToList().IndexOf("--settings") + 1];

    private static string[] Strings(JsonNode? list) => [.. list!.AsArray().Select(n => (string)n!)];

    [Fact]
    public async Task A_hosted_consumer_is_handed_ONE_settings_file_carrying_its_own_keys_and_the_hosts_allow_list()
    {
        using var dir = new ScratchDir("claude-settings");
        const string mine = """{"apiKeyHelper":"","disableSkillShellExecution":true,"permissions":{"allow":["Read"],"deny":["Bash"]}}""";
        var consumer = dir.File("one-shot.json", mine);
        var host = await HostArgs(dir);

        var argv = Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = consumer }))
            .BuildCompletionArgs(Ask("scorer"), host);

        Assert.Single(argv, a => a == "--settings");
        // the host's own per-call file, which its session deletes — no second file for anyone to clean up
        Assert.Equal(SettingsOf(host), SettingsOf(argv));
        var applied = JsonNode.Parse(File.ReadAllText(SettingsOf(argv)))!;
        Assert.Equal("", (string?)applied["apiKeyHelper"]);
        Assert.True((bool?)applied["disableSkillShellExecution"]);
        Assert.Equal(["Read", "mcp__lyntai__*"], Strings(applied["permissions"]!["allow"]));
        Assert.Equal(["Bash"], Strings(applied["permissions"]!["deny"]));
        Assert.Equal(mine, File.ReadAllText(consumer));   // the consumer's own file is only read
    }

    [Fact]
    public void Where_the_two_files_disagree_the_consumers_value_stands_and_a_list_is_joined_without_repeats()
    {
        using var dir = new ScratchDir("claude-settings");
        var consumer = dir.File("one-shot.json",
            """{"model":"haiku","permissions":{"allow":["mcp__lyntai__*","Read"],"defaultMode":"plan"},"env":"x"}""");
        var hostFile = dir.File("host.json",
            """{"model":"opus","permissions":{"allow":["mcp__lyntai__*","Grep"],"defaultMode":"default"},"env":{"A":"1"},"hooks":{}}""");

        var argv = Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = consumer }))
            .BuildCompletionArgs(Ask("scorer"), ["--settings", hostFile]);

        Assert.Equal(hostFile, SettingsOf(argv));
        var applied = JsonNode.Parse(File.ReadAllText(hostFile))!;
        Assert.Equal("haiku", (string?)applied["model"]);
        Assert.Equal("plan", (string?)applied["permissions"]!["defaultMode"]);
        Assert.Equal(["mcp__lyntai__*", "Read", "Grep"], Strings(applied["permissions"]!["allow"]));
        Assert.Equal("x", (string?)applied["env"]);        // a clash of KINDS is a clash: the consumer's value stands
        Assert.NotNull(applied["hooks"]);                  // a key only the host has is carried over
    }

    [Theory]
    [InlineData("missing")]      // measured alone: "Settings file not found", exit 1 before any turn
    [InlineData("malformed")]    // measured alone: not refused, the call goes ahead
    [InlineData("array")]
    [InlineData("duplicate")]    // a JSON reader may keep either value; the CLI's own reader decides
    [InlineData("relative")]     // resolves against the CLI's working directory, never this process's
    public async Task A_consumers_file_that_cannot_be_merged_is_handed_LAST_so_the_CLI_treats_it_as_with_no_host(string kind)
    {
        using var dir = new ScratchDir("claude-settings");
        var consumer = kind switch
        {
            "missing" => dir.Combine("absent.json"),
            "malformed" => dir.File("bad.json", "{not json"),
            "array" => dir.File("list.json", "[1]"),
            "duplicate" => dir.File("twice.json", """{"permissions":{"allow":["Read"],"allow":["Grep"]}}"""),
            _ => "one-shot.json",
        };
        var host = await HostArgs(dir);
        var allowList = File.ReadAllText(SettingsOf(host));

        var argv = Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = consumer }))
            .BuildCompletionArgs(Ask("scorer"), host);

        Assert.Equal(["--settings", consumer], argv.TakeLast(2));
        Assert.Equal(allowList, File.ReadAllText(SettingsOf(host)));   // left as the host wrote it
    }

    [Fact]
    public void A_host_settings_value_that_is_not_a_readable_file_leaves_the_consumers_file_handed_last()
    {
        using var dir = new ScratchDir("claude-settings");
        var consumer = dir.File("one-shot.json", """{"disableSkillShellExecution":true}""");
        const string inline = """{"permissions":{"allow":["mcp__lyntai__*"]}}""";   // the flag also takes a JSON string

        var argv = Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = consumer }))
            .BuildCompletionArgs(Ask("scorer"), ["--settings", inline]);

        Assert.Equal(["--settings", inline, "--settings", consumer], argv.TakeLast(4));
    }

    [Fact]
    public async Task A_hosted_call_with_no_settings_file_of_its_own_is_spawned_as_it_always_was()
    {
        using var dir = new ScratchDir("claude-settings");
        var host = await HostArgs(dir);

        Assert.Equal([.. TodaysArgv, .. host], new ClaudeCliBackend().BuildCompletionArgs(Ask(), host));
        Assert.Equal(ClaudeCliMcpConnector.SettingsJson("lyntai"), File.ReadAllText(SettingsOf(host)));
    }

    [Fact]
    public async Task Through_the_tool_host_the_spawn_applies_ONE_settings_file_holding_the_consumers_keys_and_the_allow_list()
    {
        using var dir = new ScratchDir("claude-settings");
        var consumer = dir.File("one-shot.json", """{"disableSkillShellExecution":true}""");
        var handed = new List<string>();
        JsonNode? applied = null;
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok("""{"type":"result","result":"ok"}""") };
        // read while the process would run: the host deletes its files when the call ends
        runner.OnSpawn = call =>
        {
            handed.AddRange(call.Args.Where(a => a == "--settings"));
            applied = JsonNode.Parse(File.ReadAllText(SettingsOf(call.Args)));
        };
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b
            .AddClaudeCliProvider(Backend(("scorer", new ClaudeCompletionOptions { SettingsPath = consumer })),
                command: "claude")
            .AddMcpToolHost(new ClaudeCliMcpConnector())
            .AddTool(_ => new FunctionTool("echo", (a, _) => Task.FromResult(a)))
            .UseDefaultCandidates(ClaudeCliProvider.ProviderId));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask("scorer"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(handed);
        Assert.True((bool?)applied!["disableSkillShellExecution"]);
        Assert.Equal(["mcp__lyntai__*"], Strings(applied["permissions"]!["allow"]));
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("--settings")]
    [InlineData("project,user")]
    [InlineData("pro ject")]
    [InlineData("")]
    public void A_setting_source_the_CLI_would_misread_is_refused_when_set(string source) =>
        Assert.Throws<ArgumentException>(() => new ClaudeCompletionOptions { SettingSources = [source] });

    [Fact]
    public void An_unknown_source_NAME_is_left_to_the_CLI_which_refuses_it_before_any_turn() =>
        // measured on 2.1.285: "Invalid setting source: projcet. Valid options are: user, project, local", exit 1
        Assert.Equal(["projcet"], new ClaudeCompletionOptions { SettingSources = ["projcet"] }.SettingSources!);

    [Fact]
    public async Task The_provider_spawns_each_call_with_ITS_consumers_options()
    {
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok("""{"type":"result","result":"ok"}""") };
        var provider = new ClaudeCliProvider(runner, new LyntaiOptions(),
            Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["PowerShell"] })), command: "claude");

        await provider.CompleteAsync(Ask("scorer"));
        var scorer = runner.LastArgs!;
        await provider.CompleteAsync(Ask("chat"));

        Assert.Equal(["AskUserQuestion", "PowerShell"], Denied(scorer));
        Assert.Equal(TodaysArgv, runner.LastArgs);
    }

    [Fact]
    public async Task The_registration_overload_hands_the_configured_backend_to_the_provider()
    {
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok("""{"type":"result","result":"ok"}""") };
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b
            .AddClaudeCliProvider(Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["Monitor"] })),
                command: "claude")
            .UseDefaultCandidates(ClaudeCliProvider.ProviderId));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask("scorer"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal(["AskUserQuestion", "Monitor"], Denied(runner.LastArgs!));
    }
}
