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
    }

    public static TheoryData<Door> Doors() =>
    [
        new Door("claude-cli", "2.1.296 (Claude Code)", UsageCaptures.ClaudeSignedIn_2_1_296, "CLAUDE_CONFIG_DIR", "--input-format", "2.1.312 (Claude Code)"),
        new Door("codex-cli", "codex-cli 0.160.0", UsageCaptures.CodexSignedIn_0_160_0, "CODEX_HOME", "app-server", "codex-cli 0.163.0-alpha.10.2"),
    ];

    private const string Home = "/accounts/work-home";

    private static AgentAccount Account(string providerId, string? home = Home) =>
        new() { Provider = providerId, Name = "work", Home = home! };

    private static FakeProcessRunner Runner(Door door, string? version = null, params string[] answer) =>
        new()
        {
            RunHandler = (_, args) => args.Contains("--version") ? FakeProcessRunner.Ok(version ?? door.Version) : FakeProcessRunner.Ok(""),
            // the server speaks when it is asked the usage question, as the real ones do, and not before
            ExchangeHandler = sent => sent.Contains(door.AskMarker) ? (answer.Length > 0 ? answer : [door.SignedIn]) : [],
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
        Assert.Empty(runner.Exchanges);
        Assert.Single(runner.Calls);
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
        silent.ExchangeHandler = sent => sent.Contains(door.AskMarker) ? null : [];   // the server ends without answering
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
