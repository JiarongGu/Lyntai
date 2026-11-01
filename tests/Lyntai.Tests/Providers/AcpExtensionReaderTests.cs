using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// The seam that teaches the ACP door a third agent's extensions. Every capture here is a STAND-IN: an invented agent,
/// "acme", whose keys (<c>acme/state</c>, <c>data.acmeFault</c>, <c>_meta.acme</c>) no shipped reader knows and no
/// real agent speaks, so the tests prove the REGISTRATION and the narrowing and nothing about a real agent's wire.
/// The shipped readers' own vocabulary is held by <see cref="AcpAgentSessionTests"/>, unchanged.
/// </summary>
public class AcpExtensionReaderTests
{
    private static readonly DateTimeOffset ResetsAt = DateTimeOffset.FromUnixTimeSeconds(1893456000);

    /// <summary>The stand-in agent's reader: it holds a throttle notice, clears it on a recovery, reads its own fault
    /// code and its own verdict on a turn it ended normally.</summary>
    private sealed class AcmeReader : IAcpExtensionReader
    {
        public AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time)
        {
            if (!update.TryGetProperty("_meta", out var meta) || !meta.TryGetProperty("acme/state", out var state)) return null;
            return state.GetProperty("name").GetString() switch
            {
                "throttled" => new AcpUpdateReading(
                    [new Thinking("acme is throttled")],
                    new AgentFailure(AgentFailureKind.Limit, "acme.state=throttled")
                    {
                        Restriction = new(RestrictionCause.Rate, RestrictionSubject.Account,
                            RestrictionRecovery.RetryNotBefore(DateTimeOffset.FromUnixTimeSeconds(state.GetProperty("until").GetInt64()))),
                    }),
                "recovered" => new AcpUpdateReading(),
                _ => null,
            };
        }

        public AgentFailure? ReadError(JsonElement error, TimeProvider time) =>
            error.TryGetProperty("data", out var data) && data.TryGetProperty("acmeFault", out var fault)
                ? fault.GetString() switch
                {
                    "quota_gone" => new AgentFailure(AgentFailureKind.Limit, "acme.fault=quota_gone")
                        { Restriction = new(RestrictionCause.Quota, RestrictionSubject.Account, RestrictionRecovery.ResetAt(ResetsAt)) },
                    "key_revoked" => new AgentFailure(AgentFailureKind.Access, "acme.fault=key_revoked"),
                    _ => null,
                }
                : null;

        public AcpStatedFailure? ReadTurnEnd(JsonElement answer, TimeProvider time) =>
            answer.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("acme/verdict", out var verdict) &&
            verdict.GetString() == "denied"
                ? new AcpStatedFailure("acme.verdict=denied", "acme denied the turn", new AgentFailure(AgentFailureKind.Access, "acme.verdict=denied"))
                : null;
    }

    private static FakeProcessRunner Agent(params string[] promptReplies) => new()
    {
        ExchangeHandler = line => JsonNode.Parse(line)!["method"]?.GetValue<string>() switch
        {
            "initialize" => ["""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentCapabilities":{},"authMethods":[]}}"""],
            "session/new" => ["""{"jsonrpc":"2.0","id":2,"result":{"sessionId":"s1"}}"""],
            "session/prompt" => promptReplies,
            _ => [],
        },
    };

    private static string Update(string kind, string body) =>
        """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s1","update":{"sessionUpdate":"KIND",BODY}}}"""
            .Replace("KIND", kind).Replace("BODY", body[1..^1]);

    private static string Throttled(long until = 1893456300) =>
        Update("acme_notice", $$$$"""{"_meta":{"acme/state":{"name":"throttled","until":{{{{until}}}}}}}""");

    private static string Recovered() => Update("acme_notice", """{"_meta":{"acme/state":{"name":"recovered"}}}""");

    private static string PromptError(string data) =>
        $$$"""{"jsonrpc":"2.0","id":3,"error":{"code":-32603,"message":"Internal error","data":{{{data}}}}}""";

    private static string PromptEnded(string meta = "") =>
        $$$"""{"jsonrpc":"2.0","id":3,"result":{"stopReason":"end_turn"{{{meta}}}}}""";

    private static AcpAgentSession Session(FakeProcessRunner runner, IEnumerable<IAcpExtensionReader>? readers) =>
        new(runner, new LyntaiOptions(), "agent", extensions: readers);

    private static async Task<List<AgentStreamEvent>> Run(IAgentSession session) =>
        await session.StreamAsync(new AgentSessionOptions { Prompt = "go", WorkingDirectory = TestPaths.TestScratchDir }).ToListAsync();

    private static SessionEnded Ended(IEnumerable<AgentStreamEvent> events) => Assert.IsType<SessionEnded>(events.Last());

    // ── a reader the host registers ──────────────────────────────────────────

    [Fact]
    public async Task A_registered_reader_reads_an_error_field_no_shipped_reader_knows()
    {
        var runner = Agent(PromptError("""{"acmeFault":"quota_gone"}"""));

        var ended = Ended(await Run(Session(runner, [.. AcpExtensionReaders.Default, new AcmeReader()])));

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.Equal(AgentFailureKind.Limit, ended.Failure!.Kind);
        Assert.Equal("acme.fault=quota_gone", ended.Failure.By);
        Assert.Equal(RecoveryKind.ResetAt, ended.Failure.Restriction!.Recovery.Kind);
    }

    [Fact]
    public async Task The_shipped_readers_given_the_stand_ins_keys_read_nothing()
    {
        var runner = Agent(Throttled(), PromptError("""{"acmeFault":"quota_gone"}"""));

        var events = await Run(Session(runner, null));

        var ended = Ended(events);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32603", ended.Failure.By);
        Assert.Null(ended.Failure.Restriction);
        Assert.DoesNotContain(events, e => e is Thinking);
    }

    [Fact]
    public async Task A_failure_held_from_an_update_is_read_with_the_error_that_ends_the_turn()
    {
        var runner = Agent(Throttled(), PromptError("""{"acmeFault":"unheard-of"}"""));

        var ended = Ended(await Run(Session(runner, [new AcmeReader()])));

        Assert.Equal(AgentFailureKind.Limit, ended.Failure!.Kind);
        Assert.Equal("acme.state=throttled", ended.Failure.By);
        Assert.Equal(RestrictionRecovery.RetryNotBefore(DateTimeOffset.FromUnixTimeSeconds(1893456300)), ended.Failure.Restriction!.Recovery);
    }

    [Fact]
    public async Task A_reading_without_a_failure_clears_what_the_same_reader_held()
    {
        var runner = Agent(Throttled(), Recovered(), PromptError("""{"acmeFault":"unheard-of"}"""));

        var ended = Ended(await Run(Session(runner, [new AcmeReader()])));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32603", ended.Failure.By);
    }

    [Fact]
    public async Task A_held_failure_is_not_a_failure_when_the_turn_succeeds()
    {
        var runner = Agent(Throttled(), PromptEnded());

        var ended = Ended(await Run(Session(runner, [new AcmeReader()])));

        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Null(ended.Failure);
    }

    [Fact]
    public async Task A_reader_yields_events_after_the_ones_the_door_read_and_cannot_start_or_end_the_session()
    {
        var runner = Agent(Throttled(), PromptEnded());

        var events = await Run(Session(runner, [new AcmeReader(), new SessionEndingReader()]));

        var kinds = events.Select(e => e.GetType()).ToList();
        Assert.Equal([typeof(SessionStarted), typeof(Thinking), typeof(SessionEnded)], kinds);
        Assert.Equal("s1", Assert.IsType<SessionStarted>(events[0]).SessionId);
        Assert.Equal(ProviderVerdict.Ok, Ended(events).Verdict);
    }

    [Fact]
    public async Task A_failure_the_agent_states_on_a_turn_it_ended_normally_is_a_failed_turn()
    {
        var runner = Agent(PromptEnded(""","_meta":{"acme/verdict":"denied"}"""));

        var ended = Ended(await Run(Session(runner, [new AcmeReader()])));

        Assert.True(ended.IsError);
        Assert.Equal("session-failure", ended.Subtype);
        Assert.Equal("acme denied the turn", ended.Diagnostic);
        Assert.Equal(AgentFailureKind.Access, ended.Failure!.Kind);
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
    }

    // ── a door narrowed to what its agent speaks ─────────────────────────────

    [Theory]
    [InlineData("""{"errorKind":"rate_limit"}""", "acp.data.errorKind=rate_limit")]
    [InlineData("""{"codexErrorInfo":"usageLimitExceeded"}""", "acp.data.codexErrorInfo=usageLimitExceeded")]
    public async Task A_door_named_a_set_reads_only_that_set(string data, string by)
    {
        var withBoth = Ended(await Run(Session(Agent(PromptError(data)), AcpExtensionReaders.Default)));
        Assert.Equal(by, withBoth.Failure!.By);

        var narrowed = new IAcpExtensionReader[] { by.Contains("errorKind") ? AcpExtensionReaders.Codex : AcpExtensionReaders.Claude };
        var ended = Ended(await Run(Session(Agent(PromptError(data)), narrowed)));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32603", ended.Failure.By);
    }

    [Fact]
    public async Task An_empty_set_reads_no_extension_but_the_protocols_own_sign_in_code_and_the_turns_stop_reason()
    {
        var limit = Ended(await Run(Session(Agent(PromptError("""{"errorKind":"rate_limit"}""")), [])));
        Assert.Equal("acp.error.code=-32603", limit.Failure!.By);

        var signIn = Ended(await Run(Session(Agent("""{"jsonrpc":"2.0","id":3,"error":{"code":-32000,"message":"Authentication required"}}"""), [])));
        Assert.Equal(AgentFailureKind.Access, signIn.Failure!.Kind);

        var typedEnd = Ended(await Run(Session(Agent(PromptEnded(""","_meta":{"jetbrains":{"air":{"sessionFailure":{"category":"access","severity":"error","title":"x"}}}}""")), [])));
        Assert.Equal(ProviderVerdict.Ok, typedEnd.Verdict);
    }

    // ── a reader that misreads a frame ───────────────────────────────────────

    [Fact]
    public async Task A_reader_that_throws_a_shape_fault_loses_its_own_reading_and_not_the_doors()
    {
        var runner = Agent(
            Update("agent_message_chunk", """{"content":{"type":"text","text":"hello"}}"""),
            PromptError("""{"errorKind":"rate_limit"}"""));

        var events = await Run(Session(runner, [new FaultingReader(), AcpExtensionReaders.Claude]));

        Assert.Equal("hello", Assert.IsType<TextDelta>(events[1]).Text);
        Assert.Equal("acp.data.errorKind=rate_limit", Ended(events).Failure!.By);
    }

    [Fact]
    public void A_null_reader_is_refused_where_the_set_is_named()
    {
        Assert.Throws<ArgumentException>(() => Session(Agent(), [AcpExtensionReaders.Claude, null!]));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("acme-acp", "acme", extensions: [null!])));
    }

    // ── through the registration ─────────────────────────────────────────────

    [Fact]
    public async Task The_registration_gives_each_door_its_own_set()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(Agent(PromptError("""{"acmeFault":"key_revoked","errorKind":"rate_limit"}""")));
        services.AddLyntai(b => b
            .AddAcpAgentSession("acme-acp", "acme", extensions: [new AcmeReader()])
            .AddAcpAgentSession("plain-acp", "plain"));
        using var provider = services.BuildServiceProvider();

        var acme = Ended(await Run(provider.GetRequiredKeyedService<IAgentSession>("acme-acp")));
        var plain = Ended(await Run(provider.GetRequiredKeyedService<IAgentSession>("plain-acp")));

        Assert.Equal("acme.fault=key_revoked", acme.Failure!.By);   // the set named: the stand-in's, and no shipped reader
        Assert.Equal(AgentFailureKind.Limit, plain.Failure!.Kind);   // no set named: every shipped reader, as before
        Assert.Equal("acp.data.errorKind=rate_limit", plain.Failure.By);
    }

    private sealed class SessionEndingReader : IAcpExtensionReader
    {
        public AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time) =>
            new([new SessionStarted("forged"), new SessionEnded(ProviderVerdict.Failed, true, null, "forged", null, "forged")]);
    }

    private sealed class FaultingReader : IAcpExtensionReader
    {
        public AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time) =>
            throw new InvalidOperationException("this agent's field is not the kind it was read as");

        public AgentFailure? ReadError(JsonElement error, TimeProvider time) =>
            throw new FormatException("neither is this one");
    }
}
