using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The optional allowance read behind <see cref="IAgentUsageSource"/>, through the registered provider and a
/// recording runner: it is off unless a host switches it on, it spawns nothing it has not been asked for, a version
/// whose usage response is not validated never has its server started, every failure reads unknown, and a turn or a
/// pool pick never invokes it. Zero tokens; the real binaries are in <see cref="AgentUsageLiveTests"/>.</summary>
public class AgentUsageSourceTests
{
    public sealed record Door(string ProviderId, string Version, string SignedIn, string HomeVariable, string ServerMarker, string UnvalidatedVersion)
    {
        public string AskMarker => ProviderId == "claude-cli" ? "get_usage" : "account/rateLimits/read";

        // the versions as the library parses them out of what the binary prints
        public string ValidatedParsed => ProviderId == "claude-cli" ? "2.1.296" : "0.160.0";
        public string UnvalidatedParsed => ProviderId == "claude-cli" ? "2.1.312" : "0.163.0-alpha.10.2";

        // the answer a server gives to the first request; the question is written only once it has arrived
        public string InitializeAnswer => ProviderId == "claude-cli"
            ? UsageCaptures.ClaudeSignedOut_2_1_312[0]
            : UsageCaptures.CodexSignedOut_0_163[0];
    }

    private static IEnumerable<string>? Speaks(Door door, string sent, Func<IEnumerable<string>?> ask) =>
        sent.Contains("\"initialize\"") ? [door.InitializeAnswer] : sent.Contains(door.AskMarker) ? ask() : [];

    private static readonly Door ClaudeDoor = new("claude-cli", "2.1.296 (Claude Code)", UsageCaptures.ClaudeSignedIn_2_1_296, "CLAUDE_CONFIG_DIR", "--input-format", "2.1.312 (Claude Code)");
    private static readonly Door CodexDoor = new("codex-cli", "codex-cli 0.160.0", UsageCaptures.CodexSignedIn_0_160_0, "CODEX_HOME", "app-server", "codex-cli 0.163.0-alpha.10.2");

    public static TheoryData<Door> Doors() => [ClaudeDoor, CodexDoor];

    private const string Home = "/accounts/work-home";

    private static AgentAccount Account(string providerId, string? home = Home) =>
        new() { Provider = providerId, Name = "work", Home = home! };

    private static FakeProcessRunner Runner(Door door, string? version = null, params string[] answer) =>
        new()
        {
            RunHandler = (_, args) => args.Contains("--version") ? FakeProcessRunner.Ok(version ?? door.Version) : FakeProcessRunner.Ok(""),
            // the server speaks when it is asked the usage question, as the real ones do, and not before
            ExchangeHandler = sent => Speaks(door, sent, () => answer.Length > 0 ? answer : [door.SignedIn]),
        };

    private static ServiceProvider Compose(IProcessRunner runner, Action<LyntaiBuilder>? usage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runner);
        services.AddLyntai(b =>
        {
            b.AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex");
            usage?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    private static IAgentUsageSource Source(ServiceProvider sp, Door door) =>
        Assert.IsAssignableFrom<IAgentUsageSource>(sp.GetRequiredService<AgentProviderRegistry>().Get(door.ProviderId));

    // ── off unless switched on ────────────────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task Without_AddAgentUsageReading_a_read_is_unknown_disabled_and_spawns_nothing(Door door)
    {
        var runner = Runner(door);
        using var sp = Compose(runner, null);

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.Disabled, reading.UnknownReason);
        Assert.False(reading.IsKnown);
        Assert.Empty(runner.Calls);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_host_that_enables_it_and_sets_it_back_off_never_spawns(Door door)
    {
        var runner = Runner(door);
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.Enabled = false));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.Disabled, reading.UnknownReason);
        Assert.Empty(runner.Calls);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task Registering_the_ability_spawns_nothing_until_a_host_invokes_it(Door door)
    {
        var runner = Runner(door);
        using var sp = Compose(runner, b => b.AddAgentUsageReading());

        _ = Source(sp, door);
        _ = sp.GetRequiredService<AgentProviderRegistry>().All;

        Assert.Empty(runner.Calls);
        await Task.CompletedTask;
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_turn_and_a_pool_pick_never_invoke_it_even_when_it_is_enabled(Door door)
    {
        var runner = Runner(door);
        runner.StreamLines = ["""{"type":"result","subtype":"success","is_error":false,"result":"hi","session_id":"s"}"""];
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex")
            .AddAgentUsageReading()
            .AddAgentAccountPool(o => o.Choices.Add(new AgentChoice { Provider = door.ProviderId, Accounts = [Account(door.ProviderId)] })));
        await using var sp = services.BuildServiceProvider();

        var pool = sp.GetRequiredService<AgentAccountPool>();
        var pick = (await pool.PickAsync()).Pick!;
        var ended = await sp.GetRequiredService<AgentProviderRegistry>().Get(door.ProviderId).Open("cli")
            .StreamAsync(new AgentSessionOptions { Prompt = "go", Account = pick.Seat.Account }).ToListAsync();
        await pool.ReportAsync(pick, ended.OfType<SessionEnded>().Last());

        Assert.NotEmpty(runner.Calls);
        Assert.Empty(runner.Exchanges);
        Assert.DoesNotContain(runner.Calls, c => c.Args.Contains("--version"));
    }

    // ── a validated version, started under the account's home ─────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task A_validated_version_is_asked_its_version_then_started_under_the_account_home(Door door)
    {
        var runner = Runner(door);
        string? workDir = null;
        var existedAtSpawn = false;
        runner.OnSpawn = call =>
        {
            if (!call.Args.Contains(door.ServerMarker)) return;
            workDir = call.WorkingDirectory;
            existedAtSpawn = workDir is not null && Directory.Exists(workDir);
        };
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.Timeout = TimeSpan.FromSeconds(7)));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        Assert.Equal(door.ValidatedParsed, reading.Version);
        Assert.True(reading.VersionValidated);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["--version"], runner.Calls[0].Args);
        Assert.False(runner.Calls[0].Environment?.ContainsKey(door.HomeVariable) ?? false,
            "the version question is asked of no account's home");

        var exchange = Assert.Single(runner.Exchanges);
        Assert.Contains(door.ServerMarker, exchange.Args);
        Assert.Equal(Home, exchange.Environment![door.HomeVariable]);
        Assert.Equal(TimeSpan.FromSeconds(7), exchange.MaxDuration);
        Assert.Contains(runner.Exchanged, sent => sent.Contains(door.AskMarker));

        Assert.NotNull(workDir);
        Assert.True(existedAtSpawn, "the server starts in a real, fresh, empty directory");
        Assert.NotEqual(Directory.GetCurrentDirectory(), workDir);
        Assert.False(Directory.Exists(workDir), "the scratch directory is removed when the read ends");
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_version_whose_usage_response_is_not_validated_starts_no_server(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion);
        using var sp = Compose(runner, b => b.AddAgentUsageReading());

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.VersionNotValidated, reading.UnknownReason);
        Assert.Equal(door.UnvalidatedParsed, reading.Version);
        Assert.False(reading.VersionValidated);
        Assert.Empty(runner.Exchanges);
        Assert.Single(runner.Calls);
    }

    // ── a host's policy for a version the library has not validated ───────────────────────────────

    private sealed class HostPolicy(bool probes, Func<AgentUsageVersionQuery, bool> starts) : IAgentUsageVersionPolicy
    {
        public List<AgentUsageVersionQuery> Asked { get; } = [];
        public bool ProbesVersion => probes;

        public bool ShouldStart(AgentUsageVersionQuery query)
        {
            Asked.Add(query);
            return starts(query);
        }
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_host_policy_naming_the_unvalidated_version_starts_the_server_and_the_reading_says_so(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion);
        var policy = new HostPolicy(true, q => q.Version == door.UnvalidatedParsed);
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = policy));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        Assert.Equal(door.UnvalidatedParsed, reading.Version);
        Assert.False(reading.VersionValidated);
        Assert.Single(runner.Exchanges);

        var asked = Assert.Single(policy.Asked);
        Assert.Equal(door.ProviderId, asked.Provider);
        Assert.Equal(Home, asked.Account.Home);
        Assert.Equal(door.UnvalidatedParsed, asked.Version);
        Assert.False(asked.Validated);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_host_policy_that_declines_reads_not_validated_and_starts_nothing(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion);
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = new HostPolicy(true, _ => false)));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.VersionNotValidated, reading.UnknownReason);
        Assert.Equal(door.UnvalidatedParsed, reading.Version);
        Assert.Empty(runner.Exchanges);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_host_policy_is_told_when_the_library_validated_the_version(Door door)
    {
        var policy = new HostPolicy(true, _ => true);
        using var sp = Compose(Runner(door), b => b.AddAgentUsageReading(o => o.VersionPolicy = policy));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(Assert.Single(policy.Asked).Validated);
        Assert.True(reading.VersionValidated);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task AnyVersion_starts_the_server_with_no_version_probe_and_names_no_version(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion);
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = AgentUsageVersionPolicies.AnyVersion));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        Assert.Null(reading.Version);
        Assert.Null(reading.VersionValidated);
        Assert.DoesNotContain(runner.Calls, c => c.Args.Contains("--version"));
        Assert.Single(runner.Exchanges);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_policy_that_does_not_probe_is_asked_with_no_version(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion);
        var policy = new HostPolicy(false, _ => true);
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = policy));

        await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        var asked = Assert.Single(policy.Asked);
        Assert.Null(asked.Version);
        Assert.False(asked.Validated);
        Assert.DoesNotContain(runner.Calls, c => c.Args.Contains("--version"));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_policy_that_throws_refuses_as_not_validated_and_never_throws(Door door)
    {
        foreach (var throwing in new IAgentUsageVersionPolicy[]
                 {
                     new ThrowingPolicy(onProbes: true),
                     new ThrowingPolicy(onProbes: false),
                 })
        {
            var runner = Runner(door, door.UnvalidatedVersion);
            using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = throwing));

            var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

            Assert.Equal(AgentAllowanceUnknown.VersionNotValidated, reading.UnknownReason);
            Assert.Empty(runner.Exchanges);
        }
    }

    private sealed class ThrowingPolicy(bool onProbes) : IAgentUsageVersionPolicy
    {
        public bool ProbesVersion => onProbes ? throw new InvalidOperationException("the host's policy failed") : true;
        public bool ShouldStart(AgentUsageVersionQuery query) => throw new InvalidOperationException("the host's policy failed");
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_version_that_cannot_be_read_is_unreadable_under_a_probing_policy_and_the_policy_is_not_asked(Door door)
    {
        var policy = new HostPolicy(true, _ => true);
        var runner = Runner(door, "no version here");
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = policy));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.VersionUnreadable, reading.UnknownReason);
        Assert.Null(reading.Version);
        Assert.Empty(policy.Asked);
        Assert.Empty(runner.Exchanges);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_answer_the_reader_does_not_recognise_is_unknown_under_AnyVersion_too(Door door)
    {
        var runner = Runner(door, door.UnvalidatedVersion, door.ProviderId == "claude-cli"
            ? """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true}}}"""
            : """{"id":2,"result":{"somethingNew":{}}}""");
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.VersionPolicy = AgentUsageVersionPolicies.AnyVersion));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.Unrecognised, reading.UnknownReason);
        Assert.False(reading.IsKnown);
    }

    [Theory, MemberData(nameof(Doors))]
    public void The_descriptor_is_the_same_with_and_without_a_policy(Door door)
    {
        string Described(Action<AgentUsageReadingOptions>? configure)
        {
            using var sp = Compose(Runner(door), b => { if (configure is not null) b.AddAgentUsageReading(configure); });
            return System.Text.Json.JsonSerializer.Serialize(sp.GetRequiredService<AgentProviderRegistry>().Get(door.ProviderId).Describe());
        }

        var without = Described(null);
        Assert.Equal(without, Described(o => { }));
        Assert.Equal(without, Described(o => o.VersionPolicy = AgentUsageVersionPolicies.AnyVersion));
        Assert.Equal(without, Described(o => o.VersionPolicy = new HostPolicy(true, _ => true)));
    }

    [Fact]
    public void The_version_policy_defaults_to_validated_only_and_a_null_one_is_refused_where_it_is_set()
    {
        Assert.Same(AgentUsageVersionPolicies.ValidatedOnly, new AgentUsageReadingOptions().VersionPolicy);
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.VersionPolicy = null!)));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_version_that_cannot_be_read_starts_no_server(Door door)
    {
        foreach (var output in new[] { "", "no version here" })
        {
            var runner = Runner(door, output);
            using var sp = Compose(runner, b => b.AddAgentUsageReading());

            var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

            Assert.Equal(AgentAllowanceUnknown.VersionUnreadable, reading.UnknownReason);
            Assert.Empty(runner.Exchanges);
        }

        var failing = Runner(door);
        failing.RunHandler = (_, _) => new ProcessResult(1, "", "boom");
        using var failed = Compose(failing, b => b.AddAgentUsageReading());
        Assert.Equal(AgentAllowanceUnknown.VersionUnreadable,
            (await Source(failed, door).ReadAllowanceAsync(Account(door.ProviderId))).UnknownReason);
        Assert.Empty(failing.Exchanges);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_binary_that_cannot_be_started_reads_unknown_and_never_throws(Door door)
    {
        var missing = Runner(door);
        missing.RunHandler = (_, _) => throw new System.ComponentModel.Win32Exception("not found");
        using var noVersion = Compose(missing, b => b.AddAgentUsageReading());
        Assert.Equal(AgentAllowanceUnknown.SpawnFailed,
            (await Source(noVersion, door).ReadAllowanceAsync(Account(door.ProviderId))).UnknownReason);

        var runner = Runner(door);
        runner.ExchangeHandler = _ => throw new InvalidOperationException("failed to start");
        using var sp = Compose(runner, b => b.AddAgentUsageReading());
        Assert.Equal(AgentAllowanceUnknown.SpawnFailed,
            (await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId))).UnknownReason);
    }

    // ── what the server does instead of answering ─────────────────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task A_server_that_ends_or_runs_out_of_time_without_an_answer_reads_unknown(Door door)
    {
        var silent = Runner(door);
        silent.ExchangeHandler = sent => Speaks(door, sent, () => null);   // the server ends without answering
        using var spA = Compose(silent, b => b.AddAgentUsageReading());
        Assert.Equal(AgentAllowanceUnknown.NoAnswer, (await Source(spA, door).ReadAllowanceAsync(Account(door.ProviderId))).UnknownReason);

        var slow = Runner(door);
        slow.ExchangeHandler = _ => throw new ProcessTimeoutException("server", TimeSpan.FromSeconds(30));
        using var spB = Compose(slow, b => b.AddAgentUsageReading());
        Assert.Equal(AgentAllowanceUnknown.TimedOut, (await Source(spB, door).ReadAllowanceAsync(Account(door.ProviderId))).UnknownReason);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task An_answer_that_does_not_match_reads_unknown_and_the_scratch_directory_is_still_removed(Door door)
    {
        string? workDir = null;
        var runner = Runner(door, answer: door.ProviderId == "claude-cli"
            ? """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"rate_limits_available":true}}}"""
            : """{"id":2,"result":{"somethingNew":{}}}""");
        runner.OnSpawn = call => { if (call.Args.Contains(door.ServerMarker)) workDir = call.WorkingDirectory; };
        using var sp = Compose(runner, b => b.AddAgentUsageReading());

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.Unrecognised, reading.UnknownReason);
        Assert.False(Directory.Exists(workDir));
    }

    // ── where the server runs, and what it is told of its client ──────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task A_named_working_directory_is_created_reached_and_left_with_what_it_held(Door door)
    {
        using var scratch = new ScratchDir("usage-workdir");
        var named = scratch.Combine("project");
        var runner = Runner(door);
        string? reached = null;
        var existedAtSpawn = false;
        runner.OnSpawn = call =>
        {
            if (!call.Args.Contains(door.ServerMarker)) return;
            reached = call.WorkingDirectory;
            existedAtSpawn = Directory.Exists(named);
        };
        AgentAccount? asked = null;
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.WorkingDirectory = account => { asked = account; return named; }));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        Assert.Equal(named, reached);
        Assert.True(existedAtSpawn, "an absent folder is created before the server starts in it");
        Assert.Equal(Home, asked?.Home);
        Assert.True(Directory.Exists(named), "a named folder is the host's and is never removed");

        File.WriteAllText(Path.Combine(named, "state.json"), "kept");
        Assert.True((await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId))).IsKnown);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(named, "state.json")));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_working_directory_that_answers_null_keeps_the_fresh_folder_removed_after_the_read(Door door)
    {
        var runner = Runner(door);
        string? workDir = null;
        runner.OnSpawn = call => { if (call.Args.Contains(door.ServerMarker)) workDir = call.WorkingDirectory; };
        using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.WorkingDirectory = _ => null));

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.True(reading.IsKnown, reading.UnknownReason);
        Assert.NotNull(workDir);
        Assert.False(Directory.Exists(workDir));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_working_directory_that_is_not_fully_qualified_or_cannot_be_chosen_is_refused_before_any_spawn(Door door)
    {
        foreach (var choose in new Func<AgentAccount, string?>[]
                 {
                     _ => "relative/project",
                     _ => "",
                     _ => " ",
                     _ => "project",
                     _ => Path.Combine(Path.GetTempPath(), "cut\0here"),
                     _ => throw new InvalidOperationException("the host's function failed"),
                 })
        {
            var runner = Runner(door);
            using var sp = Compose(runner, b => b.AddAgentUsageReading(o => o.WorkingDirectory = choose));

            var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

            Assert.Equal(AgentAllowanceUnknown.AccountRefused, reading.UnknownReason);
            Assert.Empty(runner.Calls);
        }
    }

    [Fact]
    public async Task Codex_is_told_the_client_the_host_names_and_claudes_request_does_not_carry_it()
    {
        var (codex, claude) = (CodexDoor, ClaudeDoor);

        var named = Runner(codex);
        using (var sp = Compose(named, b => b.AddAgentUsageReading(o => { o.ClientName = "host-app"; o.ClientVersion = "9.9"; })))
            Assert.True((await Source(sp, codex).ReadAllowanceAsync(Account("codex-cli"))).IsKnown);
        var initialize = Assert.Single(named.Exchanged, line => line.Contains("\"initialize\""));
        Assert.Contains("\"clientInfo\":{\"name\":\"host-app\",\"version\":\"9.9\"}", initialize);

        var standing = Runner(claude);
        var renamed = Runner(claude);
        using (var spStanding = Compose(standing, b => b.AddAgentUsageReading()))
            await Source(spStanding, claude).ReadAllowanceAsync(Account("claude-cli"));
        using (var spRenamed = Compose(renamed, b => b.AddAgentUsageReading(o => { o.ClientName = "host-app"; o.ClientVersion = "9.9"; })))
            await Source(spRenamed, claude).ReadAllowanceAsync(Account("claude-cli"));
        Assert.Equal(standing.Exchanged, renamed.Exchanged);
        Assert.DoesNotContain(renamed.Exchanged, line => line.Contains("host-app"));
    }

    [Fact]
    public void The_client_defaults_to_what_the_library_always_said_and_a_bad_one_is_refused_where_it_is_set()
    {
        var options = new AgentUsageReadingOptions();
        Assert.Equal("lyntai", options.ClientName);
        Assert.Equal("1", options.ClientVersion);
        Assert.Null(options.WorkingDirectory);

        foreach (var bad in new[] { "", "two\nlines", "tab\there", "nul\0", new string('x', 65) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.ClientName = bad)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.ClientVersion = bad)));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.ClientName = null!)));

        // a quote, a backslash and non-ASCII text are legal: the JSON writer carries them
        new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o =>
        {
            o.ClientName = "the \"host\" \\ 灵台";
            o.ClientVersion = new string('v', 64);
        }));
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task A_runner_that_cannot_hold_a_process_open_reads_unknown(Door door)
    {
        using var sp = Compose(new RunOnlyRunner(door.Version), b => b.AddAgentUsageReading());

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(AgentAllowanceUnknown.RunnerUnsupported, reading.UnknownReason);
    }

    private sealed class RunOnlyRunner(string version) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) =>
            Task.FromResult(FakeProcessRunner.Ok(version));

        public async IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,
            string? stdin = null, TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    // ── the account, and cancellation ─────────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task An_account_of_another_provider_or_without_a_usable_home_is_refused_before_any_spawn(Door door)
    {
        var runner = Runner(door);
        using var sp = Compose(runner, b => b.AddAgentUsageReading());
        var source = Source(sp, door);

        foreach (var account in new[]
                 {
                     Account("other-cli"),
                     Account(door.ProviderId, " "),
                     Account(door.ProviderId, "/home\0cut"),
                     Account(door.ProviderId) with { Name = "" },
                     // a key has no signed-in allowance to read, and the read starts no child under one
                     Account(door.ProviderId, null) with { Environment = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "k" } },
                     Account(door.ProviderId) with { Environment = new Dictionary<string, string> { ["OPENAI_API_KEY"] = "k" } },
                 })
            Assert.Equal(AgentAllowanceUnknown.AccountRefused, (await source.ReadAllowanceAsync(account)).UnknownReason);

        Assert.Empty(runner.Calls);
    }

    [Theory, MemberData(nameof(Doors))]
    public async Task Cancellation_propagates_and_is_not_read_as_unknown(Door door)
    {
        var before = Runner(door);
        using var spA = Compose(before, b => b.AddAgentUsageReading());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Source(spA, door).ReadAllowanceAsync(Account(door.ProviderId), cancelled.Token));
        Assert.Empty(before.Calls);

        var during = Runner(door);
        during.ExchangeHandler = _ => throw new OperationCanceledException();
        using var spB = Compose(during, b => b.AddAgentUsageReading());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Source(spB, door).ReadAllowanceAsync(Account(door.ProviderId)));
    }

    // ── what a host gets ──────────────────────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Doors))]
    public async Task The_reading_feeds_the_pools_report_without_a_conversion(Door door)
    {
        var runner = Runner(door);
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex")
            .AddAgentUsageReading()
            .AddAgentAccountPool(o => o.Choices.Add(new AgentChoice { Provider = door.ProviderId, Accounts = [Account(door.ProviderId)] })));
        await using var sp = services.BuildServiceProvider();

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));
        var lifted = await sp.GetRequiredService<AgentAccountPool>()
            .ReportReadingAsync(Account(door.ProviderId), reading.MeasuredAt, reading.HasRoom);

        Assert.Equal(0, lifted); // nothing was held; the point is that the reading's two fields are the report's two inputs
    }

    // the pool lifts a hold only for a reading measured after it, so both must read the container's clock
    [Theory, MemberData(nameof(Doors))]
    public async Task A_reading_is_stamped_by_the_clock_the_container_registers(Door door)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(Runner(door));
        services.AddSingleton<TimeProvider>(clock);
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex")
            .AddAgentUsageReading());
        await using var sp = services.BuildServiceProvider();

        var reading = await Source(sp, door).ReadAllowanceAsync(Account(door.ProviderId));

        Assert.Equal(clock.GetUtcNow(), reading.MeasuredAt);
    }

    [Theory, MemberData(nameof(Doors))]
    public void The_default_timeout_is_bounded_and_a_bad_one_is_refused_where_it_is_set(Door door)
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new AgentUsageReadingOptions().Timeout);
        Assert.False(new AgentUsageReadingOptions().Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.Timeout = TimeSpan.Zero)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddLyntai(b => b.AddAgentUsageReading(o => o.Timeout = TimeSpan.FromMinutes(6))));
        Assert.NotNull(door);
    }
}
